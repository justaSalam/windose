using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace Windose.Drivers
{
    public enum CursorType
    {
        Arrow = 0,
        Hand = 1,
        IBeam = 2,
        SizeWE = 3,
        SizeNS = 4,
        SizeNWSE = 5,
        SizeNESW = 6,
        SizeAll = 7,
        Wait = 8
    }

    public static class CursorManager
    {
        private static CursorType current = CursorType.Arrow;
        private static bool isBusy;
        private static readonly Dictionary<CursorType, Image> cursorCache = new();
        private static readonly Dictionary<CursorType, (int x, int y)> hotspotCache = new();

        public static CursorType Current
        {
            get => isBusy ? CursorType.Wait : current;
            set
            {
                if (!isBusy) current = value;
            }
        }

        public static bool IsBusy => isBusy;

        public static void SetCursor(CursorType type)
        {
            if (!isBusy) current = type;
        }

        public static void SetBusy(bool busy)
        {
            isBusy = busy;
        }

        public static (int hotspotX, int hotspotY) GetHotspot(CursorType type)
        {
            if (hotspotCache.TryGetValue(type, out var spot))
                return spot;

            spot = type switch
            {
                CursorType.Arrow => (0, 0),
                CursorType.Hand => (5, 0),
                CursorType.IBeam => (4, 7),
                CursorType.SizeWE => (8, 4),
                CursorType.SizeNS => (4, 8),
                CursorType.SizeNWSE => (7, 7),
                CursorType.SizeNESW => (7, 7),
                CursorType.SizeAll => (7, 7),
                CursorType.Wait => (5, 8),
                _ => (0, 0)
            };
            hotspotCache[type] = spot;
            return spot;
        }

        public static Image GetCursorImage(CursorType type)
        {
            if (cursorCache.TryGetValue(type, out var img))
                return img;

            Image generated = GenerateCursor(type);
            cursorCache[type] = generated;
            return generated;
        }

        private static Image GenerateCursor(CursorType type)
        {
            if (type == CursorType.Arrow && Cursors.arrow != null)
            {
                return Cursors.arrow;
            }

            return type switch
            {
                CursorType.SizeWE => CreateSizeWECursor(),
                CursorType.SizeNS => CreateSizeNSCursor(),
                CursorType.SizeNWSE => CreateSizeNWSECursor(),
                CursorType.SizeNESW => CreateSizeNESWCursor(),
                CursorType.IBeam => CreateIBeamCursor(),
                CursorType.Hand => CreateHandCursor(),
                CursorType.SizeAll => CreateSizeAllCursor(),
                CursorType.Wait => CreateWaitCursor(),
                _ => (Image?)Cursors.arrow ?? CreateFallbackArrow()
            };
        }

        private const int C_TRANS = 0x00000000;
        private const int C_BLACK = unchecked((int)0xFF000000);
        private const int C_WHITE = unchecked((int)0xFFFFFFFF);
        private const int C_GOLD  = unchecked((int)0xFFD4AF37);

        private static Bitmap CreateBitmapFromPattern(int w, int h, string[] pattern)
        {
            Bitmap bmp = new Bitmap(w, h, ColorDepth.ColorDepth32);
            int[] raw = bmp.RawData;
            if (raw == null) return bmp;

            for (int y = 0; y < h; y++)
            {
                string row = y < pattern.Length ? pattern[y] : "";
                int offset = y * w;
                for (int x = 0; x < w; x++)
                {
                    char c = x < row.Length ? row[x] : ' ';
                    raw[offset + x] = c switch
                    {
                        'B' or '#' => C_BLACK,
                        'W' or '.' => C_WHITE,
                        'G' => C_GOLD,
                        _ => C_TRANS
                    };
                }
            }
            return bmp;
        }

        private static Bitmap CreateFallbackArrow()
        {
            string[] p = new[]
            {
                "B          ",
                "BB         ",
                "BWB        ",
                "BWW        ",
                "BWWWB      ",
                "BWWWWB     ",
                "BWWWWWB    ",
                "BWWWWWWB   ",
                "BWWWWWWWB  ",
                "BWWWWWWWWB ",
                "BWWWWWWWWWB",
                "BWWWWBBBBBB",
                "BWWWB      ",
                "BW BWB     ",
                "B   BWB    ",
                "    BWB    ",
                "     B     "
            };
            return CreateBitmapFromPattern(11, 17, p);
        }

        private static Bitmap CreateSizeWECursor()
        {
            string[] p = new[]
            {
                "    B       B    ",
                "   BB       BB   ",
                "  BWB       BWB  ",
                " BWWBBBBBBBBBWW B",
                "BWWWWWWWWWWWWWWWB",
                " BWWBBBBBBBBBWW B",
                "  BWB       BWB  ",
                "   BB       BB   ",
                "    B       B    "
            };
            return CreateBitmapFromPattern(17, 9, p);
        }

        private static Bitmap CreateSizeNSCursor()
        {
            string[] p = new[]
            {
                "    B    ",
                "   BWB   ",
                "  BWWWB  ",
                " BWWWWWB ",
                "BBBBWBBBB",
                "   BWB   ",
                "   BWB   ",
                "   BWB   ",
                "   BWB   ",
                "   BWB   ",
                "   BWB   ",
                "   BWB   ",
                "BBBBWBBBB",
                " BWWWWWB ",
                "  BWWWB  ",
                "   BWB   ",
                "    B    "
            };
            return CreateBitmapFromPattern(9, 17, p);
        }

        private static Bitmap CreateSizeNWSECursor()
        {
            string[] p = new[]
            {
                "BBBBBB         ",
                "BWWWWWB        ",
                "BWWWWBB        ",
                "BWW BWB        ",
                "BWB  BWB       ",
                "BB    BWB      ",
                "       BWB     ",
                "        BWB    ",
                "       BWB BB  ",
                "      BWB  BWB ",
                "     BWB  BWW B",
                "     BBWWWWWW B",
                "      BWWWWWW B",
                "       BWWWWWW B",
                "        BBBBBB "
            };
            return CreateBitmapFromPattern(15, 15, p);
        }

        private static Bitmap CreateSizeNESWCursor()
        {
            string[] p = new[]
            {
                "         BBBBBB",
                "        BWWWWWB",
                "        BBWWWW B",
                "        BWB WWW B",
                "       BWB  BWB",
                "      BWB    BB",
                "     BWB       ",
                "    BWB        ",
                "  BB BWB       ",
                " BWB  BWB      ",
                "BWW B  BWB     ",
                "BWWWWWWBB      ",
                "BWWWWWW B      ",
                "BWWWWWW B      ",
                " BBBBBB        "
            };
            return CreateBitmapFromPattern(15, 15, p);
        }

        private static Bitmap CreateIBeamCursor()
        {
            string[] p = new[]
            {
                "BBBB BBB",
                "BWWWBWWW",
                " BWB BWB",
                "  BWB   ",
                "  BWB   ",
                "  BWB   ",
                "  BWB   ",
                "  BWB   ",
                "  BWB   ",
                "  BWB   ",
                "  BWB   ",
                "  BWB   ",
                " BWB BWB",
                "BWWWBWWW",
                "BBBB BBB"
            };
            return CreateBitmapFromPattern(8, 15, p);
        }

        private static Bitmap CreateHandCursor()
        {
            string[] p = new[]
            {
                "     BB        ",
                "    BWW B      ",
                "    BWW B      ",
                "    BWW B      ",
                "    BWW B  BB  ",
                "    BWW B BBWB ",
                " BB BWW B BWW B",
                "BWW BWW B BWW B",
                "BWW BWW BBBWW B",
                "BWW BWWWWWWWW B",
                " BWWWWWWWWWWW B",
                "  BWWWWWWWWWW B",
                "  BWWWWWWWWWW B",
                "  BWWWWWWWWWW B",
                "   BWWWWWWWW B ",
                "   BWWWWWWWW B ",
                "    BBBBBBBB   "
            };
            return CreateBitmapFromPattern(15, 17, p);
        }

        private static Bitmap CreateSizeAllCursor()
        {
            string[] p = new[]
            {
                "      B        ",
                "     BWB       ",
                "    BWWWB      ",
                "   BBBWBBB     ",
                "     BWB       ",
                " B   BWB   B   ",
                "BWB BBBBB BWB  ",
                "BWWWWWWWWWWWB  ",
                "BWB BBBBB BWB  ",
                " B   BWB   B   ",
                "     BWB       ",
                "   BBBWBBB     ",
                "    BWWWB      ",
                "     BWB       ",
                "      B        "
            };
            return CreateBitmapFromPattern(15, 15, p);
        }

        private static Bitmap CreateWaitCursor()
        {
            string[] p = new[]
            {
                "BBBBBBBBBBB",
                "BWWWWWWWWWB",
                " BWWWWWWWB ",
                "  BWWWWWB  ",
                "   BGGGB   ",
                "    BGB    ",
                "    BGB    ",
                "   BGGGB   ",
                "  BGGGGGB  ",
                " BGGGGGGGB ",
                "BGGGGGGGGGB",
                "BBBBBBBBBBB"
            };
            return CreateBitmapFromPattern(11, 12, p);
        }
    }
}
