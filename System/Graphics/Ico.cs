using System;
using System.IO;

namespace Cosmos.Kernel.System.Graphics
{
    /// <summary>
    /// Represents a Windows Icon (.ico) image inheriting from Cosmos Image.
    /// Supports embedded BMP (with 1-bit AND transparency masks) and embedded PNG frames.
    /// </summary>
    public class Ico : Image
    {
        public Ico(string path) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("ICO file not found", path);

            byte[] data = File.ReadAllBytes(path);
            Decode(data);
        }

        public Ico(byte[] imageData) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (imageData == null || imageData.Length == 0)
                throw new ArgumentNullException(nameof(imageData));

            Decode(imageData);
        }

        public Ico(Stream stream) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            Decode(ms.ToArray());
        }

        public Ico(int width, int height) : base(width, height, ColorDepth.ColorDepth32)
        {
            ImageAccessor.SetRawData(this, new int[width * height]);
        }

        private void Decode(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var br = new BinaryReader(ms);
            ushort reserved = br.ReadUInt16();
            ushort type = br.ReadUInt16();
            ushort count = br.ReadUInt16();
            if (reserved != 0 || type != 1 || count == 0)
                throw new InvalidDataException("Not a valid ICO image");

            int bestIndex = 0;
            int bestArea = -1;
            int bestOffset = 0;
            int bestSize = 0;

            for (int i = 0; i < count; i++)
            {
                byte w = br.ReadByte();
                byte h = br.ReadByte();
                int realW = (w == 0) ? 256 : w;
                int realH = (h == 0) ? 256 : h;
                byte colors = br.ReadByte();
                byte res = br.ReadByte();
                ushort planes = br.ReadUInt16();
                ushort bpp = br.ReadUInt16();
                int size = br.ReadInt32();
                int offset = br.ReadInt32();

                int area = realW * realH;
                if (area > bestArea)
                {
                    bestArea = area;
                    bestIndex = i;
                    bestOffset = offset;
                    bestSize = size;
                }
            }

            ms.Position = bestOffset;
            byte[] imgData = br.ReadBytes(bestSize);

            // 1. Check if embedded payload is PNG
            if (imgData.Length >= 8 && imgData[0] == 0x89 && imgData[1] == (byte)'P' && imgData[2] == (byte)'N' && imgData[3] == (byte)'G')
            {
                using var pngMs = new MemoryStream(imgData);
                var png = new Png(pngMs);
                ImageAccessor.Initialize(this, (int)png.Width, (int)png.Height, png.RawData, ColorDepth.ColorDepth32);
                return;
            }

            // 2. Embedded BMP DIB
            using var dibMs = new MemoryStream(imgData);
            using var dibBr = new BinaryReader(dibMs);
            int headerSize = dibBr.ReadInt32();
            int wDIB = dibBr.ReadInt32();
            int fullHeight = dibBr.ReadInt32();
            int hDIB = fullHeight / 2; // ICO BMPs have 2x height (image + AND mask)
            ushort dibPlanes = dibBr.ReadUInt16();
            ushort dibBpp = dibBr.ReadUInt16();
            int compression = dibBr.ReadInt32();
            int imageSize = dibBr.ReadInt32();
            int xpels = dibBr.ReadInt32();
            int ypels = dibBr.ReadInt32();
            int clrUsed = dibBr.ReadInt32();
            int clrImp = dibBr.ReadInt32();

            int[]? palette = null;
            if (dibBpp <= 8)
            {
                int palCount = clrUsed > 0 ? clrUsed : (1 << dibBpp);
                palette = new int[palCount];
                for (int i = 0; i < palCount; i++)
                {
                    byte b = dibBr.ReadByte();
                    byte g = dibBr.ReadByte();
                    byte r = dibBr.ReadByte();
                    dibBr.ReadByte(); // reserved
                    palette[i] = unchecked((int)(0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | b));
                }
            }

            int[] result = new int[wDIB * hDIB];
            int rowStride = ((wDIB * dibBpp + 31) / 32) * 4;
            byte[] xorData = dibBr.ReadBytes(rowStride * hDIB);

            int maskStride = ((wDIB + 31) / 32) * 4;
            byte[] andData = dibBr.ReadBytes(maskStride * hDIB);

            for (int y = 0; y < hDIB; y++)
            {
                int srcY = hDIB - 1 - y; // Bottom-up
                int xorRow = srcY * rowStride;
                int andRow = srcY * maskStride;
                int dstRow = y * wDIB;

                for (int x = 0; x < wDIB; x++)
                {
                    int pixel = 0;
                    if (dibBpp == 32)
                    {
                        int o = xorRow + x * 4;
                        pixel = (xorData[o + 3] << 24) | (xorData[o + 2] << 16) | (xorData[o + 1] << 8) | xorData[o];
                    }
                    else if (dibBpp == 24)
                    {
                        int o = xorRow + x * 3;
                        pixel = unchecked((int)(0xFF000000 | ((uint)xorData[o + 2] << 16) | ((uint)xorData[o + 1] << 8) | xorData[o]));
                    }
                    else if (dibBpp == 8 && palette != null)
                    {
                        pixel = palette[xorData[xorRow + x]];
                    }
                    else if (dibBpp == 4 && palette != null)
                    {
                        byte b = xorData[xorRow + (x >> 1)];
                        int idx = (x & 1) == 0 ? (b >> 4) : (b & 0xF);
                        pixel = palette[idx];
                    }
                    else if (dibBpp == 1 && palette != null)
                    {
                        byte b = xorData[xorRow + (x >> 3)];
                        int idx = (b >> (7 - (x & 7))) & 1;
                        pixel = palette[idx];
                    }

                    // Apply 1-bit AND transparency mask
                    if (andRow + (x >> 3) < andData.Length)
                    {
                        int maskByte = andData[andRow + (x >> 3)];
                        int maskBit = (maskByte >> (7 - (x & 7))) & 1;
                        if (maskBit != 0)
                        {
                            pixel = 0; // Transparent
                        }
                    }
                    result[dstRow + x] = pixel;
                }
            }

            ImageAccessor.Initialize(this, wDIB, hDIB, result, ColorDepth.ColorDepth32);
        }
    }
}
