using System;
using System.IO;

namespace Cosmos.Kernel.System.Graphics
{
    /// <summary>
    /// Represents a JPEG (JFIF / EXIF) image inheriting from Cosmos Image.
    /// Supports Baseline (SOF0), Extended Sequential (SOF1), and Progressive (SOF2) DCT.
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
            int pos = 0;
            if (bytes.Length < 2 || bytes[pos++] != 0xFF || bytes[pos++] != 0xD8)
                throw new InvalidDataException("Not a valid JPEG (missing SOI)");

            ushort[][] qTables = new ushort[4][];
            HuffmanTable?[] dcTables = new HuffmanTable[4];
            HuffmanTable?[] acTables = new HuffmanTable[4];

            int imgW = 0, imgH = 0;
            int numComponents = 0;
            ComponentInfo[]? components = null;
            int restartInterval = 0;
            bool isProgressive = false;

            int maxH = 1, maxV = 1;
            int mcuCols = 0, mcuRows = 0;

            while (pos < bytes.Length)
            {
                if (bytes[pos++] != 0xFF) continue;
                while (pos < bytes.Length && bytes[pos] == 0xFF) pos++;
                if (pos >= bytes.Length) break;

                int marker = bytes[pos++];
                if (marker == 0xD9) break; // EOI
                if (marker == 0xD8 || marker == 0x00 || (marker >= 0xD0 && marker <= 0xD7)) continue;

                if (pos + 2 > bytes.Length) break;
                int len = (bytes[pos] << 8) | bytes[pos + 1];
                if (len < 2 || pos + len > bytes.Length) break;

                int payloadStart = pos + 2;
                int payloadLen = len - 2;
                pos += len;

                if (marker == 0xDB) // DQT (Define Quantization Table)
                {
                    int p = payloadStart;
                    int end = payloadStart + payloadLen;
                    while (p < end)
                    {
                        int info = bytes[p++];
                        int precision = (info >> 4) & 0xF;
                        int id = info & 0xF;
                        ushort[] table = new ushort[64];
                        for (int i = 0; i < 64; i++)
                        {
                            table[i] = (precision == 0)
                                ? bytes[p++]
                                : (ushort)((bytes[p++] << 8) | bytes[p++]);
                        }
                        if (id < 4) qTables[id] = table;
                    }
                }
                else if (marker == 0xC4) // DHT (Define Huffman Table)
                {
                    int p = payloadStart;
                    int end = payloadStart + payloadLen;
                    while (p < end)
                    {
                        int info = bytes[p++];
                        int tableClass = (info >> 4) & 0xF; // 0 = DC, 1 = AC
                        int id = info & 0xF;
                        byte[] counts = new byte[16];
                        int totalSymbols = 0;
                        for (int i = 0; i < 16; i++)
                        {
                            counts[i] = bytes[p++];
                            totalSymbols += counts[i];
                        }
                        byte[] symbols = new byte[totalSymbols];
                        Array.Copy(bytes, p, symbols, 0, totalSymbols);
                        p += totalSymbols;

                        var ht = new HuffmanTable(counts, symbols);
                        if (id < 4)
                        {
                            if (tableClass == 0) dcTables[id] = ht;
                            else acTables[id] = ht;
                        }
                    }
                }
                else if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2) // SOF0, SOF1, SOF2 (Progressive)
                {
                    isProgressive = (marker == 0xC2);
                    int p = payloadStart;
                    int precision = bytes[p++];
                    imgH = (bytes[p++] << 8) | bytes[p++];
                    imgW = (bytes[p++] << 8) | bytes[p++];
                    numComponents = bytes[p++];

                    components = new ComponentInfo[numComponents];
                    maxH = 1;
                    maxV = 1;

                    for (int i = 0; i < numComponents; i++)
                    {
                        byte id = bytes[p++];
                        byte hv = bytes[p++];
                        byte qId = bytes[p++];
                        int h = (hv >> 4) & 0xF;
                        int v = hv & 0xF;
                        if (h > maxH) maxH = h;
                        if (v > maxV) maxV = v;

                        components[i] = new ComponentInfo
                        {
                            Id = id,
                            H = h,
                            V = v,
                            QTableId = qId
                        };
                    }

                    int mcuWidth = maxH * 8;
                    int mcuHeight = maxV * 8;
                    mcuCols = (imgW + mcuWidth - 1) / mcuWidth;
                    mcuRows = (imgH + mcuHeight - 1) / mcuHeight;

                    for (int i = 0; i < numComponents; i++)
                    {
                        var c = components[i];
                        c.BlocksPerRow = mcuCols * c.H;
                        c.BlocksPerCol = mcuRows * c.V;
                        c.ValidBlocksX = (imgW * c.H + maxH * 8 - 1) / (maxH * 8);
                        c.ValidBlocksY = (imgH * c.V + maxV * 8 - 1) / (maxV * 8);
                        c.Coeffs = new short[c.BlocksPerRow * c.BlocksPerCol * 64];
                    }
                }
                else if (marker == 0xDD) // DRI (Define Restart Interval)
                {
                    restartInterval = (bytes[payloadStart] << 8) | bytes[payloadStart + 1];
                }
                else if (marker == 0xDA) // SOS (Start of Scan)
                {
                    if (components == null)
                        throw new InvalidDataException("SOS encountered before SOF");

                    int p = payloadStart;
                    int compCount = bytes[p++];
                    ComponentInfo[] scanComps = new ComponentInfo[compCount];

                    for (int i = 0; i < compCount; i++)
                    {
                        byte id = bytes[p++];
                        byte tables = bytes[p++];
                        ComponentInfo? target = null;
                        for (int c = 0; c < numComponents; c++)
                        {
                            if (components[c].Id == id)
                            {
                                target = components[c];
                                break;
                            }
                        }
                        if (target == null)
                            throw new InvalidDataException("Invalid component ID in SOS");

                        target.DcTableId = (tables >> 4) & 0xF;
                        target.AcTableId = tables & 0xF;
                        scanComps[i] = target;
                    }

                    int ss = bytes[p++];
                    int se = bytes[p++];
                    byte approx = bytes[p++];
                    int ah = (approx >> 4) & 0xF;
                    int al = approx & 0xF;

                    var br = new JpegBitReader(bytes, pos);
                    DecodeScan(br, scanComps, dcTables, acTables, restartInterval, isProgressive, ss, se, ah, al, mcuCols, mcuRows);
                    pos = br.Position;

                    // For baseline single-scan JPEGs, we can exit immediately if EOI follows
                    if (!isProgressive && compCount == numComponents)
                        break;
                }
            }

            if (imgW <= 0 || imgH <= 0 || components == null)
                throw new InvalidDataException("Invalid or unsupported JPEG stream");

            int[] raw = ReconstructPixels(imgW, imgH, components, qTables, maxH, maxV);
            ImageAccessor.Initialize(this, imgW, imgH, raw, ColorDepth.ColorDepth32);
        }

        private static void DecodeScan(
            JpegBitReader br,
            ComponentInfo[] scanComps,
            HuffmanTable?[] dcTables,
            HuffmanTable?[] acTables,
            int restartInterval,
            bool isProgressive,
            int ss, int se, int ah, int al,
            int mcuCols, int mcuRows)
        {
            int[] prevDc = new int[scanComps.Length];
            int eobRun = 0;
            int mcuCount = 0;

            if (scanComps.Length == 1)
            {
                // Non-interleaved scan: iterate over valid 8x8 blocks of this single component in raster order
                var comp = scanComps[0];
                var dcTable = dcTables[comp.DcTableId];
                var acTable = acTables[comp.AcTableId];
                int blocksX = comp.ValidBlocksX;
                int blocksY = comp.ValidBlocksY;
                int stride = comp.BlocksPerRow;

                for (int by = 0; by < blocksY; by++)
                {
                    for (int bx = 0; bx < blocksX; bx++)
                    {
                        if (restartInterval > 0 && mcuCount > 0 && (mcuCount % restartInterval) == 0)
                        {
                            br.ConsumeRestartMarker();
                            prevDc[0] = 0;
                            eobRun = 0;
                        }

                        int blockOffset = (by * stride + bx) * 64;
                        DecodeBlockScan(br, comp.Coeffs, blockOffset, dcTable, acTable, ref prevDc[0], ref eobRun, isProgressive, ss, se, ah, al);
                        mcuCount++;
                    }
                }
            }
            else
            {
                // Interleaved scan: iterate over MCUs
                for (int mcuY = 0; mcuY < mcuRows; mcuY++)
                {
                    for (int mcuX = 0; mcuX < mcuCols; mcuX++)
                    {
                        if (restartInterval > 0 && mcuCount > 0 && (mcuCount % restartInterval) == 0)
                        {
                            br.ConsumeRestartMarker();
                            Array.Clear(prevDc, 0, prevDc.Length);
                            eobRun = 0;
                        }

                        for (int c = 0; c < scanComps.Length; c++)
                        {
                            var comp = scanComps[c];
                            var dcTable = dcTables[comp.DcTableId];
                            var acTable = acTables[comp.AcTableId];
                            int stride = comp.BlocksPerRow;

                            for (int v = 0; v < comp.V; v++)
                            {
                                for (int h = 0; h < comp.H; h++)
                                {
                                    int bx = mcuX * comp.H + h;
                                    int by = mcuY * comp.V + v;
                                    int blockOffset = (by * stride + bx) * 64;

                                    DecodeBlockScan(br, comp.Coeffs, blockOffset, dcTable, acTable, ref prevDc[c], ref eobRun, isProgressive, ss, se, ah, al);
                                }
                            }
                        }
                        mcuCount++;
                    }
                }
            }
        }

        private static void DecodeBlockScan(
            JpegBitReader br,
            short[] coeffs,
            int offset,
            HuffmanTable? dc,
            HuffmanTable? ac,
            ref int prevDc,
            ref int eobRun,
            bool isProgressive,
            int ss, int se, int ah, int al)
        {
            if (!isProgressive)
            {
                // Baseline / Sequential DCT (SS = 0, SE = 63, AH = 0, AL = 0)
                if (dc != null)
                {
                    int dcCategory = dc.ReadSymbol(br);
                    int dcDiff = br.ReadBitsSigned(dcCategory);
                    prevDc += dcDiff;
                    coeffs[offset] = (short)prevDc;
                }

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
                        coeffs[offset + k] = (short)val;
                    }
                    k++;
                }
                return;
            }

            // Progressive DCT
            if (ss == 0)
            {
                // DC Scan
                if (ah == 0)
                {
                    if (dc != null)
                    {
                        int dcCategory = dc.ReadSymbol(br);
                        int dcDiff = br.ReadBitsSigned(dcCategory);
                        prevDc += dcDiff;
                        coeffs[offset] = (short)(prevDc << al);
                    }
                }
                else
                {
                    // DC Successive Approximation Refinement
                    if (br.ReadBit() != 0)
                    {
                        coeffs[offset] |= (short)(1 << al);
                    }
                }
            }
            else
            {
                // AC Scan
                if (ah == 0)
                {
                    // AC First Scan
                    if (eobRun > 0)
                    {
                        eobRun--;
                        return;
                    }

                    int k = ss;
                    while (k <= se && ac != null)
                    {
                        int rs = ac.ReadSymbol(br);
                        int r = (rs >> 4) & 0xF;
                        int s = rs & 0xF;

                        if (s == 0)
                        {
                            if (r == 15)
                            {
                                k += 16;
                            }
                            else
                            {
                                eobRun = (1 << r) + br.ReadBits(r) - 1;
                                break;
                            }
                        }
                        else
                        {
                            k += r;
                            if (k > se) break;
                            int val = br.ReadBitsSigned(s);
                            coeffs[offset + k] = (short)(val << al);
                            k++;
                        }
                    }
                }
                else
                {
                    // AC Successive Approximation Refinement
                    short posBit = (short)(1 << al);
                    short negBit = (short)(-1 << al);
                    int k = ss;

                    if (eobRun == 0)
                    {
                        while (k <= se && ac != null)
                        {
                            int rs = ac.ReadSymbol(br);
                            int r = (rs >> 4) & 0xF;
                            int s = rs & 0xF;
                            int newVal = 0;

                            if (s == 0)
                            {
                                if (r < 15)
                                {
                                    eobRun = (1 << r) + br.ReadBits(r);
                                    break;
                                }
                            }
                            else if (s == 1)
                            {
                                newVal = (br.ReadBit() != 0) ? posBit : negBit;
                            }

                            // Advance over r zero coefficients while refining existing non-zero coefficients
                            while (k <= se)
                            {
                                short c = coeffs[offset + k];
                                if (c != 0)
                                {
                                    if (br.ReadBit() != 0)
                                    {
                                        if ((c & posBit) == 0)
                                            coeffs[offset + k] = (short)(c + (c > 0 ? posBit : negBit));
                                    }
                                }
                                else
                                {
                                    if (r == 0) break;
                                    r--;
                                }
                                k++;
                            }

                            if (newVal != 0 && k <= se)
                            {
                                coeffs[offset + k] = (short)newVal;
                            }
                            k++;
                        }
                    }

                    if (eobRun > 0)
                    {
                        while (k <= se)
                        {
                            short c = coeffs[offset + k];
                            if (c != 0)
                            {
                                if (br.ReadBit() != 0)
                                {
                                    if ((c & posBit) == 0)
                                        coeffs[offset + k] = (short)(c + (c > 0 ? posBit : negBit));
                                }
                            }
                            k++;
                        }
                        eobRun--;
                    }
                }
            }
        }

        private static int[] ReconstructPixels(int width, int height, ComponentInfo[] components, ushort[][] qTables, int maxH, int maxV)
        {
            byte[][] planes = new byte[components.Length][];
            int[] planeWidths = new int[components.Length];
            int[] planeHeights = new int[components.Length];
            int[] block = new int[64];

            for (int c = 0; c < components.Length; c++)
            {
                var comp = components[c];
                var qTable = qTables[comp.QTableId];
                int pStride = comp.BlocksPerRow * 8;
                int pHeight = comp.BlocksPerCol * 8;
                byte[] plane = new byte[pStride * pHeight];

                planeWidths[c] = pStride;
                planeHeights[c] = pHeight;
                planes[c] = plane;

                short[] coeffs = comp.Coeffs;
                for (int by = 0; by < comp.BlocksPerCol; by++)
                {
                    for (int bx = 0; bx < comp.BlocksPerRow; bx++)
                    {
                        int offset = (by * comp.BlocksPerRow + bx) * 64;
                        Array.Clear(block, 0, 64);

                        for (int k = 0; k < 64; k++)
                        {
                            short val = coeffs[offset + k];
                            if (val != 0)
                            {
                                block[ZigZag[k]] = val * qTable[k];
                            }
                        }

                        Idct8x8(block);

                        int blockX = bx * 8;
                        int blockY = by * 8;
                        for (int y = 0; y < 8; y++)
                        {
                            int row = (blockY + y) * pStride + blockX;
                            int bRow = y * 8;
                            for (int x = 0; x < 8; x++)
                            {
                                plane[row + x] = (byte)Math.Clamp(block[bRow + x] + 128, 0, 255);
                            }
                        }
                    }
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

        private static void Idct8x8(int[] block)
        {
            // Pass 1: Rows (constants scaled by 1024; 1D factor of 1/2 -> total 2048 = 2^11)
            // Shift right by 8 here to keep 3 bits of fractional precision for the column pass.
            for (int i = 0; i < 8; i++)
            {
                int row = i * 8;
                int a0 = block[row + 0], a1 = block[row + 1], a2 = block[row + 2], a3 = block[row + 3];
                int a4 = block[row + 4], a5 = block[row + 5], a6 = block[row + 6], a7 = block[row + 7];

                // Even part
                int v0 = (a0 + a4) * 724;
                int v1 = (a0 - a4) * 724;
                int v2 = a2 * 392 - a6 * 946;
                int v3 = a2 * 946 + a6 * 392;
                int e0 = v0 + v3;
                int e1 = v1 + v2;
                int e2 = v1 - v2;
                int e3 = v0 - v3;

                // Odd part (exact cosine matrix for u = 1, 3, 5, 7)
                int o0 = a1 * 1004 + a3 * 851 + a5 * 569 + a7 * 200;
                int o1 = a1 * 851 - a3 * 200 - a5 * 1004 - a7 * 569;
                int o2 = a1 * 569 - a3 * 1004 + a5 * 200 + a7 * 851;
                int o3 = a1 * 200 - a3 * 569 + a5 * 851 - a7 * 1004;

                block[row + 0] = (e0 + o0 + 128) >> 8;
                block[row + 1] = (e1 + o1 + 128) >> 8;
                block[row + 2] = (e2 + o2 + 128) >> 8;
                block[row + 3] = (e3 + o3 + 128) >> 8;
                block[row + 4] = (e3 - o3 + 128) >> 8;
                block[row + 5] = (e2 - o2 + 128) >> 8;
                block[row + 6] = (e1 - o1 + 128) >> 8;
                block[row + 7] = (e0 - o0 + 128) >> 8;
            }

            // Pass 2: Columns
            // Remaining shift: 11 (from row pass) - 8 + 11 (from col pass) = 14 (2^14 = 16384)
            for (int j = 0; j < 8; j++)
            {
                int a0 = block[0 * 8 + j], a1 = block[1 * 8 + j], a2 = block[2 * 8 + j], a3 = block[3 * 8 + j];
                int a4 = block[4 * 8 + j], a5 = block[5 * 8 + j], a6 = block[6 * 8 + j], a7 = block[7 * 8 + j];

                // Even part
                int v0 = (a0 + a4) * 724;
                int v1 = (a0 - a4) * 724;
                int v2 = a2 * 392 - a6 * 946;
                int v3 = a2 * 946 + a6 * 392;
                int e0 = v0 + v3;
                int e1 = v1 + v2;
                int e2 = v1 - v2;
                int e3 = v0 - v3;
                // Odd part
                int o0 = a1 * 1004 + a3 * 851 + a5 * 569 + a7 * 200;
                int o1 = a1 * 851 - a3 * 200 - a5 * 1004 - a7 * 569;
                int o2 = a1 * 569 - a3 * 1004 + a5 * 200 + a7 * 851;
                int o3 = a1 * 200 - a3 * 569 + a5 * 851 - a7 * 1004;

                block[0 * 8 + j] = (e0 + o0 + 8192) >> 14;
                block[1 * 8 + j] = (e1 + o1 + 8192) >> 14;
                block[2 * 8 + j] = (e2 + o2 + 8192) >> 14;
                block[3 * 8 + j] = (e3 + o3 + 8192) >> 14;
                block[4 * 8 + j] = (e3 - o3 + 8192) >> 14;
                block[5 * 8 + j] = (e2 - o2 + 8192) >> 14;
                block[6 * 8 + j] = (e1 - o1 + 8192) >> 14;
                block[7 * 8 + j] = (e0 - o0 + 8192) >> 14;
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
            public int BlocksPerRow;
            public int BlocksPerCol;
            public int ValidBlocksX;
            public int ValidBlocksY;
            public short[] Coeffs = Array.Empty<short>();
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
            private bool hitMarker;

            public int Position => pos;

            public JpegBitReader(byte[] d, int startPos)
            {
                data = d;
                pos = startPos;
                bitBuffer = 0;
                bitsCount = 0;
                hitMarker = false;
            }

            public int ReadBit()
            {
                if (bitsCount == 0)
                {
                    if (hitMarker || pos >= data.Length)
                        return 0;

                    byte b = data[pos++];
                    if (b == 0xFF)
                    {
                        int mPos = pos;
                        while (mPos < data.Length && data[mPos] == 0xFF)
                            mPos++;

                        if (mPos < data.Length)
                        {
                            byte marker = data[mPos];
                            if (marker == 0x00)
                            {
                                // Stuffed 0xFF byte
                                pos = mPos + 1;
                                b = 0xFF;
                            }
                            else if (marker >= 0xD0 && marker <= 0xD7)
                            {
                                // Restart marker mid-stream; skip and read next byte
                                pos = mPos + 1;
                                b = (pos < data.Length) ? data[pos++] : (byte)0;
                            }
                            else
                            {
                                // Hit next JPEG segment marker (DHT, SOS, EOI, etc.)
                                // Rewind pos to the 0xFF prefix so the outer parser sees it
                                pos--;
                                hitMarker = true;
                                return 0;
                            }
                        }
                    }
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

            public void ConsumeRestartMarker()
            {
                bitsCount = 0;
                bitBuffer = 0;

                if (hitMarker)
                    return;

                while (pos < data.Length - 1)
                {
                    if (data[pos] == 0xFF)
                    {
                        int mPos = pos + 1;
                        while (mPos < data.Length && data[mPos] == 0xFF)
                            mPos++;

                        if (mPos < data.Length)
                        {
                            byte next = data[mPos];
                            if (next >= 0xD0 && next <= 0xD7)
                            {
                                pos = mPos + 1;
                                return;
                            }
                            if (next != 0x00)
                            {
                                return;
                            }
                        }
                    }
                    pos++;
                }
            }
        }
    }
}