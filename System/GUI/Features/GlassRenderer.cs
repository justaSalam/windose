using System.Drawing;
using Windose;

/// <summary>
/// Tuning knobs for a glass (Windows-7-like translucent) surface.
/// The compositor turns whatever was already drawn under a glass surface into a
/// blurred, washed-out backdrop, and the surface itself is then composited over it
/// with alpha blending, so its see-through pixels show the backdrop.
/// </summary>
public struct GlassStyle
{
    /// <summary>Radius of the backdrop blur in pixels. 0 skips blurring (cheapest glass).</summary>
    public int BlurRadius;

    /// <summary>Box blur passes. 2 passes approximate a gaussian.</summary>
    public int BlurPasses;

    /// <summary>0..255: how far the blurred backdrop is washed towards white.</summary>
    public byte Whiten;

    /// <summary>0..255: how much colour is pulled out of the blurred backdrop.</summary>
    public byte Desaturate;

    /// <summary>Tint laid over the backdrop. Its alpha channel is the tint strength.</summary>
    public Color Tint;

    /// <summary>Bright bevel line drawn on glass edges.</summary>
    public Color BorderBright;

    /// <summary>Dark outline drawn around glass edges.</summary>
    public Color BorderDark;

    /// <summary>Windows-7 "Aero" glass tinted with the given hue.</summary>
    public static GlassStyle Aero(Color tint) => new GlassStyle
    {
        BlurRadius = 8,
        BlurPasses = 2,
        Whiten = 150,
        Desaturate = 90,
        Tint = Color.FromArgb(90, tint.R, tint.G, tint.B),
        BorderBright = Color.FromArgb(220, 255, 255, 255),
        BorderDark = Color.FromArgb(160, 40, 56, 74),
    };

    /// <summary>Windows 7 Aero glass with signature translucent blue tint.</summary>
    public static GlassStyle AeroDefault() => Aero(Color.FromArgb(120, 50, 100, 165));

    /// <summary>Windows 7 Taskbar dark translucent Aero glass.</summary>
    public static GlassStyle AeroTaskbar() => new GlassStyle
    {
        BlurRadius = 6,
        BlurPasses = 2,
        Whiten = 50,
        Desaturate = 40,
        Tint = Color.FromArgb(190, 24, 38, 54),
        BorderBright = Color.FromArgb(130, 255, 255, 255),
        BorderDark = Color.FromArgb(200, 12, 18, 28),
    };

    /// <summary>Windows 7 Start Menu translucent Aero glass.</summary>
    public static GlassStyle AeroStartMenu() => new GlassStyle
    {
        BlurRadius = 8,
        BlurPasses = 2,
        Whiten = 70,
        Desaturate = 50,
        Tint = Color.FromArgb(210, 28, 44, 62),
        BorderBright = Color.FromArgb(140, 255, 255, 255),
        BorderDark = Color.FromArgb(220, 15, 22, 32),
    };

    /// <summary>Cheap "frosted" glass: blurred backdrop, no tint wash.</summary>
    public static GlassStyle Frosted(int radius) => new GlassStyle
    {
        BlurRadius = radius,
        BlurPasses = 2,
        Whiten = 90,
        Desaturate = 60,
        Tint = Color.FromArgb(0, 255, 255, 255),
        BorderBright = Color.FromArgb(200, 255, 255, 255),
        BorderDark = Color.FromArgb(140, 30, 34, 40),
    };

    /// <summary>Tint-only glass: no blur at all, just a translucent wash. Very cheap.</summary>
    public static GlassStyle Tinted(Color tint, byte strength) => new GlassStyle
    {
        BlurRadius = 0,
        BlurPasses = 1,
        Whiten = 0,
        Desaturate = 0,
        Tint = Color.FromArgb(strength, tint.R, tint.G, tint.B),
        BorderBright = Color.FromArgb(200, 255, 255, 255),
        BorderDark = Color.FromArgb(140, 30, 34, 40),
    };
}

