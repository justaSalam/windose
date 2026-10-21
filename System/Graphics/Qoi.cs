using System;
using System.IO;

namespace Cosmos.Kernel.System.Graphics
{
    /// <summary>
    /// Represents a QOI (Quite OK Image format) image inheriting from Cosmos Image.
    /// Supports both high-speed decoding and encoding.
    /// </summary>
    public class Qoi : Image
    {
        public Qoi(string path) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("QOI file not found", path);

            byte[] data = File.ReadAllBytes(path);
            Decode(data);
        }

        public Qoi(byte[] imageData) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (imageData == null || imageData.Length == 0)
                throw new ArgumentNullException(nameof(imageData));

            Decode(imageData);
        }

        public Qoi(Stream stream) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            Decode(ms.ToArray());
        }

        public Qoi(int width, int height) : base(width, height, ColorDepth.ColorDepth32)
        {
            ImageAccessor.SetRawData(this, new int[width * height]);
        }

        private void Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 14)
                throw new InvalidDataException("Data too small to be a QOI image");

            if (bytes[0] != 'q' || bytes[1] != 'o' || bytes[2] != 'i' || bytes[3] != 'f')
                throw new InvalidDataException("Invalid QOI signature");

            int w = (bytes[4] << 24) | (bytes[5] << 16) | (bytes[6] << 8) | bytes[7];
            int h = (bytes[8] << 24) | (bytes[9] << 16) | (bytes[10] << 8) | bytes[11];
            byte channels = bytes[12];
            byte colorspace = bytes[13];

            if (w <= 0 || h <= 0)
                throw new InvalidDataException("Invalid QOI dimensions");

            int total = w * h;
            int[] pixels = new int[total];
            int[] indexTable = new int[64];

            int r = 0, g = 0, b = 0, a = 255;
            int p = 14;
            int px = 0;
            int end = bytes.Length - 8;

            while (px < total && p < end)
            {
                byte b1 = bytes[p++];
                if (b1 == 0xFE) // QOI_OP_RGB
                {
                    r = bytes[p++];
                    g = bytes[p++];
                    b = bytes[p++];
                }
                else if (b1 == 0xFF) // QOI_OP_RGBA
                {
                    r = bytes[p++];
                    g = bytes[p++];
                    b = bytes[p++];
                    a = bytes[p++];
                }
                else
                {
                    int tag = b1 >> 6;
                    if (tag == 0) // QOI_OP_INDEX
                    {
                        int idx = b1 & 0x3F;
                        int color = indexTable[idx];
                        a = (color >> 24) & 0xFF;
                        r = (color >> 16) & 0xFF;
                        g = (color >> 8) & 0xFF;
                        b = color & 0xFF;
                    }
                    else if (tag == 1) // QOI_OP_DIFF
                    {
                        r = (r + ((b1 >> 4) & 0x03) - 2) & 0xFF;
                        g = (g + ((b1 >> 2) & 0x03) - 2) & 0xFF;
                        b = (b + (b1 & 0x03) - 2) & 0xFF;
                    }
                    else if (tag == 2) // QOI_OP_LUMA
                    {
                        byte b2 = bytes[p++];
                        int dg = (b1 & 0x3F) - 32;
                        int dr = ((b2 >> 4) & 0x0F) - 8 + dg;
                        int db = (b2 & 0x0F) - 8 + dg;
                        r = (r + dr) & 0xFF;
                        g = (g + dg) & 0xFF;
                        b = (b + db) & 0xFF;
                    }
                    else if (tag == 3) // QOI_OP_RUN
                    {
                        int run = (b1 & 0x3F) + 1;
                        int current = unchecked((int)(((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
                        for (int i = 0; i < run && px < total; i++)
                        {
                            pixels[px++] = current;
                        }
                        continue;
                    }
                }

                int pixel = unchecked((int)(((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
                int hash = (r * 3 + g * 5 + b * 7 + a * 11) % 64;
                indexTable[hash] = pixel;
                pixels[px++] = pixel;
            }

            ImageAccessor.Initialize(this, w, h, pixels, ColorDepth.ColorDepth32);
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

            // Magic
            bw.Write(new byte[] { (byte)'q', (byte)'o', (byte)'i', (byte)'f' });
            bw.Write((byte)(Width >> 24)); bw.Write((byte)(Width >> 16));
            bw.Write((byte)(Width >> 8)); bw.Write((byte)Width);
            bw.Write((byte)(Height >> 24)); bw.Write((byte)(Height >> 16));
            bw.Write((byte)(Height >> 8)); bw.Write((byte)Height);
            bw.Write((byte)4); // RGBA
            bw.Write((byte)0); // sRGB

            int[] index = new int[64];
            int prevR = 0, prevG = 0, prevB = 0, prevA = 255;
            int run = 0;

            for (int i = 0; i < RawData.Length; i++)
            {
                int color = RawData[i];
                int a = (color >> 24) & 0xFF;
                int r = (color >> 16) & 0xFF;
                int g = (color >> 8) & 0xFF;
                int b = color & 0xFF;

                if (r == prevR && g == prevG && b == prevB && a == prevA)
                {
                    run++;
                    if (run == 62 || i == RawData.Length - 1)
                    {
                        bw.Write((byte)(0xC0 | (run - 1)));
                        run = 0;
                    }
                }
                else
                {
                    if (run > 0)
                    {
                        bw.Write((byte)(0xC0 | (run - 1)));
                        run = 0;
                    }

                    int hash = (r * 3 + g * 5 + b * 7 + a * 11) % 64;
                    if (index[hash] == color)
                    {
                        bw.Write((byte)(0x00 | hash));
                    }
                    else
                    {
                        index[hash] = color;
                        if (a == prevA)
                        {
                            int vr = r - prevR;
                            int vg = g - prevG;
                            int vb = b - prevB;
                            int vgr = vr - vg;
                            int vgb = vb - vg;

                            if (vr >= -2 && vr <= 1 && vg >= -2 && vg <= 1 && vb >= -2 && vb <= 1)
                            {
                                bw.Write((byte)(0x40 | ((vr + 2) << 4) | ((vg + 2) << 2) | (vb + 2)));
                            }
                            else if (vgr >= -8 && vgr <= 7 && vg >= -32 && vg <= 31 && vgb >= -8 && vgb <= 7)
                            {
                                bw.Write((byte)(0x80 | (vg + 32)));
                                bw.Write((byte)(((vgr + 8) << 4) | (vgb + 8)));
                            }
                            else
                            {
                                bw.Write((byte)0xFE);
                                bw.Write((byte)r);
                                bw.Write((byte)g);
                                bw.Write((byte)b);
                            }
                        }
                        else
                        {
                            bw.Write((byte)0xFF);
                            bw.Write((byte)r);
                            bw.Write((byte)g);
                            bw.Write((byte)b);
                            bw.Write((byte)a);
                        }
                    }

                    prevR = r; prevG = g; prevB = b; prevA = a;
                }
            }

            // 8-byte end padding
            bw.Write(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });
            return ms.ToArray();
        }
    }
}
