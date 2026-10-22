using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Cosmos.Kernel.System.Graphics
{
    public enum KnownImageFormat
    {
        Unknown,
        WebP,
        Png,
        Jpeg,
        Gif,
        Bitmap,
        Qoi,
        Ico,
        Tga,
        Ppm,
        Svg
    }

    /// <summary>
    /// Unified image loader and format detection factory for Windose / Cosmos.
    /// Supports automatic detection of WebP, PNG, JPEG, GIF, BMP, QOI, ICO, TGA, and PPM.
    /// </summary>
    public static class ImageLoader
    {
        /// <summary>
        /// Loads an image from the filesystem, automatically detecting its format.
        /// </summary>
        public static Image Load(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("Image file not found", path);

            byte[] data = File.ReadAllBytes(path);
            KnownImageFormat format = DetectFormat(data, path);
            return CreateImage(format, data, path);
        }

        /// <summary>
        /// Loads an image from a byte array, automatically detecting its format.
        /// </summary>
        public static Image FromBytes(byte[] data, string? hintPathOrExtension = null)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentNullException(nameof(data));

            KnownImageFormat format = DetectFormat(data, hintPathOrExtension);
            return CreateImage(format, data, hintPathOrExtension);
        }

        /// <summary>
        /// Loads an image from a stream, automatically detecting its format.
        /// </summary>
        public static Image FromStream(Stream stream, string? hintPathOrExtension = null)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] data = ms.ToArray();
            return FromBytes(data, hintPathOrExtension);
        }

        /// <summary>
        /// Tries to load an image, returning false instead of throwing if unsupported or invalid.
        /// </summary>
        public static bool TryLoad(string path, [NotNullWhen(true)] out Image? image)
        {
            try
            {
                image = Load(path);
                return true;
            }
            catch
            {
                image = null;
                return false;
            }
        }

        /// <summary>
        /// Tries to load an image from bytes, returning false if unsupported or invalid.
        /// </summary>
        public static bool TryFromBytes(byte[] data, [NotNullWhen(true)] out Image? image, string? hintPathOrExtension = null)
        {
            try
            {
                image = FromBytes(data, hintPathOrExtension);
                return true;
            }
            catch
            {
                image = null;
                return false;
            }
        }

        /// <summary>
        /// Detects image format based on magic bytes or file extension hints.
        /// </summary>
        public static KnownImageFormat DetectFormat(byte[] data, string? hintPathOrExtension = null)
        {
            if (data == null || data.Length == 0)
                return KnownImageFormat.Unknown;

            // 1. PNG: 89 50 4E 47 0D 0A 1A 0A
            if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
                && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
            {
                return KnownImageFormat.Png;
            }

            // 2. WebP: RIFF .... WEBP, or raw VP8L signature 0x2F, or raw VP8 keyframe
            if (data.Length >= 12 && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
                && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
            {
                return KnownImageFormat.WebP;
            }
            if (data.Length >= 10 && (data[0] & 1) == 0 && data[3] == 0x9D && data[4] == 0x01 && data[5] == 0x2A)
            {
                return KnownImageFormat.WebP;
            }

            // 3. JPEG: FF D8 FF
            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            {
                return KnownImageFormat.Jpeg;
            }

            // 4. GIF: GIF87a or GIF89a
            if (data.Length >= 6 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F'
                && data[3] == (byte)'8' && (data[4] == (byte)'7' || data[4] == (byte)'9') && data[5] == (byte)'a')
            {
                return KnownImageFormat.Gif;
            }

            // 5. BMP: BM (42 4D)
            if (data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M')
            {
                return KnownImageFormat.Bitmap;
            }

            // 6. QOI: qoif
            if (data.Length >= 4 && data[0] == (byte)'q' && data[1] == (byte)'o' && data[2] == (byte)'i' && data[3] == (byte)'f')
            {
                return KnownImageFormat.Qoi;
            }

            // 7. ICO: 00 00 01 00
            if (data.Length >= 4 && data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x01 && data[3] == 0x00)
            {
                return KnownImageFormat.Ico;
            }

            // 8. PPM: P3 or P6
            if (data.Length >= 2 && data[0] == (byte)'P' && (data[1] == (byte)'3' || data[1] == (byte)'6'))
            {
                return KnownImageFormat.Ppm;
            }

            // 9. SVG: text/xml starting with "<svg" or "<?xml" containing "<svg"
            if (data.Length >= 4)
            {
                int start = 0;
                // Skip UTF-8 BOM if present
                if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                    start = 3;
                while (start < data.Length && (data[start] == ' ' || data[start] == '\t' || data[start] == '\r' || data[start] == '\n'))
                    start++;

                if (start + 4 <= data.Length && data[start] == '<')
                {
                    if ((char)data[start + 1] == 's' && (char)data[start + 2] == 'v' && (char)data[start + 3] == 'g')
                    {
                        return KnownImageFormat.Svg;
                    }
                    int scanLimit = Math.Min(data.Length - 4, 512);
                    for (int si = start; si < scanLimit; si++)
                    {
                        if (data[si] == '<' && (char)data[si + 1] == 's' && (char)data[si + 2] == 'v' && (char)data[si + 3] == 'g')
                        {
                            return KnownImageFormat.Svg;
                        }
                    }
                }
            }

            // Fallback by extension hint
            if (!string.IsNullOrEmpty(hintPathOrExtension))
            {
                string ext = Path.GetExtension(hintPathOrExtension).ToLowerInvariant();
                return ext switch
                {
                    ".webp" => KnownImageFormat.WebP,
                    ".png" => KnownImageFormat.Png,
                    ".jpg" or ".jpeg" or ".jfif" => KnownImageFormat.Jpeg,
                    ".gif" => KnownImageFormat.Gif,
                    ".bmp" => KnownImageFormat.Bitmap,
                    ".qoi" => KnownImageFormat.Qoi,
                    ".ico" => KnownImageFormat.Ico,
                    ".tga" => KnownImageFormat.Tga,
                    ".ppm" or ".pnm" => KnownImageFormat.Ppm,
                    ".svg" => KnownImageFormat.Svg,
                    _ => KnownImageFormat.Unknown
                };
            }

            return KnownImageFormat.Unknown;
        }

        private static Image CreateImage(KnownImageFormat format, byte[] data, string? pathOrExt)
        {
            switch (format)
            {
                case KnownImageFormat.WebP:
                    return new WebP(data);
                case KnownImageFormat.Png:
                    return new Png(data);
                case KnownImageFormat.Jpeg:
                    return new Jpeg(data);
                case KnownImageFormat.Gif:
                    return new Gif(data);
                case KnownImageFormat.Bitmap:
                    return new Bitmap(data, ColorOrder.BGR);
                case KnownImageFormat.Qoi:
                    return new Qoi(data);
                case KnownImageFormat.Ico:
                    return new Ico(data);
                case KnownImageFormat.Tga:
                    return new Tga(data);
                case KnownImageFormat.Ppm:
                    return new Ppm(data);
                case KnownImageFormat.Svg:
                    return new Svg(data);
                default:
                    // If unknown, try WebP, then Png, then Jpeg, then Svg, then Bitmap
                    try { return new WebP(data); } catch { }
                    try { return new Png(data); } catch { }
                    try { return new Jpeg(data); } catch { }
                    try { return new Svg(data); } catch { }
                    try { return new Bitmap(data, ColorOrder.BGR); } catch { }
                    throw new NotSupportedException($"Image format could not be identified or is unsupported.");
            }
        }
    }
}