/// <summary>
/// Pixel-level glass engine. Every routine is plain integer maths on ARGB arrays so it
/// works both on the screen backbuffer (backdrop blur behind a window) and on any
/// component's own render surface (frosted panels inside a window).
/// </summary>
public static class GlassRenderer
{
    public const int MaxBlurRadius = 32;
    public const int MaxBlurPasses = 3;

    // Reused scratch surfaces so glass composition does not allocate per frame.
    private static int[] scratchA = new int[0];
    private static int[] scratchB = new int[0];

    /// <summary>True when a style produces any visible backdrop treatment.</summary>
    public static bool IsVisibleThrough(in GlassStyle style)
        => style.BlurRadius > 0 || style.Whiten > 0 || style.Desaturate > 0 || style.Tint.A > 0;

    // ---- backdrop pipeline (screen side) ----

    /// <summary>
    /// Turns whatever is currently in the screen backbuffer under <paramref name="screenRect"/>
    /// into the blurred, washed backdrop a glass surface is composited over.
    /// Call it after everything below the glass surface has been composited and before the
    /// glass surface itself is blitted.
    /// </summary>
    public static void ComposeBackdrop(Rectangle screenRect, in GlassStyle style)
    {
        ComponentBuffer screen = Kernel.mainBuffer;
        if (screen == null) return;

        Rectangle region = screen.ClippedRegion(screenRect.X, screenRect.Y, screenRect.Width, screenRect.Height);
        if (region.Width <= 0 || region.Height <= 0) return;

        ApplyInPlace(screen.GetBuffer(), screen.Width, region, style);
    }

    /// <summary>
    /// Same pipeline, but applied to whatever is already inside a component's own buffer.
    /// Useful for frosted panels: draw what should be behind first, then frost it.
    /// </summary>
    public static void ComposeInBuffer(ComponentBuffer target, Rectangle localRegion, in GlassStyle style)
    {
        if (target == null) return;

        Rectangle region = target.ClippedRegion(localRegion.X, localRegion.Y, localRegion.Width, localRegion.Height);
        if (region.Width <= 0 || region.Height <= 0) return;

        ApplyInPlace(target.GetBuffer(), target.Width, region, style);
    }

    /// <summary>Copies a screen region out for custom drawing (reflections, sampling, ...).</summary>
    public static bool SampleScreen(Rectangle screenRect, int[] destination)
    {
        ComponentBuffer screen = Kernel.mainBuffer;
        if (screen == null || destination == null) return false;

        Rectangle region = screen.ClippedRegion(screenRect.X, screenRect.Y, screenRect.Width, screenRect.Height);
        if (region.Width <= 0 || region.Height <= 0) return false;
        if (destination.Length < region.Width * region.Height) return false;

        CopyRegion(screen.GetBuffer(), screen.Width, region, destination);
        return true;
    }

    private static void ApplyInPlace(int[] pixels, int stride, in Rectangle region, in GlassStyle style)
    {
        if (style.BlurRadius > 0)
            BlurPixels(pixels, stride, region, style.BlurRadius, style.BlurPasses);
        if (style.Desaturate > 0)
            DesaturatePixels(pixels, stride, region, style.Desaturate);
        if (style.Whiten > 0)
            LightenPixels(pixels, stride, region, style.Whiten);
        if (style.Tint.A > 0)
            TintPixels(pixels, stride, region, style.Tint);
    }

    // ---- raw pixel operations ----

    /// <summary>
    /// Separable box blur of a rectangle inside a pixel array. Running sums keep it O(pixels)
    /// per pass, and samples outside the rectangle are clamped to its edges so the blur never
    /// reads stale pixels outside the composited region.
    /// </summary>
    public static void BlurPixels(int[] pixels, int stride, in Rectangle region, int radius, int passes)
    {
        if (pixels == null || stride <= 0) return;

        radius = Math.Clamp(radius, 0, MaxBlurRadius);
        passes = Math.Clamp(passes, 1, MaxBlurPasses);
        if (radius == 0) return;

        Rectangle rect = ClampToBuffer(pixels, stride, region);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        int count = rect.Width * rect.Height;
        EnsureScratch(count);

        CopyRegion(pixels, stride, rect, scratchA);

        int[] source = scratchA;
        int[] target = scratchB;
        for (int pass = 0; pass < passes; pass++)
        {
            BlurHorizontal(source, rect.Width, rect.Height, target, radius);
            BlurVertical(target, rect.Width, rect.Height, source, radius);
        }

        WriteRegion(pixels, stride, rect, source);
    }

