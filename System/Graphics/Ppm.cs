using System;
using System.IO;
using System.Text;

namespace Cosmos.Kernel.System.Graphics
{
    /// <summary>
    /// Represents a Netpbm PPM image (supporting P3 ASCII and P6 Binary) inheriting from Cosmos Image.
    /// </summary>
    public class Ppm : Image
    {
        public Ppm(string path) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("PPM file not found", path);

            byte[] data = File.ReadAllBytes(path);
            Decode(data);
        }

        public Ppm(byte[] imageData) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (imageData == null || imageData.Length == 0)
                throw new ArgumentNullException(nameof(imageData));

            Decode(imageData);
        }

        public Ppm(Stream stream) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            Decode(ms.ToArray());
        }

        public Ppm(int width, int height) : base(width, height, ColorDepth.ColorDepth32)
        {
            ImageAccessor.SetRawData(this, new int[width * height]);
        }

        private void Decode(byte[] bytes)
        {
            int p = 0;
            string token = ReadToken(bytes, ref p);
            if (token != "P3" && token != "P6")
                throw new InvalidDataException("Not a valid PPM image (expected P3 or P6)");

            bool isP6 = token == "P6";
            int w = int.Parse(ReadToken(bytes, ref p));
            int h = int.Parse(ReadToken(bytes, ref p));
            int maxVal = int.Parse(ReadToken(bytes, ref p));

            if (w <= 0 || h <= 0)
                throw new InvalidDataException("Invalid PPM dimensions");

            int total = w * h;
            int[] pixels = new int[total];

            if (isP6)
            {
                for (int i = 0; i < total && p + 2 < bytes.Length; i++)
                {
                    byte r = bytes[p++];
                    byte g = bytes[p++];
                    byte b = bytes[p++];
                    pixels[i] = unchecked((int)(0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | b));
                }
            }
            else
            {
                for (int i = 0; i < total; i++)
                {
                    int r = int.Parse(ReadToken(bytes, ref p));
                    int g = int.Parse(ReadToken(bytes, ref p));
                    int b = int.Parse(ReadToken(bytes, ref p));
                    pixels[i] = unchecked((int)(0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
                }
            }

            ImageAccessor.Initialize(this, w, h, pixels, ColorDepth.ColorDepth32);
        }

        private static string ReadToken(byte[] bytes, ref int p)
        {
            while (p < bytes.Length)
            {
                while (p < bytes.Length && (bytes[p] == ' ' || bytes[p] == '\t' || bytes[p] == '\r' || bytes[p] == '\n'))
                    p++;
                if (p < bytes.Length && bytes[p] == '#')
                {
                    while (p < bytes.Length && bytes[p] != '\n') p++;
                    continue;
                }
                break;
            }

            int start = p;
            while (p < bytes.Length && bytes[p] > 32 && bytes[p] != '#')
                p++;

            return Encoding.ASCII.GetString(bytes, start, p - start);
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
            using var sw = new StreamWriter(ms, Encoding.ASCII);
            sw.Write($"P6\n{Width} {Height}\n255\n");
            sw.Flush();

            for (int i = 0; i < RawData.Length; i++)
            {
                int color = RawData[i];
                ms.WriteByte((byte)((color >> 16) & 0xFF)); // R
                ms.WriteByte((byte)((color >> 8) & 0xFF));  // G
                ms.WriteByte((byte)(color & 0xFF));         // B
            }

            return ms.ToArray();
        }
    }
}
