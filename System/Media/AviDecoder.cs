using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cosmos.Kernel.System.Graphics;

namespace Windose.System.Media
{
    /// <summary>
    /// RIFF AVI video container parser and MJPEG/DIB frame decoder for Windose.
    /// </summary>
    public class AviDecoder
    {
        public struct FrameEntry
        {
            public int Offset;
            public int Length;
            public bool IsMjpeg;
            public bool IsDib;
        }

        private readonly byte[] data;
        private readonly List<FrameEntry> frames = new List<FrameEntry>();
        private readonly List<byte[]> audioChunks = new List<byte[]>();
        private byte[]? combinedAudio;

        public int Width { get; private set; } = 320;
        public int Height { get; private set; } = 240;
        public float Fps { get; private set; } = 30f;
        public int FrameCount => frames.Count;
        public int DurationMs => (int)(frames.Count * (1000.0f / (Fps > 0 ? Fps : 30f)));
        public bool HasAudio => audioChunks.Count > 0;
        public bool IsValid { get; private set; }

        // Small cache for decoded bitmaps to prevent repeated allocations during scrubbing
        private int cachedFrameIndex = -1;
        private Bitmap? cachedBitmap;

        public AviDecoder(byte[] aviBytes)
        {
            data = aviBytes ?? throw new ArgumentNullException(nameof(aviBytes));
            Parse();
        }

        public AviDecoder(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("AVI file not found", path);
            data = File.ReadAllBytes(path);
            Parse();
        }

