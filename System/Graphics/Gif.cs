using System;
using System.IO;
using System.Text;

namespace Cosmos.Kernel.System.Graphics
{
    /// <summary>
    /// Represents a single decoded frame in an animated GIF.
    /// </summary>
    public class GifFrame
    {
        public Bitmap Image { get; set; }
        public int DelayMs { get; set; }
        public int DisposalMethod { get; set; }
    }

    /// <summary>
    /// Represents a GIF format image (GIF87a / GIF89a) inheriting from Cosmos Image.
    /// </summary>
    public class Gif : Image
    {
        public global::System.Collections.Generic.List<GifFrame> Frames { get; } = new global::System.Collections.Generic.List<GifFrame>();

        public Gif(string path) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("GIF file not found", path);

            byte[] data = File.ReadAllBytes(path);
            Decode(data);
        }

        public Gif(byte[] imageData) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (imageData == null || imageData.Length == 0)
                throw new ArgumentNullException(nameof(imageData));

            Decode(imageData);
        }

        public Gif(Stream stream) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            Decode(ms.ToArray());
        }

        public Gif(int width, int height) : base(width, height, ColorDepth.ColorDepth32)
        {
            ImageAccessor.SetRawData(this, new int[width * height]);
        }

        private void Decode(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var br = new BinaryReader(ms);
            byte[] sig = br.ReadBytes(6);
            string sigStr = Encoding.ASCII.GetString(sig);
            if (sigStr != "GIF87a" && sigStr != "GIF89a")
                throw new InvalidDataException("Not a valid GIF image");

            int screenW = br.ReadUInt16();
            int screenH = br.ReadUInt16();
            byte fields = br.ReadByte();
            byte bgIndex = br.ReadByte();
            byte aspect = br.ReadByte();

            bool hasGct = (fields & 0x80) != 0;
            int gctSize = 1 << ((fields & 7) + 1);
            int[]? gct = null;
            if (hasGct)
            {
                gct = new int[gctSize];
                for (int i = 0; i < gctSize; i++)
                {
                    byte r = br.ReadByte();
                    byte g = br.ReadByte();
                    byte b = br.ReadByte();
                    gct[i] = unchecked((int)(0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
                }
            }

            int[] result = new int[screenW * screenH];
            int transparentColor = -1;
            int currentDelay = 100;
            int currentDisposal = 0;

            while (ms.Position < ms.Length)
            {
                byte blockType = br.ReadByte();
                if (blockType == 0x3B) break; // Trailer

                if (blockType == 0x21) // Extension
                {
                    byte label = br.ReadByte();
                    if (label == 0xF9) // Graphic Control Extension
                    {
                        byte blockSize = br.ReadByte();
                        byte gceFlags = br.ReadByte();
                        int delay = br.ReadUInt16();
                        byte transIdx = br.ReadByte();
                        br.ReadByte(); // Block terminator
                        if ((gceFlags & 1) != 0) transparentColor = transIdx;
                        currentDisposal = (gceFlags >> 2) & 7;
                        currentDelay = delay > 0 ? (delay * 10) : 100;
                    }
                    else
                    {
                        while (true)
                        {
                            byte bsz = br.ReadByte();
                            if (bsz == 0) break;
                            br.ReadBytes(bsz);
                        }
                    }
                }
                else if (blockType == 0x2C) // Image Descriptor
                {
                    int imgX = br.ReadUInt16();
                    int imgY = br.ReadUInt16();
                    int imgW = br.ReadUInt16();
                    int imgH = br.ReadUInt16();
                    byte imgFields = br.ReadByte();
                    bool hasLct = (imgFields & 0x80) != 0;
                    bool interlace = (imgFields & 0x40) != 0;
                    int[]? palette = gct;
                    if (hasLct)
                    {
                        int lctSize = 1 << ((imgFields & 7) + 1);
                        palette = new int[lctSize];
                        for (int i = 0; i < lctSize; i++)
                        {
                            byte r = br.ReadByte();
                            byte g = br.ReadByte();
                            byte b = br.ReadByte();
                            palette[i] = unchecked((int)(0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
                        }
                    }

                    byte minCodeSize = br.ReadByte();
                    using var pixelMs = new MemoryStream();
                    while (true)
                    {
                        byte bsz = br.ReadByte();
                        if (bsz == 0) break;
                        byte[] sub = br.ReadBytes(bsz);
                        pixelMs.Write(sub, 0, sub.Length);
                    }

                    byte[] lzwData = pixelMs.ToArray();
                    byte[] decodedIndices = LzwDecode(lzwData, minCodeSize, imgW * imgH);

                    int srcIdx = 0;
                    if (!interlace)
                    {
                        for (int y = 0; y < imgH; y++)
                        {
                            int row = (imgY + y) * screenW + imgX;
                            for (int x = 0; x < imgW; x++)
                            {
                                byte idx = (srcIdx < decodedIndices.Length) ? decodedIndices[srcIdx++] : (byte)0;
                                if (idx != transparentColor && palette != null && idx < palette.Length)
                                    result[row + x] = palette[idx];
                            }
                        }
                    }
                    else
                    {
                        int[] startRows = { 0, 4, 2, 1 };
                        int[] stepRows = { 8, 8, 4, 2 };
                        for (int pass = 0; pass < 4; pass++)
                        {
                            for (int y = startRows[pass]; y < imgH; y += stepRows[pass])
                            {
                                int row = (imgY + y) * screenW + imgX;
                                for (int x = 0; x < imgW; x++)
                                {
                                    byte idx = (srcIdx < decodedIndices.Length) ? decodedIndices[srcIdx++] : (byte)0;
                                    if (idx != transparentColor && palette != null && idx < palette.Length)
                                        result[row + x] = palette[idx];
                                }
                            }
                        }
                    }

                    Bitmap frameBmp = new Bitmap(screenW, screenH, ColorDepth.ColorDepth32);
                    if (frameBmp.RawData != null && frameBmp.RawData.Length == result.Length)
                    {
                        Array.Copy(result, frameBmp.RawData, result.Length);
                    }
                    Frames.Add(new GifFrame
                    {
                        Image = frameBmp,
                        DelayMs = currentDelay > 0 ? currentDelay : 100,
                        DisposalMethod = currentDisposal
                    });

                    if (Frames.Count >= 500) break; // safety guard

                    if (currentDisposal == 2)
                    {
                        Array.Clear(result, 0, result.Length);
                    }
                    transparentColor = -1;
                    currentDelay = 100;
                    currentDisposal = 0;
                }
            }

            if (Frames.Count == 0)
            {
                Bitmap singleFrame = new Bitmap(screenW, screenH, ColorDepth.ColorDepth32);
                if (singleFrame.RawData != null && singleFrame.RawData.Length == result.Length)
                {
                    Array.Copy(result, singleFrame.RawData, result.Length);
                }
                Frames.Add(new GifFrame { Image = singleFrame, DelayMs = 100, DisposalMethod = 0 });
            }

            int[] initialPixels = (Frames.Count > 0 && Frames[0].Image.RawData != null) ? Frames[0].Image.RawData : result;
            ImageAccessor.Initialize(this, screenW, screenH, initialPixels, ColorDepth.ColorDepth32);
        }

        private static byte[] LzwDecode(byte[] input, int minCodeSize, int expectedCount)
        {
            byte[] output = new byte[expectedCount];
            int outPos = 0;

            int clearCode = 1 << minCodeSize;
            int eoiCode = clearCode + 1;
            int codeSize = minCodeSize + 1;
            int maxCode = 1 << codeSize;

            int[] prefix = new int[4096];
            byte[] suffix = new byte[4096];
            byte[] pixelStack = new byte[4097];

            int nextCode = eoiCode + 1;
            int oldCode = -1;

            int bitPos = 0;
            int ReadCode()
            {
                int bitOffset = bitPos;
                int byteOffset = bitOffset >> 3;
                if (byteOffset >= input.Length) return eoiCode;
                int shift = bitOffset & 7;
                uint val = input[byteOffset];
                if (byteOffset + 1 < input.Length) val |= ((uint)input[byteOffset + 1]) << 8;
                if (byteOffset + 2 < input.Length) val |= ((uint)input[byteOffset + 2]) << 16;
                int code = (int)((val >> shift) & ((1u << codeSize) - 1u));
                bitPos += codeSize;
                return code;
            }

            while (outPos < expectedCount)
            {
                int code = ReadCode();
                if (code == eoiCode) break;
                if (code == clearCode)
                {
                    codeSize = minCodeSize + 1;
                    maxCode = 1 << codeSize;
                    nextCode = eoiCode + 1;
                    oldCode = -1;
                    continue;
                }

                int top = 0;
                int cur = code;
                if (cur >= nextCode)
                {
                    if (oldCode >= 0)
                        pixelStack[top++] = (byte)suffix[oldCode];
                    cur = oldCode;
                }

                while (cur >= clearCode)
                {
                    pixelStack[top++] = suffix[cur];
                    cur = prefix[cur];
                }
                pixelStack[top++] = (byte)cur;

                if (oldCode != -1 && nextCode < 4096)
                {
                    prefix[nextCode] = oldCode;
                    suffix[nextCode] = (byte)cur;
                    nextCode++;
                    if (nextCode >= maxCode && codeSize < 12)
                    {
                        codeSize++;
                        maxCode = 1 << codeSize;
                    }
                }
                oldCode = code;

                while (top > 0 && outPos < expectedCount)
                {
                    output[outPos++] = pixelStack[--top];
                }
            }

            return output;
        }
    }
}
