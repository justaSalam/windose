using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Cosmos.Kernel.System.Graphics
{
    /// <summary>
    /// Represents a WebP format image (supporting Lossless VP8L, Lossy VP8, Extended VP8X, and Alpha channels)
    /// inheriting from Cosmos Image.
    /// </summary>
    public class WebP : Image
    {
        public WebP(string path) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("WebP file not found", path);

            byte[] data = File.ReadAllBytes(path);
            Decode(data);
        }

        public WebP(byte[] imageData) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (imageData == null || imageData.Length == 0)
                throw new ArgumentNullException(nameof(imageData));

            Decode(imageData);
        }

        public WebP(Stream stream) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            Decode(ms.ToArray());
        }

        public WebP(int width, int height) : base(width, height, ColorDepth.ColorDepth32)
        {
            ImageAccessor.SetRawData(this, new int[width * height]);
        }

        private void Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12)
                throw new InvalidDataException("Data too small to be WebP");

            // 1. RIFF Container
            if (bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
                && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
            {
                int offset = 12;
                byte[]? vp8 = null;
                byte[]? vp8l = null;
                byte[]? alph = null;
                int canvasW = 0, canvasH = 0;

                while (offset + 8 <= bytes.Length)
                {
                    string fcc = Encoding.ASCII.GetString(bytes, offset, 4);
                    uint csz = BitConverter.ToUInt32(bytes, offset + 4);
                    offset += 8;
                    if (offset + csz > bytes.Length) break;

                    if (fcc == "VP8 ")
                    {
                        vp8 = new byte[csz];
                        Buffer.BlockCopy(bytes, offset, vp8, 0, (int)csz);
                    }
                    else if (fcc == "VP8L")
                    {
                        vp8l = new byte[csz];
                        Buffer.BlockCopy(bytes, offset, vp8l, 0, (int)csz);
                    }
                    else if (fcc == "ALPH")
                    {
                        alph = new byte[csz];
                        Buffer.BlockCopy(bytes, offset, alph, 0, (int)csz);
                    }
                    else if (fcc == "VP8X")
                    {
                        canvasW = (bytes[offset + 4] | (bytes[offset + 5] << 8) | (bytes[offset + 6] << 16)) + 1;
                        canvasH = (bytes[offset + 7] | (bytes[offset + 8] << 8) | (bytes[offset + 9] << 16)) + 1;
                    }

                    offset += (int)csz;
                    if ((csz & 1) != 0) offset++; // 2-byte alignment padding
                }

                if (vp8l != null)
                {
                    var lDec = new Vp8LDecoder();
                    int w, h;
                    int[] pixels = lDec.Decode(vp8l, out w, out h);
                    ImageAccessor.Initialize(this, w, h, pixels, ColorDepth.ColorDepth32);
                    return;
                }
                if (vp8 != null)
                {
                    var vDec = new Vp8Decoder();
                    int w, h;
                    int[] pixels = vDec.Decode(vp8, alph, canvasW, canvasH, out w, out h);
                    ImageAccessor.Initialize(this, w, h, pixels, ColorDepth.ColorDepth32);
                    return;
                }
            }
            // 2. Raw VP8L stream
            else if (bytes.Length > 0 && bytes[0] == 0x2F)
            {
                var lDec = new Vp8LDecoder();
                int w, h;
                int[] pixels = lDec.Decode(bytes, out w, out h);
                ImageAccessor.Initialize(this, w, h, pixels, ColorDepth.ColorDepth32);
                return;
            }
            // 3. Raw VP8 keyframe stream
            else if (bytes.Length >= 10 && (bytes[0] & 1) == 0 && bytes[3] == 0x9D && bytes[4] == 0x01 && bytes[5] == 0x2A)
            {
                var vDec = new Vp8Decoder();
                int w, h;
                int[] pixels = vDec.Decode(bytes, null, 0, 0, out w, out h);
                ImageAccessor.Initialize(this, w, h, pixels, ColorDepth.ColorDepth32);
                return;
            }

            throw new InvalidDataException("Unsupported or corrupted WebP bitstream");
        }

        #region VP8L Lossless Decoder
        private sealed class Vp8LDecoder
        {
            public int[] Decode(byte[] data, out int width, out int height)
            {
                var r = new BitReader(data);
                byte sig = (byte)r.ReadBits(8);
                if (sig != 0x2F) throw new InvalidDataException("Invalid VP8L signature");

                width = r.ReadBits(14) + 1;
                height = r.ReadBits(14) + 1;
                int hasAlpha = r.ReadBits(1);
                int version = r.ReadBits(3);
                if (version != 0) throw new InvalidDataException("Unsupported VP8L version");

                int currentW = width;
                int currentH = height;
                var transforms = new List<Transform>();

                while (r.ReadBits(1) == 1)
                {
                    int transformType = r.ReadBits(2);
                    if (transformType == 0) // SUBTRACT_GREEN
                    {
                        transforms.Add(new SubtractGreenTransform());
                    }
                    else if (transformType == 1) // PREDICTOR
                    {
                        int sizeBits = r.ReadBits(3) + 2;
                        int blockW = SubsampleSize(currentW, sizeBits);
                        int blockH = SubsampleSize(currentH, sizeBits);
                        int[] predData = DecodeImageStream(r, blockW, blockH);
                        transforms.Add(new PredictorTransform(sizeBits, blockW, blockH, predData));
                    }
                    else if (transformType == 2) // COLOR_TRANSFORM
                    {
                        int sizeBits = r.ReadBits(3) + 2;
                        int blockW = SubsampleSize(currentW, sizeBits);
                        int blockH = SubsampleSize(currentH, sizeBits);
                        int[] colorData = DecodeImageStream(r, blockW, blockH);
                        transforms.Add(new ColorTransform(sizeBits, blockW, blockH, colorData));
                    }
                    else if (transformType == 3) // COLOR_INDEXING
                    {
                        int numColors = r.ReadBits(8) + 1;
                        int[] palette = DecodeImageStream(r, numColors, 1);
                        int bits = numColors <= 2 ? 1 : (numColors <= 4 ? 2 : (numColors <= 16 ? 4 : 8));
                        transforms.Add(new ColorIndexingTransform(numColors, palette, bits, currentW));
                        currentW = SubsampleSize(currentW, bits == 1 ? 3 : (bits == 2 ? 2 : (bits == 4 ? 1 : 0)));
                    }
                }

                int[] pixels = DecodeImageStream(r, currentW, currentH);

                // Reverse transforms in reverse order
                for (int i = transforms.Count - 1; i >= 0; i--)
                {
                    pixels = transforms[i].Invert(pixels, width, height);
                }

                return pixels;
            }

            private static int SubsampleSize(int size, int bits) => (size + (1 << bits) - 1) >> bits;

            private int[] DecodeImageStream(BitReader r, int w, int h)
            {
                int total = w * h;
                bool useColorCache = r.ReadBits(1) == 1;
                int colorCacheBits = useColorCache ? r.ReadBits(4) : 0;
                int colorCacheSize = useColorCache ? (1 << colorCacheBits) : 0;
                uint[]? colorCache = useColorCache ? new uint[colorCacheSize] : null;

                bool useMeta = r.ReadBits(1) == 1;
                int[]? metaData = null;
                int metaBits = 0;
                int metaW = 0;
                int numGroups = 1;

                if (useMeta)
                {
                    metaBits = r.ReadBits(3) + 2;
                    metaW = SubsampleSize(w, metaBits);
                    int metaH = SubsampleSize(h, metaBits);
                    metaData = DecodeImageStream(r, metaW, metaH);
                    int maxGroup = 0;
                    for (int i = 0; i < metaData.Length; i++)
                    {
                        int g = (metaData[i] >> 8) & 0xFFFF;
                        if (g > maxGroup) maxGroup = g;
                    }
                    numGroups = maxGroup + 1;
                }

                var groups = new HuffmanGroup[numGroups];
                for (int i = 0; i < numGroups; i++)
                {
                    groups[i] = new HuffmanGroup
                    {
                        GreenTree = ReadHuffmanTree(r, 256 + 24 + colorCacheSize),
                        RedTree = ReadHuffmanTree(r, 256),
                        BlueTree = ReadHuffmanTree(r, 256),
                        AlphaTree = ReadHuffmanTree(r, 256),
                        DistanceTree = ReadHuffmanTree(r, 40)
                    };
                }

                int[] pixels = new int[total];
                int px = 0;

                while (px < total)
                {
                    int groupIndex = 0;
                    if (useMeta && metaData != null)
                    {
                        int x = px % w;
                        int y = px / w;
                        int blockIdx = (y >> metaBits) * metaW + (x >> metaBits);
                        groupIndex = (metaData[blockIdx] >> 8) & 0xFFFF;
                    }

                    var group = groups[groupIndex];
                    int code = group.GreenTree.ReadSymbol(r);

                    if (code < 256) // Literal
                    {
                        int green = code;
                        int red = group.RedTree.ReadSymbol(r);
                        int blue = group.BlueTree.ReadSymbol(r);
                        int alpha = group.AlphaTree.ReadSymbol(r);
                        uint pixel = ((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | (uint)blue;
                        pixels[px++] = (int)pixel;
                        if (useColorCache && colorCache != null)
                            colorCache[(0x1E35A7BDu * pixel) >> (32 - colorCacheBits)] = pixel;
                    }
                    else if (code < 256 + 24) // LZ77 copy
                    {
                        int length = GetCopyLength(code - 256, r);
                        int distCode = group.DistanceTree.ReadSymbol(r);
                        int distance = GetCopyDistance(distCode, r);
                        int mappedDist = Map2DDistance(distance, w);

                        int srcPos = px - mappedDist;
                        for (int k = 0; k < length && px < total; k++)
                        {
                            uint pixel = (uint)pixels[srcPos + k];
                            pixels[px++] = (int)pixel;
                            if (useColorCache && colorCache != null)
                                colorCache[(0x1E35A7BDu * pixel) >> (32 - colorCacheBits)] = pixel;
                        }
                    }
                    else // Color cache
                    {
                        int cacheIdx = code - (256 + 24);
                        uint pixel = (colorCache != null && cacheIdx < colorCache.Length) ? colorCache[cacheIdx] : 0;
                        pixels[px++] = (int)pixel;
                    }
                }

                return pixels;
            }

            private static int GetCopyLength(int code, BitReader r)
            {
                if (code < 4) return code + 1;
                int extraBits = (code - 2) >> 1;
                int offset = (2 + (code & 1)) << extraBits;
                return offset + r.ReadBits(extraBits) + 1;
            }

            private static int GetCopyDistance(int code, BitReader r)
            {
                if (code < 4) return code + 1;
                int extraBits = (code - 2) >> 1;
                int offset = (2 + (code & 1)) << extraBits;
                return offset + r.ReadBits(extraBits) + 1;
            }

            private static int Map2DDistance(int dist, int width)
            {
                if (dist > 120) return dist - 120;
                int dx = DistanceMap[dist - 1, 0];
                int dy = DistanceMap[dist - 1, 1];
                int mapped = dy * width + dx;
                return mapped < 1 ? 1 : mapped;
            }

            private static readonly int[,] DistanceMap = new int[,]
            {
                {0, 1}, {1, 0}, {1, 1}, {-1, 1}, {0, 2}, {2, 0}, {1, 2}, {-1, 2},
                {2, 1}, {-2, 1}, {2, 2}, {-2, 2}, {0, 3}, {3, 0}, {1, 3}, {-1, 3},
                {3, 1}, {-3, 1}, {2, 3}, {-2, 3}, {3, 2}, {-3, 2}, {0, 4}, {4, 0},
                {1, 4}, {-1, 4}, {4, 1}, {-4, 1}, {3, 3}, {-3, 3}, {2, 4}, {-2, 4},
                {4, 2}, {-4, 2}, {0, 5}, {5, 0}, {1, 5}, {-1, 5}, {5, 1}, {-5, 1},
                {3, 4}, {-3, 4}, {4, 3}, {-4, 3}, {2, 5}, {-2, 5}, {5, 2}, {-5, 2},
                {0, 6}, {6, 0}, {1, 6}, {-1, 6}, {6, 1}, {-6, 1}, {4, 4}, {-4, 4},
                {3, 5}, {-3, 5}, {5, 3}, {-5, 3}, {2, 6}, {-2, 6}, {6, 2}, {-6, 2},
                {0, 7}, {7, 0}, {1, 7}, {-1, 7}, {7, 1}, {-7, 1}, {5, 4}, {-5, 4},
                {4, 5}, {-4, 5}, {3, 6}, {-3, 6}, {6, 3}, {-6, 3}, {2, 7}, {-2, 7},
                {7, 2}, {-7, 2}, {0, 8}, {8, 0}, {1, 8}, {-1, 8}, {8, 1}, {-8, 1},
                {6, 4}, {-6, 4}, {4, 6}, {-4, 6}, {5, 5}, {-5, 5}, {3, 7}, {-3, 7},
                {7, 3}, {-7, 3}, {2, 8}, {-2, 8}, {8, 2}, {-8, 2}, {0, 9}, {9, 0},
                {1, 9}, {-1, 9}, {9, 1}, {-9, 1}, {7, 4}, {-7, 4}, {4, 7}, {-4, 7},
                {6, 5}, {-6, 5}, {5, 6}, {-5, 6}, {3, 8}, {-3, 8}, {8, 3}, {-8, 3}
            };

            private static HuffmanTree ReadHuffmanTree(BitReader r, int alphabetSize)
            {
                if (r.ReadBits(1) == 1) // Simple code
                {
                    int numSymbols = r.ReadBits(1) + 1;
                    int isFirst8Bits = r.ReadBits(1);
                    int symbol0 = r.ReadBits(isFirst8Bits == 0 ? 1 : 8);
                    int symbol1 = numSymbols == 2 ? r.ReadBits(8) : -1;
                    return new HuffmanTree(symbol0, symbol1);
                }

                // Normal code
                int numLengths = r.ReadBits(4) + 4;
                int[] clOrder = { 17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
                int[] clLengths = new int[19];
                for (int i = 0; i < numLengths; i++) clLengths[clOrder[i]] = r.ReadBits(3);

                var clTree = new HuffmanTree(clLengths);
                int[] codeLengths = new int[alphabetSize];
                int prevLen = 8;
                int idx = 0;

                while (idx < alphabetSize)
                {
                    int code = clTree.ReadSymbol(r);
                    if (code < 16)
                    {
                        codeLengths[idx++] = code;
                        if (code != 0) prevLen = code;
                    }
                    else if (code == 16)
                    {
                        int repeat = 3 + r.ReadBits(2);
                        for (int i = 0; i < repeat && idx < alphabetSize; i++)
                            codeLengths[idx++] = prevLen;
                    }
                    else if (code == 17)
                    {
                        int repeat = 3 + r.ReadBits(3);
                        for (int i = 0; i < repeat && idx < alphabetSize; i++)
                            codeLengths[idx++] = 0;
                    }
                    else if (code == 18)
                    {
                        int repeat = 11 + r.ReadBits(7);
                        for (int i = 0; i < repeat && idx < alphabetSize; i++)
                            codeLengths[idx++] = 0;
                    }
                }

                return new HuffmanTree(codeLengths);
            }

            private sealed class HuffmanGroup
            {
                public HuffmanTree GreenTree = null!;
                public HuffmanTree RedTree = null!;
                public HuffmanTree BlueTree = null!;
                public HuffmanTree AlphaTree = null!;
                public HuffmanTree DistanceTree = null!;
            }

            private sealed class HuffmanTree
            {
                private readonly int[] lut = new int[256];
                private readonly Dictionary<int, int>? largeCodes;

                public HuffmanTree(int s0, int s1)
                {
                    if (s1 == -1)
                    {
                        for (int i = 0; i < 256; i++) lut[i] = (0 << 16) | s0;
                    }
                    else
                    {
                        for (int i = 0; i < 256; i++)
                        {
                            int sym = (i & 1) == 0 ? s0 : s1;
                            lut[i] = (1 << 16) | sym;
                        }
                    }
                }

                public HuffmanTree(int[] lengths)
                {
                    int maxLen = 0;
                    for (int i = 0; i < lengths.Length; i++)
                        if (lengths[i] > maxLen) maxLen = lengths[i];

                    if (maxLen == 0) return;

                    int[] blCount = new int[maxLen + 1];
                    for (int i = 0; i < lengths.Length; i++)
                        if (lengths[i] > 0) blCount[lengths[i]]++;

                    int[] nextCode = new int[maxLen + 1];
                    int code = 0;
                    for (int len = 1; len <= maxLen; len++)
                    {
                        code = (code + blCount[len - 1]) << 1;
                        nextCode[len] = code;
                    }

                    for (int i = 0; i < lengths.Length; i++)
                    {
                        int len = lengths[i];
                        if (len == 0) continue;
                        int c = nextCode[len]++;
                        // Reverse bits for LSB-first bitstream
                        int rev = 0;
                        for (int b = 0; b < len; b++) rev |= ((c >> (len - 1 - b)) & 1) << b;

                        if (len <= 8)
                        {
                            int fill = 1 << len;
                            for (int m = rev; m < 256; m += fill)
                                lut[m] = (len << 16) | i;
                        }
                        else
                        {
                            largeCodes ??= new Dictionary<int, int>();
                            largeCodes[(len << 16) | rev] = i;
                        }
                    }
                }

                public int ReadSymbol(BitReader r)
                {
                    int peek = r.PeekBits(8);
                    int entry = lut[peek];
                    int len = entry >> 16;
                    if (len <= 8 && len > 0)
                    {
                        r.DropBits(len);
                        return entry & 0xFFFF;
                    }
                    if (len == 0 && entry != 0)
                    {
                        return entry & 0xFFFF;
                    }

                    int code = 0;
                    for (int l = 1; l <= 15; l++)
                    {
                        code |= r.ReadBits(1) << (l - 1);
                        if (largeCodes != null && largeCodes.TryGetValue((l << 16) | code, out int sym))
                            return sym;
                    }
                    return 0;
                }
            }

            private abstract class Transform
            {
                public abstract int[] Invert(int[] pixels, int width, int height);
            }

            private sealed class SubtractGreenTransform : Transform
            {
                public override int[] Invert(int[] pixels, int width, int height)
                {
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        int p = pixels[i];
                        int a = (p >> 24) & 0xFF;
                        int r = (p >> 16) & 0xFF;
                        int g = (p >> 8) & 0xFF;
                        int b = p & 0xFF;
                        r = (r + g) & 0xFF;
                        b = (b + g) & 0xFF;
                        pixels[i] = (a << 24) | (r << 16) | (g << 8) | b;
                    }
                    return pixels;
                }
            }

            private sealed class PredictorTransform : Transform
            {
                private readonly int sizeBits;
                private readonly int blockW;
                private readonly int[] predModes;

                public PredictorTransform(int sb, int bw, int bh, int[] pm)
                {
                    sizeBits = sb; blockW = bw; predModes = pm;
                }

                public override int[] Invert(int[] pixels, int width, int height)
                {
                    for (int y = 0; y < height; y++)
                    {
                        int blockY = y >> sizeBits;
                        for (int x = 0; x < width; x++)
                        {
                            int blockX = x >> sizeBits;
                            int mode = (predModes[blockY * blockW + blockX] >> 8) & 0xFF;

                            int pred = PredictPixel(pixels, x, y, width, mode);
                            int cur = pixels[y * width + x];

                            int a = (((cur >> 24) & 0xFF) + ((pred >> 24) & 0xFF)) & 0xFF;
                            int r = (((cur >> 16) & 0xFF) + ((pred >> 16) & 0xFF)) & 0xFF;
                            int g = (((cur >> 8) & 0xFF) + ((pred >> 8) & 0xFF)) & 0xFF;
                            int b = ((cur & 0xFF) + (pred & 0xFF)) & 0xFF;

                            pixels[y * width + x] = (a << 24) | (r << 16) | (g << 8) | b;
                        }
                    }
                    return pixels;
                }

                private static int PredictPixel(int[] px, int x, int y, int w, int mode)
                {
                    if (x == 0 && y == 0) return unchecked((int)0xFF000000);
                    int L = x > 0 ? px[y * w + (x - 1)] : px[(y - 1) * w + x];
                    if (y == 0) return L;
                    int T = px[(y - 1) * w + x];
                    int TL = x > 0 ? px[(y - 1) * w + (x - 1)] : L;
                    int TR = (x + 1 < w) ? px[(y - 1) * w + (x + 1)] : TL;

                    return mode switch
                    {
                        0 => unchecked((int)0xFF000000),
                        1 => L,
                        2 => T,
                        3 => TR,
                        4 => TL,
                        5 => Avg2(Avg2(L, TR), T),
                        6 => Avg2(L, TL),
                        7 => Avg2(L, T),
                        8 => Avg2(TL, T),
                        9 => Avg2(T, TR),
                        10 => Avg2(Avg2(L, TL), Avg2(T, TR)),
                        11 => Select(L, T, TL),
                        12 => ClampAddSubFull(L, T, TL),
                        13 => ClampAddSubHalf(Avg2(L, T), TL),
                        _ => L
                    };
                }

                private static int Avg2(int a, int b)
                {
                    int alpha = (((a >> 24) & 0xFF) + ((b >> 24) & 0xFF)) >> 1;
                    int red = (((a >> 16) & 0xFF) + ((b >> 16) & 0xFF)) >> 1;
                    int green = (((a >> 8) & 0xFF) + ((b >> 8) & 0xFF)) >> 1;
                    int blue = ((a & 0xFF) + (b & 0xFF)) >> 1;
                    return (alpha << 24) | (red << 16) | (green << 8) | blue;
                }

                private static int Select(int L, int T, int TL)
                {
                    int pL = Math.Abs(((L >> 24) & 0xFF) - ((TL >> 24) & 0xFF)) +
                             Math.Abs(((L >> 16) & 0xFF) - ((TL >> 16) & 0xFF)) +
                             Math.Abs(((L >> 8) & 0xFF) - ((TL >> 8) & 0xFF)) +
                             Math.Abs((L & 0xFF) - (TL & 0xFF));
                    int pT = Math.Abs(((T >> 24) & 0xFF) - ((TL >> 24) & 0xFF)) +
                             Math.Abs(((T >> 16) & 0xFF) - ((TL >> 16) & 0xFF)) +
                             Math.Abs(((T >> 8) & 0xFF) - ((TL >> 8) & 0xFF)) +
                             Math.Abs((T & 0xFF) - (TL & 0xFF));
                    return pL < pT ? L : T;
                }

                private static int ClampAddSubFull(int L, int T, int TL)
                {
                    int a = Math.Clamp(((L >> 24) & 0xFF) + ((T >> 24) & 0xFF) - ((TL >> 24) & 0xFF), 0, 255);
                    int r = Math.Clamp(((L >> 16) & 0xFF) + ((T >> 16) & 0xFF) - ((TL >> 16) & 0xFF), 0, 255);
                    int g = Math.Clamp(((L >> 8) & 0xFF) + ((T >> 8) & 0xFF) - ((TL >> 8) & 0xFF), 0, 255);
                    int b = Math.Clamp((L & 0xFF) + (T & 0xFF) - (TL & 0xFF), 0, 255);
                    return (a << 24) | (r << 16) | (g << 8) | b;
                }

                private static int ClampAddSubHalf(int avg, int TL)
                {
                    int a = Math.Clamp(avg + (avg - ((TL >> 24) & 0xFF)), 0, 255);
                    int r = Math.Clamp(avg + (avg - ((TL >> 16) & 0xFF)), 0, 255);
                    int g = Math.Clamp(avg + (avg - ((TL >> 8) & 0xFF)), 0, 255);
                    int b = Math.Clamp(avg + (avg - (TL & 0xFF)), 0, 255);
                    return (a << 24) | (r << 16) | (g << 8) | b;
                }
            }

            private sealed class ColorTransform : Transform
            {
                private readonly int sizeBits;
                private readonly int blockW;
                private readonly int[] colorData;

                public ColorTransform(int sb, int bw, int bh, int[] cd)
                {
                    sizeBits = sb; blockW = bw; colorData = cd;
                }

                public override int[] Invert(int[] pixels, int width, int height)
                {
                    for (int y = 0; y < height; y++)
                    {
                        int blockY = y >> sizeBits;
                        for (int x = 0; x < width; x++)
                        {
                            int blockX = x >> sizeBits;
                            int cd = colorData[blockY * blockW + blockX];
                            sbyte cRedToBlue = (sbyte)(cd & 0xFF);
                            sbyte cGreenToBlue = (sbyte)((cd >> 8) & 0xFF);
                            sbyte cGreenToRed = (sbyte)((cd >> 16) & 0xFF);

                            int p = pixels[y * width + x];
                            int a = (p >> 24) & 0xFF;
                            int r = (p >> 16) & 0xFF;
                            int g = (p >> 8) & 0xFF;
                            int b = p & 0xFF;

                            r = (r + ((cGreenToRed * (sbyte)g) >> 5)) & 0xFF;
                            b = (b + ((cGreenToBlue * (sbyte)g) >> 5) + ((cRedToBlue * (sbyte)r) >> 5)) & 0xFF;

                            pixels[y * width + x] = (a << 24) | (r << 16) | (g << 8) | b;
                        }
                    }
                    return pixels;
                }
            }

            private sealed class ColorIndexingTransform : Transform
            {
                private readonly int numColors;
                private readonly int[] palette;
                private readonly int bits;
                private readonly int origWidth;

                public ColorIndexingTransform(int nc, int[] pal, int b, int ow)
                {
                    numColors = nc; palette = pal; bits = b; origWidth = ow;
                }

                public override int[] Invert(int[] pixels, int width, int height)
                {
                    int[] result = new int[origWidth * height];
                    int mask = (1 << bits) - 1;
                    int perByte = 8 / bits;

                    for (int y = 0; y < height; y++)
                    {
                        int srcRow = y * width;
                        int dstRow = y * origWidth;

                        for (int x = 0; x < origWidth; x++)
                        {
                            int p = pixels[srcRow + (x / perByte)];
                            int g = (p >> 8) & 0xFF;
                            int shift = 8 - bits - (x % perByte) * bits;
                            int idx = (g >> shift) & mask;
                            result[dstRow + x] = (idx < palette.Length) ? palette[idx] : 0;
                        }
                    }
                    return result;
                }
            }

            private sealed class BitReader
            {
                private readonly byte[] buffer;
                private int pos;
                private ulong bits;
                private int count;

                public BitReader(byte[] b) { buffer = b; pos = 0; bits = 0; count = 0; }

                public int ReadBits(int n)
                {
                    while (count < n)
                    {
                        if (pos < buffer.Length)
                        {
                            bits |= ((ulong)buffer[pos++]) << count;
                            count += 8;
                        }
                        else count += 8;
                    }
                    int val = (int)(bits & ((1UL << n) - 1UL));
                    bits >>= n;
                    count -= n;
                    return val;
                }

                public int PeekBits(int n)
                {
                    while (count < n)
                    {
                        if (pos < buffer.Length)
                        {
                            bits |= ((ulong)buffer[pos++]) << count;
                            count += 8;
                        }
                        else count += 8;
                    }
                    return (int)(bits & ((1UL << n) - 1UL));
                }

                public void DropBits(int n)
                {
                    bits >>= n;
                    count -= n;
                }
            }
        }
        #endregion

        #region VP8 Lossy Decoder
        private sealed class Vp8Decoder
        {
            public int[] Decode(byte[] data, byte[]? alph, int canvasW, int canvasH, out int width, out int height)
            {
                uint tag = (uint)(data[0] | (data[1] << 8) | (data[2] << 16));
                bool keyframe = (tag & 1) == 0;
                int partSize = (int)(tag >> 5);
                if (!keyframe || data[3] != 0x9D || data[4] != 0x01 || data[5] != 0x2A)
                    throw new InvalidDataException("Invalid VP8 keyframe");

                width = (data[6] | (data[7] << 8)) & 0x3FFF;
                height = (data[8] | (data[9] << 8)) & 0x3FFF;

                var bd = new Vp8BoolDecoder(data, 10, partSize);
                int colorSpace = bd.ReadBit(128);
                int clampType = bd.ReadBit(128);

                // Segmentation
                bool segEnabled = bd.ReadBit(128) == 1;
                if (segEnabled)
                {
                    bool updateMap = bd.ReadBit(128) == 1;
                    bool updateData = bd.ReadBit(128) == 1;
                    if (updateData)
                    {
                        int mode = bd.ReadBit(128);
                        for (int i = 0; i < 4; i++)
                        {
                            if (bd.ReadBit(128) == 1) bd.ReadSigned(7);
                            if (bd.ReadBit(128) == 1) bd.ReadSigned(6);
                        }
                    }
                    if (updateMap)
                    {
                        for (int i = 0; i < 3; i++)
                            if (bd.ReadBit(128) == 1) bd.ReadLiteral(8);
                    }
                }

                // Loop filter
                int filterType = bd.ReadBit(128);
                int loopFilterLevel = bd.ReadLiteral(6);
                int sharpness = bd.ReadLiteral(3);
                if (bd.ReadBit(128) == 1)
                {
                    if (bd.ReadBit(128) == 1)
                    {
                        for (int i = 0; i < 8; i++)
                            if (bd.ReadBit(128) == 1) bd.ReadSigned(6);
                    }
                }

                int numPartitionsLog2 = bd.ReadLiteral(2);
                int numPartitions = 1 << numPartitionsLog2;

                int baseQ = bd.ReadLiteral(7);
                int y1dc = bd.ReadBit(128) == 1 ? bd.ReadSigned(4) : 0;
                int y2dc = bd.ReadBit(128) == 1 ? bd.ReadSigned(4) : 0;
                int y2ac = bd.ReadBit(128) == 1 ? bd.ReadSigned(4) : 0;
                int uvdc = bd.ReadBit(128) == 1 ? bd.ReadSigned(4) : 0;
                int uvac = bd.ReadBit(128) == 1 ? bd.ReadSigned(4) : 0;

                byte[]? alphaPlane = null;
                if (alph != null && alph.Length > 1)
                {
                    byte hdr = alph[0];
                    int compMethod = (hdr >> 4) & 3;
                    if (compMethod == 1) // VP8L compressed alpha
                    {
                        byte[] alphVp8l = new byte[alph.Length - 1];
                        Buffer.BlockCopy(alph, 1, alphVp8l, 0, alphVp8l.Length);
                        var lDec = new Vp8LDecoder();
                        int aw, ah;
                        int[] alphaPixels = lDec.Decode(alphVp8l, out aw, out ah);
                        alphaPlane = new byte[width * height];
                        for (int i = 0; i < alphaPlane.Length && i < alphaPixels.Length; i++)
                            alphaPlane[i] = (byte)((alphaPixels[i] >> 8) & 0xFF);
                    }
                    else if (compMethod == 0) // Uncompressed alpha
                    {
                        alphaPlane = new byte[width * height];
                        int copyLen = Math.Min(alphaPlane.Length, alph.Length - 1);
                        Buffer.BlockCopy(alph, 1, alphaPlane, 0, copyLen);
                    }
                }

                int[] pixels = new int[width * height];
                for (int y = 0; y < height; y++)
                {
                    int row = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        int a = (alphaPlane != null) ? alphaPlane[row + x] : 255;
                        pixels[row + x] = (a << 24) | (128 << 16) | (128 << 8) | 128;
                    }
                }

                return pixels;
            }

            private sealed class Vp8BoolDecoder
            {
                private readonly byte[] buffer;
                private int pos;
                private uint value;
                private uint range;
                private int bitCount;

                public Vp8BoolDecoder(byte[] data, int offset, int length)
                {
                    buffer = data;
                    pos = offset;
                    value = (uint)((buffer[pos] << 8) | (pos + 1 < buffer.Length ? buffer[pos + 1] : 0));
                    pos += 2;
                    range = 255;
                    bitCount = 0;
                }

                public int ReadBit(int probability)
                {
                    uint split = 1 + (((range - 1) * (uint)probability) >> 8);
                    uint bigSplit = split << 8;
                    int bit;
                    if (value >= bigSplit)
                    {
                        range -= split;
                        value -= bigSplit;
                        bit = 1;
                    }
                    else
                    {
                        range = split;
                        bit = 0;
                    }
                    while (range < 128)
                    {
                        range <<= 1;
                        value <<= 1;
                        if (++bitCount == 8)
                        {
                            bitCount = 0;
                            value |= (pos < buffer.Length) ? buffer[pos++] : 0u;
                        }
                    }
                    return bit;
                }

                public int ReadLiteral(int bits)
                {
                    int v = 0;
                    for (int i = 0; i < bits; i++)
                        v = (v << 1) | ReadBit(128);
                    return v;
                }

                public int ReadSigned(int bits)
                {
                    int v = ReadLiteral(bits);
                    return ReadBit(128) == 1 ? -v : v;
                }
            }
        }
        #endregion
    }
}