        private void Parse()
        {
            if (data == null || data.Length < 64) return;

            // RIFF header check
            if (data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F') return;
            if (data[8] != 'A' || data[9] != 'V' || data[10] != 'I' || data[11] != ' ') return;

            int pos = 12;
            int moviEnd = data.Length;

            while (pos + 8 <= data.Length)
            {
                string fourcc = Encoding.ASCII.GetString(data, pos, 4);
                uint chunkSize = BitConverter.ToUInt32(data, pos + 4);
                pos += 8;

                if (fourcc == "LIST" && pos + 4 <= data.Length)
                {
                    string listType = Encoding.ASCII.GetString(data, pos, 4);
                    pos += 4;
                    int listContentEnd = Math.Min(data.Length, (int)(pos + chunkSize - 4));

                    if (listType == "hdrl")
                    {
                        ParseHeaderList(pos, listContentEnd);
                        pos = listContentEnd;
                    }
                    else if (listType == "movi")
                    {
                        ParseMoviList(pos, listContentEnd);
                        pos = listContentEnd;
                    }
                    else
                    {
                        pos = listContentEnd;
                    }
                }
                else
                {
                    pos += (int)chunkSize;
                }

                // Chunk padding to 2-byte boundary
                if ((chunkSize & 1) != 0) pos++;
            }

            IsValid = frames.Count > 0;
        }

        private void ParseHeaderList(int start, int end)
        {
            int p = start;
            while (p + 8 <= end)
            {
                string tag = Encoding.ASCII.GetString(data, p, 4);
                uint sz = BitConverter.ToUInt32(data, p + 4);
                p += 8;

                if (tag == "avih" && p + 40 <= end)
                {
                    uint uSec = BitConverter.ToUInt32(data, p);
                    if (uSec > 0) Fps = 1000000.0f / uSec;
                    uint totalF = BitConverter.ToUInt32(data, p + 16);
                    uint w = BitConverter.ToUInt32(data, p + 32);
                    uint h = BitConverter.ToUInt32(data, p + 36);
                    if (w > 0 && w < 4096) Width = (int)w;
                    if (h > 0 && h < 4096) Height = (int)h;
                }

                p += (int)sz;
                if ((sz & 1) != 0) p++;
            }
        }

        private void ParseMoviList(int start, int end)
        {
            int p = start;
            while (p + 8 <= end)
            {
                string fourcc = Encoding.ASCII.GetString(data, p, 4);
                uint sz = BitConverter.ToUInt32(data, p + 4);
                p += 8;

                if (p + sz > data.Length) break;

                // Video chunks: ##dc (compressed/MJPEG), ##db (uncompressed DIB)
                if (fourcc.EndsWith("dc", StringComparison.OrdinalIgnoreCase))
                {
                    bool isJpeg = sz >= 4 && data[p] == 0xFF && data[p + 1] == 0xD8;
                    frames.Add(new FrameEntry { Offset = p, Length = (int)sz, IsMjpeg = isJpeg, IsDib = !isJpeg });
                }
                else if (fourcc.EndsWith("db", StringComparison.OrdinalIgnoreCase))
                {
                    frames.Add(new FrameEntry { Offset = p, Length = (int)sz, IsMjpeg = false, IsDib = true });
                }
                // Audio chunk: ##wb
                else if (fourcc.EndsWith("wb", StringComparison.OrdinalIgnoreCase) && sz > 0)
                {
                    byte[] achunk = new byte[sz];
                    Array.Copy(data, p, achunk, 0, (int)sz);
                    audioChunks.Add(achunk);
                }

                p += (int)sz;
                if ((sz & 1) != 0) p++;
            }
        }

        /// <summary>
        /// Retrieves the decoded video frame at the given 0-based frame index.
        /// </summary>
        public Bitmap? GetFrame(int index)
        {
            if (frames.Count == 0) return null;
            if (index < 0) index = 0;
            if (index >= frames.Count) index = frames.Count - 1;

            if (index == cachedFrameIndex && cachedBitmap != null)
                return cachedBitmap;

            FrameEntry entry = frames[index];
            if (entry.Length <= 0 || entry.Offset + entry.Length > data.Length)
                return cachedBitmap;

            Bitmap? bmp = null;
            try
            {
                if (entry.IsMjpeg)
                {
                    byte[] chunk = new byte[entry.Length];
                    Array.Copy(data, entry.Offset, chunk, 0, entry.Length);
                    // Decode MJPEG frame via Windose Jpeg decoder
                    Image jimg = new Jpeg(chunk);
                    if (jimg is Bitmap b)
                    {
                        bmp = b;
                    }
                    else
                    {
                        bmp = new Bitmap((int)jimg.Width, (int)jimg.Height, ColorDepth.ColorDepth32);
                        if (jimg.RawData != null && bmp.RawData != null)
                            Array.Copy(jimg.RawData, bmp.RawData, Math.Min(jimg.RawData.Length, bmp.RawData.Length));
                    }
                }
                else
                {
                    // Uncompressed DIB or RGB frame
                    bmp = DecodeDibFrame(entry.Offset, entry.Length, Width, Height);
                }
            }
            catch
            {
                // Fallback on previous frame if decoding error
                return cachedBitmap;
            }

            if (bmp != null)
            {
                cachedFrameIndex = index;
                cachedBitmap = bmp;
            }
            return bmp;
        }

        private Bitmap DecodeDibFrame(int offset, int len, int defaultW, int defaultH)
        {
            int w = defaultW;
            int h = defaultH;
            int pixelOffset = offset;

            // Check if chunk starts with BITMAPINFOHEADER (biSize = 40)
            if (len >= 40 && BitConverter.ToInt32(data, offset) == 40)
            {
                w = Math.Abs(BitConverter.ToInt32(data, offset + 4));
                h = Math.Abs(BitConverter.ToInt32(data, offset + 8));
                pixelOffset = offset + 40;
            }

            Bitmap bmp = new Bitmap(w, h, ColorDepth.ColorDepth32);
            int[] raw = bmp.RawData;
            if (raw == null) return bmp;

            // Read BGR / BGRA bottom-up scanlines
            int rowBytes = (w * 3 + 3) & ~3;
            for (int y = h - 1; y >= 0; y--)
            {
                int srcRow = pixelOffset + (h - 1 - y) * rowBytes;
                int dstRow = y * w;
                for (int x = 0; x < w; x++)
                {
                    int sp = srcRow + x * 3;
                    if (sp + 2 < data.Length && dstRow + x < raw.Length)
                    {
                        byte b = data[sp];
                        byte g = data[sp + 1];
                        byte r = data[sp + 2];
                        raw[dstRow + x] = unchecked((int)(0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
                    }
                }
            }

            return bmp;
        }

        /// <summary>
        /// Combines all audio chunks into a single raw PCM audio byte buffer.
        /// </summary>
        public byte[]? GetCombinedAudio()
        {
            if (combinedAudio != null) return combinedAudio;
            if (audioChunks.Count == 0) return null;

            int total = 0;
            for (int i = 0; i < audioChunks.Count; i++) total += audioChunks[i].Length;

            combinedAudio = new byte[total];
            int pos = 0;
            for (int i = 0; i < audioChunks.Count; i++)
            {
                Array.Copy(audioChunks[i], 0, combinedAudio, pos, audioChunks[i].Length);
                pos += audioChunks[i].Length;
            }
            return combinedAudio;
        }
    }
}
