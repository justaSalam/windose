using System;
using System.IO;

namespace Cosmos.Kernel.System.Graphics
{
    /// <summary>
    /// Represents a JPEG (JFIF / EXIF) image inheriting from Cosmos Image.
    /// </summary>
    public class Jpeg : Image
    {
        public Jpeg(string path) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("JPEG file not found", path);

            byte[] data = File.ReadAllBytes(path);
            Decode(data);
        }

        public Jpeg(byte[] imageData) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (imageData == null || imageData.Length == 0)
                throw new ArgumentNullException(nameof(imageData));

            Decode(imageData);
        }

        public Jpeg(Stream stream) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            Decode(ms.ToArray());
        }

        public Jpeg(int width, int height) : base(width, height, ColorDepth.ColorDepth32)
        {
            ImageAccessor.SetRawData(this, new int[width * height]);
        }

        private void Decode(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            int b0 = ms.ReadByte();
            int b1 = ms.ReadByte();
            if (b0 != 0xFF || b1 != 0xD8)
                throw new InvalidDataException("Not a valid JPEG (missing SOI)");

            byte[][] qTables = new byte[4][];
            HuffmanTable?[] dcTables = new HuffmanTable[4];
            HuffmanTable?[] acTables = new HuffmanTable[4];

            int imgW = 0, imgH = 0;
            int numComponents = 0;
            ComponentInfo[]? components = null;
            int restartInterval = 0;
            byte[]? scanData = null;

            while (ms.Position < ms.Length)
            {
                int markerPrefix = ms.ReadByte();
                if (markerPrefix != 0xFF) continue;
                int marker = ms.ReadByte();
                while (marker == 0xFF) marker = ms.ReadByte();

                if (marker == 0xD9) break; // EOI
                if (marker == 0xD8) continue; // SOI

                int len = (ms.ReadByte() << 8) | ms.ReadByte();
                if (len < 2) break;
                byte[] payload = new byte[len - 2];
                ms.ReadExactly(payload, 0, payload.Length);

                if (marker == 0xDB) // DQT
                {
                    int p = 0;
                    while (p < payload.Length)
                    {
                        int info = payload[p++];
                        int precision = (info >> 4) & 0xF;
                        int id = info & 0xF;
                        byte[] table = new byte[64];
                        for (int i = 0; i < 64; i++)
                        {
                            table[i] = (precision == 0) ? payload[p++] : (byte)((payload[p++] << 8) | payload[p++]);
                        }
                        if (id < 4) qTables[id] = table;
                    }
                }
                else if (marker == 0xC4) // DHT
                {
                    int p = 0;
                    while (p < payload.Length)
                    {
                        int info = payload[p++];
                        int tableClass = (info >> 4) & 0xF; // 0 = DC, 1 = AC
                        int id = info & 0xF;
                        byte[] counts = new byte[16];
                        int totalSymbols = 0;
                        for (int i = 0; i < 16; i++)
                        {
                            counts[i] = payload[p++];
                            totalSymbols += counts[i];
                        }
                        byte[] symbols = new byte[totalSymbols];
                        Array.Copy(payload, p, symbols, 0, totalSymbols);
                        p += totalSymbols;

                        var ht = new HuffmanTable(counts, symbols);
                        if (id < 4)
                        {
                            if (tableClass == 0) dcTables[id] = ht;
                            else acTables[id] = ht;
                        }
                    }
                }
                else if (marker == 0xC0) // SOF0 (Baseline DCT)
                {
                    int precision = payload[0];
                    imgH = (payload[1] << 8) | payload[2];
                    imgW = (payload[3] << 8) | payload[4];
                    numComponents = payload[5];
                    components = new ComponentInfo[numComponents];
                    int p = 6;
                    for (int i = 0; i < numComponents; i++)
                    {
                        components[i] = new ComponentInfo
                        {
                            Id = payload[p++],
                            H = (payload[p] >> 4) & 0xF,
                            V = payload[p++] & 0xF,
                            QTableId = payload[p++]
                        };
                    }
                }
                else if (marker == 0xDD) // DRI
                {
                    restartInterval = (payload[0] << 8) | payload[1];
                }
                else if (marker == 0xDA) // SOS
                {
                    int compCount = payload[0];
                    int p = 1;
                    for (int i = 0; i < compCount; i++)
                    {
                        byte id = payload[p++];
                        byte tables = payload[p++];
                        if (components != null)
                        {
                            for (int c = 0; c < numComponents; c++)
                            {
                                if (components[c].Id == id)
                                {
                                    components[c].DcTableId = (tables >> 4) & 0xF;
                                    components[c].AcTableId = tables & 0xF;
                                    break;
                                }
                            }
                        }
                    }

                    long remaining = ms.Length - ms.Position;
                    scanData = new byte[remaining];
                    ms.ReadExactly(scanData, 0, scanData.Length);
                    break;
                }
            }

            if (imgW <= 0 || imgH <= 0 || components == null || scanData == null)
                throw new InvalidDataException("Invalid or unsupported JPEG stream");

            int[] raw = DecodeBaselineJpeg(scanData, imgW, imgH, components, qTables, dcTables, acTables, restartInterval);
            ImageAccessor.Initialize(this, imgW, imgH, raw, ColorDepth.ColorDepth32);
        }

        private static int[] DecodeBaselineJpeg(byte[] scanData, int width, int height, ComponentInfo[] components,
            byte[][] qTables, HuffmanTable?[] dcTables, HuffmanTable?[] acTables, int restartInterval)
        {
            var br = new JpegBitReader(scanData);
            int maxH = 1, maxV = 1;
            foreach (var c in components)
            {
                if (c.H > maxH) maxH = c.H;
                if (c.V > maxV) maxV = c.V;
            }

            int mcuWidth = maxH * 8;
            int mcuHeight = maxV * 8;
            int mcuCols = (width + mcuWidth - 1) / mcuWidth;
            int mcuRows = (height + mcuHeight - 1) / mcuHeight;

            byte[][] planes = new byte[components.Length][];
            int[] planeWidths = new int[components.Length];
            int[] planeHeights = new int[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                planeWidths[i] = mcuCols * components[i].H * 8;
                planeHeights[i] = mcuRows * components[i].V * 8;
                planes[i] = new byte[planeWidths[i] * planeHeights[i]];
            }

            int[] prevDc = new int[components.Length];
            int mcuCount = 0;

            for (int mcuY = 0; mcuY < mcuRows; mcuY++)
            {
                for (int mcuX = 0; mcuX < mcuCols; mcuX++)
                {
                    if (restartInterval > 0 && mcuCount > 0 && (mcuCount % restartInterval) == 0)
                    {
                        br.AlignToByte();
                        Array.Clear(prevDc, 0, prevDc.Length);
                    }

                    for (int c = 0; c < components.Length; c++)
                    {
                        var comp = components[c];
                        var qTable = qTables[comp.QTableId];
                        var dcTable = dcTables[comp.DcTableId];
                        var acTable = acTables[comp.AcTableId];
                        var plane = planes[c];
                        int pStride = planeWidths[c];

                        for (int v = 0; v < comp.V; v++)
                        {
                            for (int h = 0; h < comp.H; h++)
                            {
                                int[] block = DecodeBlock(br, dcTable, acTable, qTable, ref prevDc[c]);
                                int blockX = (mcuX * comp.H + h) * 8;
                                int blockY = (mcuY * comp.V + v) * 8;

                                for (int by = 0; by < 8; by++)
                                {
                                    int row = (blockY + by) * pStride + blockX;
                                    for (int bx = 0; bx < 8; bx++)
                                    {
                                        plane[row + bx] = (byte)Math.Clamp(block[by * 8 + bx] + 128, 0, 255);
                                    }
                                }
                            }
                        }
                    }
                    mcuCount++;
                }
            }

            int[] result = new int[width * height];
            if (components.Length == 1) // Grayscale
            {
                var p0 = planes[0];
                int s0 = planeWidths[0];
                for (int y = 0; y < height; y++)
                {
                    int rOff = y * width;
                    int pOff = y * s0;
                    for (int x = 0; x < width; x++)
                    {
                        int g = p0[pOff + x];
                        result[rOff + x] = unchecked((int)(0xFF000000 | ((uint)g << 16) | ((uint)g << 8) | (uint)g));
                    }
                }
            }
            else // YCbCr
            {
                var yPlane = planes[0]; int yStride = planeWidths[0];
                var cbPlane = planes[1]; int cbStride = planeWidths[1];
                var crPlane = planes[2]; int crStride = planeWidths[2];

                int hRatioY_Cb = Math.Max(1, maxH / components[1].H);
                int vRatioY_Cb = Math.Max(1, maxV / components[1].V);
                int hRatioY_Cr = Math.Max(1, maxH / components[2].H);
                int vRatioY_Cr = Math.Max(1, maxV / components[2].V);

                for (int y = 0; y < height; y++)
                {
                    int rOff = y * width;
                    int yOff = y * yStride;
                    int cbY = (y / vRatioY_Cb) * cbStride;
                    int crY = (y / vRatioY_Cr) * crStride;

                    for (int x = 0; x < width; x++)
                    {
                        int yVal = yPlane[yOff + x];
                        int cb = cbPlane[cbY + (x / hRatioY_Cb)] - 128;
                        int cr = crPlane[crY + (x / hRatioY_Cr)] - 128;

                        int r = Math.Clamp((yVal * 1024 + 1436 * cr + 512) >> 10, 0, 255);
                        int g = Math.Clamp((yVal * 1024 - 352 * cb - 731 * cr + 512) >> 10, 0, 255);
                        int b = Math.Clamp((yVal * 1024 + 1815 * cb + 512) >> 10, 0, 255);

                        result[rOff + x] = unchecked((int)(0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
                    }
                }
            }

            return result;
        }

        private static readonly int[] ZigZag =
        {
            0,  1,  8, 16,  9,  2,  3, 10,
            17, 24, 32, 25, 18, 11,  4,  5,
            12, 19, 26, 33, 40, 48, 41, 34,
            27, 20, 13,  6,  7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36,
            29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46,
            53, 60, 61, 54, 47, 55, 62, 63
        };

        private static int[] DecodeBlock(JpegBitReader br, HuffmanTable? dc, HuffmanTable? ac, byte[] qTable, ref int prevDc)
        {
            int[] coeff = new int[64];

            // DC
            if (dc != null)
            {
                int dcCategory = dc.ReadSymbol(br);
                int dcDiff = br.ReadBitsSigned(dcCategory);
                prevDc += dcDiff;
            }
            coeff[0] = prevDc * qTable[0];

            // AC
            int k = 1;
            while (k < 64 && ac != null)
            {
                int rs = ac.ReadSymbol(br);
                if (rs == 0) break; // EOB
                int r = (rs >> 4) & 0xF;
                int s = rs & 0xF;
                k += r;
                if (k >= 64) break;
                if (s != 0)
                {
                    int val = br.ReadBitsSigned(s);
                    coeff[ZigZag[k]] = val * qTable[k];
                }
                k++;
            }

            Idct8x8(coeff);
            return coeff;
        }

        private static void Idct8x8(int[] block)
        {
            for (int i = 0; i < 8; i++)
            {
                int row = i * 8;
                int a0 = block[row + 0], a1 = block[row + 1], a2 = block[row + 2], a3 = block[row + 3];
                int a4 = block[row + 4], a5 = block[row + 5], a6 = block[row + 6], a7 = block[row + 7];

                int v0 = (a0 + a4) * 512;
                int v1 = (a0 - a4) * 512;
                int v2 = a2 * 392 - a6 * 946;
                int v3 = a2 * 946 + a6 * 392;
                int v4 = v0 + v3;
                int v5 = v1 + v2;
                int v6 = v1 - v2;
                int v7 = v0 - v3;

                int w0 = a1 * 980 + a7 * 196;
                int w1 = a1 * 196 - a7 * 980;
                int w2 = a5 * 836 - a3 * 555;
                int w3 = a5 * 555 + a3 * 836;
                int w4 = w0 + w3;
                int w5 = w1 + w2;
                int w6 = w1 - w2;
                int w7 = w0 - w3;

                block[row + 0] = (v4 + w4 + 256) >> 9;
                block[row + 1] = (v5 + w5 + 256) >> 9;
                block[row + 2] = (v6 + w6 + 256) >> 9;
                block[row + 3] = (v7 + w7 + 256) >> 9;
                block[row + 4] = (v7 - w7 + 256) >> 9;
                block[row + 5] = (v6 - w6 + 256) >> 9;
                block[row + 6] = (v5 - w5 + 256) >> 9;
                block[row + 7] = (v4 - w4 + 256) >> 9;
            }

            for (int j = 0; j < 8; j++)
            {
                int a0 = block[0 * 8 + j], a1 = block[1 * 8 + j], a2 = block[2 * 8 + j], a3 = block[3 * 8 + j];
                int a4 = block[4 * 8 + j], a5 = block[5 * 8 + j], a6 = block[6 * 8 + j], a7 = block[7 * 8 + j];

                int v0 = (a0 + a4) * 512;
                int v1 = (a0 - a4) * 512;
                int v2 = a2 * 392 - a6 * 946;
                int v3 = a2 * 946 + a6 * 392;
                int v4 = v0 + v3;
                int v5 = v1 + v2;
                int v6 = v1 - v2;
                int v7 = v0 - v3;

                int w0 = a1 * 980 + a7 * 196;
                int w1 = a1 * 196 - a7 * 980;
                int w2 = a5 * 836 - a3 * 555;
                int w3 = a5 * 555 + a3 * 836;
                int w4 = w0 + w3;
                int w5 = w1 + w2;
                int w6 = w1 - w2;
                int w7 = w0 - w3;

                block[0 * 8 + j] = (v4 + w4 + 1024) >> 11;
                block[1 * 8 + j] = (v5 + w5 + 1024) >> 11;
                block[2 * 8 + j] = (v6 + w6 + 1024) >> 11;
                block[3 * 8 + j] = (v7 + w7 + 1024) >> 11;
                block[4 * 8 + j] = (v7 - w7 + 1024) >> 11;
                block[5 * 8 + j] = (v6 - w6 + 1024) >> 11;
                block[6 * 8 + j] = (v5 - w5 + 1024) >> 11;
                block[7 * 8 + j] = (v4 - w4 + 1024) >> 11;
            }
        }

        private sealed class ComponentInfo
        {
            public byte Id;
            public int H;
            public int V;
            public int QTableId;
            public int DcTableId;
            public int AcTableId;
        }

        private sealed class HuffmanTable
        {
            private readonly int[] minCode = new int[17];
            private readonly int[] maxCode = new int[17];
            private readonly int[] valPtr = new int[17];
            private readonly byte[] values;

            public HuffmanTable(byte[] counts, byte[] symbols)
            {
                values = symbols;
                int code = 0;
                int idx = 0;
                for (int i = 1; i <= 16; i++)
                {
                    int count = counts[i - 1];
                    if (count == 0)
                    {
                        maxCode[i] = -1;
                    }
                    else
                    {
                        valPtr[i] = idx;
                        minCode[i] = code;
                        maxCode[i] = code + count - 1;
                        code += count;
                        idx += count;
                    }
                    code <<= 1;
                }
            }

            public int ReadSymbol(JpegBitReader br)
            {
                int code = 0;
                for (int len = 1; len <= 16; len++)
                {
                    code = (code << 1) | br.ReadBit();
                    if (code <= maxCode[len] && code >= minCode[len])
                    {
                        int index = valPtr[len] + (code - minCode[len]);
                        return values[index];
                    }
                }
                return 0;
            }
        }

        private sealed class JpegBitReader
        {
            private readonly byte[] data;
            private int pos;
            private uint bitBuffer;
            private int bitsCount;

            public JpegBitReader(byte[] d) { data = d; pos = 0; bitBuffer = 0; bitsCount = 0; }

            public int ReadBit()
            {
                if (bitsCount == 0)
                {
                    byte b = (pos < data.Length) ? data[pos++] : (byte)0;
                    if (b == 0xFF && pos < data.Length && data[pos] == 0x00) pos++; // Skip byte unstuffing
                    bitBuffer = b;
                    bitsCount = 8;
                }
                bitsCount--;
                return (int)((bitBuffer >> bitsCount) & 1);
            }

            public int ReadBits(int n)
            {
                int res = 0;
                for (int i = 0; i < n; i++)
                    res = (res << 1) | ReadBit();
                return res;
            }

            public int ReadBitsSigned(int n)
            {
                if (n == 0) return 0;
                int val = ReadBits(n);
                if (val < (1 << (n - 1)))
                    val -= (1 << n) - 1;
                return val;
            }

            public void AlignToByte() => bitsCount = 0;
        }
    }
}
