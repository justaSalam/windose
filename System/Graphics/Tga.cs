using System;
using System.IO;

namespace Cosmos.Kernel.System.Graphics
{
    /// <summary>
    /// Represents a Truevision TGA image inheriting from Cosmos Image.
    /// Supports uncompressed and RLE compressed RGB, RGBA, and grayscale.
    /// </summary>
    public class Tga : Image
    {
        public Tga(string path) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("TGA file not found", path);

            byte[] data = File.ReadAllBytes(path);
            Decode(data);
        }

        public Tga(byte[] imageData) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (imageData == null || imageData.Length == 0)
                throw new ArgumentNullException(nameof(imageData));

            Decode(imageData);
        }

        public Tga(Stream stream) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            Decode(ms.ToArray());
        }

        public Tga(int width, int height) : base(width, height, ColorDepth.ColorDepth32)
        {
            ImageAccessor.SetRawData(this, new int[width * height]);
        }

        private void Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 18)
                throw new InvalidDataException("Data too small to be a TGA image");

            int idLen = bytes[0];
            int colorMapType = bytes[1];
            int imageType = bytes[2];
            int w = bytes[12] | (bytes[13] << 8);
            int h = bytes[14] | (bytes[15] << 8);
            int bpp = bytes[16];
            byte descriptor = bytes[17];
            bool topDown = (descriptor & 0x20) != 0;

            if (w <= 0 || h <= 0)
                throw new InvalidDataException("Invalid TGA dimensions");

            int offset = 18 + idLen;

            // Optional Color Map (Palette)
            int[]? palette = null;
            if (colorMapType == 1)
            {
                int colorMapFirstEntry = bytes[3] | (bytes[4] << 8);
                int colorMapLength = bytes[5] | (bytes[6] << 8);
                int colorMapEntrySize = bytes[7];
                palette = new int[colorMapLength];
                int entryBytes = (colorMapEntrySize + 7) / 8;

                for (int i = 0; i < colorMapLength; i++)
                {
                    if (entryBytes == 3)
                    {
                        byte b = bytes[offset++];
                        byte g = bytes[offset++];
                        byte r = bytes[offset++];
                        palette[i] = unchecked((int)(0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | b));
                    }
                    else if (entryBytes == 4)
                    {
                        byte b = bytes[offset++];
                        byte g = bytes[offset++];
                        byte r = bytes[offset++];
                        byte a = bytes[offset++];
                        palette[i] = (a << 24) | (r << 16) | (g << 8) | b;
                    }
                    else
                    {
                        offset += entryBytes;
                    }
                }
            }

            int total = w * h;
            int[] pixels = new int[total];
            bool isRle = (imageType == 9 || imageType == 10 || imageType == 11);
            int bytesPerPixel = (bpp + 7) / 8;

            if (!isRle)
            {
                for (int y = 0; y < h; y++)
                {
                    int targetY = topDown ? y : (h - 1 - y);
                    int row = targetY * w;
                    for (int x = 0; x < w; x++)
                    {
                        pixels[row + x] = ReadPixel(bytes, ref offset, bpp, palette);
                    }
                }
            }
            else
            {
                int px = 0;
                while (px < total && offset < bytes.Length)
                {
                    byte packet = bytes[offset++];
                    int count = (packet & 0x7F) + 1;
                    if ((packet & 0x80) != 0) // RLE packet
                    {
                        int pixel = ReadPixel(bytes, ref offset, bpp, palette);
                        for (int i = 0; i < count && px < total; i++)
                        {
                            int x = px % w;
                            int y = px / w;
                            int targetY = topDown ? y : (h - 1 - y);
                            pixels[targetY * w + x] = pixel;
                            px++;
                        }
                    }
                    else // Raw packet
                    {
                        for (int i = 0; i < count && px < total; i++)
                        {
                            int pixel = ReadPixel(bytes, ref offset, bpp, palette);
                            int x = px % w;
                            int y = px / w;
                            int targetY = topDown ? y : (h - 1 - y);
                            pixels[targetY * w + x] = pixel;
                            px++;
                        }
                    }
                }
            }

            ImageAccessor.Initialize(this, w, h, pixels, ColorDepth.ColorDepth32);
        }

        private static int ReadPixel(byte[] bytes, ref int offset, int bpp, int[]? palette)
        {
            if (offset >= bytes.Length) return 0;
            if (bpp == 32)
            {
                int p = (bytes[offset + 3] << 24) | (bytes[offset + 2] << 16) | (bytes[offset + 1] << 8) | bytes[offset];
                offset += 4;
                return p;
            }
            if (bpp == 24)
            {
                int p = unchecked((int)(0xFF000000 | ((uint)bytes[offset + 2] << 16) | ((uint)bytes[offset + 1] << 8) | bytes[offset]));
                offset += 3;
                return p;
            }
            if (bpp == 16)
            {
                ushort val = (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
                offset += 2;
                int r = ((val >> 10) & 0x1F) * 255 / 31;
                int g = ((val >> 5) & 0x1F) * 255 / 31;
                int b = (val & 0x1F) * 255 / 31;
                int a = (val & 0x8000) != 0 ? 255 : 0;
                return (a << 24) | (r << 16) | (g << 8) | b;
            }
            if (bpp == 8)
            {
                byte val = bytes[offset++];
                if (palette != null && val < palette.Length)
                    return palette[val];
                return unchecked((int)(0xFF000000 | ((uint)val << 16) | ((uint)val << 8) | val));
            }
            return 0;
        }

        public void Save(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            byte[] bytes = Encode();
            File.WriteAllBytes(path, bytes);
        }

        public void Save(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            byte[] bytes = Encode();
            stream.Write(bytes, 0, bytes.Length);
        }

        public byte[] Encode()
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            byte[] hdr = new byte[18];
            hdr[2] = 2; // uncompressed true-color
            hdr[12] = (byte)Width; hdr[13] = (byte)(Width >> 8);
            hdr[14] = (byte)Height; hdr[15] = (byte)(Height >> 8);
            hdr[16] = 32; // 32 bpp
            hdr[17] = 0x20; // top-down
            bw.Write(hdr);

            for (int i = 0; i < RawData.Length; i++)
            {
                int color = RawData[i];
                bw.Write((byte)(color & 0xFF));         // B
                bw.Write((byte)((color >> 8) & 0xFF));  // G
                bw.Write((byte)((color >> 16) & 0xFF)); // R
                bw.Write((byte)((color >> 24) & 0xFF)); // A
            }

            return ms.ToArray();
        }
    }
}