    /// <summary>Washes a rectangle towards white (Aero's glass "glow").</summary>
    public static void LightenPixels(int[] pixels, int stride, in Rectangle region, byte amount)
    {
        if (pixels == null || stride <= 0 || amount == 0) return;

        Rectangle rect = ClampToBuffer(pixels, stride, region);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        int inverse = 255 - amount;
        int offset = rect.Y * stride + rect.X;

        for (int y = 0; y < rect.Height; y++, offset += stride)
        {
            int index = offset;
            int end = index + rect.Width;
            for (; index < end; index++)
            {
                int pixel = pixels[index];
                int red = ((((pixel >> 16) & 0xff) * inverse) + 255 * amount) >> 8;
                int green = ((((pixel >> 8) & 0xff) * inverse) + 255 * amount) >> 8;
                int blue = (((pixel & 0xff) * inverse) + 255 * amount) >> 8;
                pixels[index] = (pixel & unchecked((int)0xFF000000)) | (red << 16) | (green << 8) | blue;
            }
        }
    }

    /// <summary>Pulls colour out of a rectangle, leaving it silvery like frosted glass.</summary>
    public static void DesaturatePixels(int[] pixels, int stride, in Rectangle region, byte amount)
    {
        if (pixels == null || stride <= 0 || amount == 0) return;

        Rectangle rect = ClampToBuffer(pixels, stride, region);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        int offset = rect.Y * stride + rect.X;

        for (int y = 0; y < rect.Height; y++, offset += stride)
        {
            int index = offset;
            int end = index + rect.Width;
            for (; index < end; index++)
            {
                int pixel = pixels[index];
                int red = (pixel >> 16) & 0xff;
                int green = (pixel >> 8) & 0xff;
                int blue = pixel & 0xff;

                int grey = (red * 77 + green * 151 + blue * 28) >> 8;
                red += ((grey - red) * amount) >> 8;
                green += ((grey - green) * amount) >> 8;
                blue += ((grey - blue) * amount) >> 8;

                pixels[index] = (pixel & unchecked((int)0xFF000000)) | (red << 16) | (green << 8) | blue;
            }
        }
    }

    /// <summary>Source-over tint of a rectangle. Alpha is treated as tint strength.</summary>
    public static void TintPixels(int[] pixels, int stride, in Rectangle region, Color tint)
    {
        if (pixels == null || stride <= 0) return;

        int alpha = tint.A;
        if (alpha == 0) return;

        Rectangle rect = ClampToBuffer(pixels, stride, region);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        int inverse = 255 - alpha;
        int tintRed = tint.R;
        int tintGreen = tint.G;
        int tintBlue = tint.B;
        int offset = rect.Y * stride + rect.X;

        for (int y = 0; y < rect.Height; y++, offset += stride)
        {
            int index = offset;
            int end = index + rect.Width;
            for (; index < end; index++)
            {
                int pixel = pixels[index];
                int red = (tintRed * alpha + ((pixel >> 16) & 0xff) * inverse) >> 8;
                int green = (tintGreen * alpha + ((pixel >> 8) & 0xff) * inverse) >> 8;
                int blue = (tintBlue * alpha + (pixel & 0xff) * inverse) >> 8;
                pixels[index] = (pixel & unchecked((int)0xFF000000)) | (red << 16) | (green << 8) | blue;
            }
        }
    }

    // ---- internals ----

