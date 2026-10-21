using System.Drawing;

public static class CompositorSelfTests
{
    public static void Run()
    {
        TestAlphaDrawing();
        TestClippedAlphaBlit();
        TestNestedClipContext();
        TestPartialRegionComposition();
        TestGlassTransparency();
        TestGlassBlurImpulse();
        TestGlassBlurKeepsUniformColor();
    }

    /// <summary>Glass surfaces depend on alpha surviving raw fills and source-over layers.</summary>
    private static void TestGlassTransparency()
    {
        ComponentBuffer bitmap = new ComponentBuffer(4, 1);
        bitmap.Clear(Color.Black);

        // Punch two pixels to fully transparent, then tint one of them.
        bitmap.FillRaw(Color.FromArgb(0, 0, 0, 0), 0, 0, 2, 1);
        bitmap.DrawOverRectangle(Color.FromArgb(128, 255, 0, 0), 0, 0, 1, 1);

        AssertPixel(bitmap, 0, 0, unchecked((int)0x80FF0000)); // tint kept its alpha
        AssertPixel(bitmap, 1, 0, 0x00000000);                 // still see-through
        AssertPixel(bitmap, 2, 0, Color.Black.ToArgb());       // untouched
    }

    /// <summary>A blurred impulse spreads across its box window with clamped edges.</summary>
    private static void TestGlassBlurImpulse()
    {
        ComponentBuffer bitmap = new ComponentBuffer(6, 1);
        bitmap.Clear(Color.Black);
        bitmap.FillRaw(Color.White, 2, 0, 1, 1);

        bitmap.BlurArea(0, 0, 6, 1, 1, 1);

        int[] expected =
        {
            Color.Black.ToArgb(),
            Color.FromArgb(255, 85, 85, 85).ToArgb(),
            Color.FromArgb(255, 85, 85, 85).ToArgb(),
            Color.FromArgb(255, 85, 85, 85).ToArgb(),
            Color.Black.ToArgb(),
            Color.Black.ToArgb(),
        };

        for (int x = 0; x < expected.Length; x++)
            AssertPixel(bitmap, x, 0, expected[x]);
    }

    /// <summary>Blurring a flat colour must leave it untouched (index and clamping maths).</summary>
    private static void TestGlassBlurKeepsUniformColor()
    {
        ComponentBuffer bitmap = new ComponentBuffer(6, 3);
        Color flat = Color.FromArgb(255, 0x30, 0x50, 0x70);
        bitmap.Clear(flat);

        bitmap.BlurArea(0, 0, 6, 3, 3, 2);

        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 6; x++)
                AssertPixel(bitmap, x, y, flat.ToArgb());
    }

    private static void TestAlphaDrawing()
    {
        ComponentBuffer bitmap = new ComponentBuffer(4, 3);
        bitmap.Clear(Color.Black);

        bitmap.DrawFilledRectangle(Color.FromArgb(128, 255, 255, 255), 0, 0, 2, 1);
        bitmap.DrawHorizontalLine(Color.FromArgb(128, 255, 0, 0), 4, -1, 1);
        bitmap.DrawVerticalLine(Color.FromArgb(128, 0, 255, 0), 2, 3, 1);

        AssertPixel(bitmap, 0, 0, unchecked((int)0xFF7F7F7F));
        AssertPixel(bitmap, 1, 0, unchecked((int)0xFF7F7F7F));
        AssertPixel(bitmap, 0, 1, unchecked((int)0xFF7F0000));
        AssertPixel(bitmap, 2, 1, unchecked((int)0xFF7F0000));
        AssertPixel(bitmap, 3, 1, unchecked((int)0xFF007F00));
        AssertPixel(bitmap, 3, 2, unchecked((int)0xFF007F00));
        AssertPixel(bitmap, 3, 0, Color.Black.ToArgb());
    }

    private static void TestClippedAlphaBlit()
    {
        ComponentBuffer bitmap = new ComponentBuffer(3, 1);
        bitmap.Clear(Color.Black);
        int[] source =
        {
            unchecked((int)0xFFFF0000),
            unchecked((int)0x8000FF00),
            unchecked((int)0xFF0000FF),
        };

        bitmap.DrawArrayAlphaClipped(source, 3, 0, 0, -1, 0, 3, 1);

        AssertPixel(bitmap, 0, 0, unchecked((int)0xFF007F00));
        AssertPixel(bitmap, 1, 0, unchecked((int)0xFF0000FF));
        AssertPixel(bitmap, 2, 0, Color.Black.ToArgb());
    }

    private static void TestNestedClipContext()
    {
        ComponentBuffer bitmap = new ComponentBuffer(4, 3);
        bitmap.Clear(Color.Black);

        if (!bitmap.PushContext(1, 1, 2, 1, new Rectangle(0, 0, 4, 3)))
            throw new InvalidOperationException("Unable to push compositor test clip context.");

        try
        {
            bitmap.DrawFilledRectangle(Color.White, 0, 0, 4, 1);
        }
        finally
        {
            bitmap.PopContext();
        }

        AssertPixel(bitmap, 0, 1, Color.Black.ToArgb());
        AssertPixel(bitmap, 1, 1, Color.White.ToArgb());
        AssertPixel(bitmap, 2, 1, Color.White.ToArgb());
        AssertPixel(bitmap, 3, 1, Color.Black.ToArgb());
    }

    private static void TestPartialRegionComposition()
    {
        ComponentBuffer screen = new ComponentBuffer(5, 1);
        ComponentBuffer background = new ComponentBuffer(5, 1);
        ComponentBuffer overlay = new ComponentBuffer(1, 1);
        screen.Clear(Color.Blue);
        background.Clear(Color.Red);
        overlay.Clear(Color.Green);

        // Draw background starting at x=1 so x=0 remains the sentinel blue value.
        screen.DrawArrayClipped(background.Buffer, background.Width, 0, 0, 1, 0, 4, 1);
        // Place a one-pixel overlay at x=2 (old overlay bounds).
        screen.DrawArrayClipped(overlay.Buffer, overlay.Width, 0, 0, 2, 0, 1, 1);

        // Remove the overlay: paint black over its old bounds then restore the lower surface
        // only in that exact one-pixel invalidated region.
        screen.DrawFilledRectangle(Color.Black, 2, 0, 1, 1);
        screen.DrawArrayClipped(background.Buffer, background.Width, 2, 0, 2, 0, 1, 1);

        AssertPixel(screen, 0, 0, Color.Blue.ToArgb());
        AssertPixel(screen, 1, 0, Color.Red.ToArgb());
        AssertPixel(screen, 2, 0, Color.Red.ToArgb());
        AssertPixel(screen, 3, 0, Color.Red.ToArgb());
        AssertPixel(screen, 4, 0, Color.Red.ToArgb());
    }

    private static void AssertPixel(ComponentBuffer bitmap, int x, int y, int expected)
    {
        int actual = bitmap.Buffer[y * bitmap.Width + x];
        if (actual != expected)
            throw new InvalidOperationException($"Compositor pixel mismatch at ({x}, {y}): expected 0x{expected:X8}, got 0x{actual:X8}.");
    }
}