    private static Rectangle ClampToBuffer(int[] pixels, int stride, in Rectangle region)
    {
        int rows = pixels.Length / stride;

        int left = Math.Clamp(region.X, 0, stride);
        int top = Math.Clamp(region.Y, 0, rows);
        int right = Math.Clamp(region.X + region.Width, left, stride);
        int bottom = Math.Clamp(region.Y + region.Height, top, rows);

        return new Rectangle(left, top, right - left, bottom - top);
    }

    private static void EnsureScratch(int count)
    {
        if (scratchA.Length < count) scratchA = new int[count];
        if (scratchB.Length < count) scratchB = new int[count];
    }

    private static void CopyRegion(int[] pixels, int stride, in Rectangle rect, int[] destination)
    {
        int offset = rect.Y * stride + rect.X;
        for (int y = 0; y < rect.Height; y++)
            Array.Copy(pixels, offset + y * stride, destination, y * rect.Width, rect.Width);
    }

    private static void WriteRegion(int[] pixels, int stride, in Rectangle rect, int[] source)
    {
        int offset = rect.Y * stride + rect.X;
        for (int y = 0; y < rect.Height; y++)
            Array.Copy(source, y * rect.Width, pixels, offset + y * stride, rect.Width);
    }

    private static void BlurHorizontal(int[] source, int width, int height, int[] target, int radius)
    {
        int divisor = radius * 2 + 1;

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            int sumRed = 0, sumGreen = 0, sumBlue = 0, sumAlpha = 0;

            for (int k = -radius; k <= radius; k++)
            {
                int sample = source[row + Math.Clamp(k, 0, width - 1)];
                sumRed += (sample >> 16) & 0xff;
                sumGreen += (sample >> 8) & 0xff;
                sumBlue += sample & 0xff;
                sumAlpha += (sample >> 24) & 0xff;
            }

            for (int x = 0; x < width; x++)
            {
                target[row + x] =
                    ((sumAlpha / divisor) << 24) |
                    ((sumRed / divisor) << 16) |
                    ((sumGreen / divisor) << 8) |
                    (sumBlue / divisor);

                int added = source[row + Math.Min(x + radius + 1, width - 1)];
                int removed = source[row + Math.Max(x - radius, 0)];

                sumRed += ((added >> 16) & 0xff) - ((removed >> 16) & 0xff);
                sumGreen += ((added >> 8) & 0xff) - ((removed >> 8) & 0xff);
                sumBlue += (added & 0xff) - (removed & 0xff);
                sumAlpha += ((added >> 24) & 0xff) - ((removed >> 24) & 0xff);
            }
        }
    }

    private static void BlurVertical(int[] source, int width, int height, int[] target, int radius)
    {
        int divisor = radius * 2 + 1;

        for (int x = 0; x < width; x++)
        {
            int sumRed = 0, sumGreen = 0, sumBlue = 0, sumAlpha = 0;

            for (int k = -radius; k <= radius; k++)
            {
                int sample = source[Math.Clamp(k, 0, height - 1) * width + x];
                sumRed += (sample >> 16) & 0xff;
                sumGreen += (sample >> 8) & 0xff;
                sumBlue += sample & 0xff;
                sumAlpha += (sample >> 24) & 0xff;
            }

            for (int y = 0; y < height; y++)
            {
                target[y * width + x] =
                    ((sumAlpha / divisor) << 24) |
                    ((sumRed / divisor) << 16) |
                    ((sumGreen / divisor) << 8) |
                    (sumBlue / divisor);

                int added = source[Math.Min(y + radius + 1, height - 1) * width + x];
                int removed = source[Math.Max(y - radius, 0) * width + x];

                sumRed += ((added >> 16) & 0xff) - ((removed >> 16) & 0xff);
                sumGreen += ((added >> 8) & 0xff) - ((removed >> 8) & 0xff);
                sumBlue += (added & 0xff) - (removed & 0xff);
                sumAlpha += ((added >> 24) & 0xff) - ((removed >> 24) & 0xff);
            }
        }
    }
}
