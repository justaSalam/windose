using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Windose.System.Kernel;

namespace Windose.Drivers;

public sealed class DisplayManager
{
    public Canvas canvas { get; private set; }
    public ComponentBuffer BackBuffer { get; private set; }
    public int Width => canvas == null ? 0 : canvas.Width;
    public int Height => canvas == null ? 0 : canvas.Height;

    public DisplayManager()
    {
        canvas = Kernel.canvas;

        int width = (int)Registry.GetInteger("System/Display/Width", 1920);
        int height = (int)Registry.GetInteger("System/Display/Height", 1080);
        int depth = (int)Registry.GetInteger("System/Display/BitsPerPixel", 32);

        BackBuffer = new ComponentBuffer(canvas.Width, canvas.Height);
    }

    public void Present(int cursorX, int cursorY, bool contentChanged)
    {
        bool isStarting = LaunchTracker.isStarting;
        bool cursorBlink = isStarting && ((Environment.TickCount / 150) % 2 != 0);

        // Skip the fullscreen upload when neither the backbuffer nor the cursor state changed.
        bool cursorChanged = cursorX != lastCursorX || cursorY != lastCursorY
            || isStarting != lastStarting
            || (isStarting && cursorBlink != lastCursorBlink);

        if (!firstPresent && !contentChanged && !cursorChanged)
            return;

        firstPresent = false;
        lastCursorX = cursorX;
        lastCursorY = cursorY;
        lastStarting = isStarting;
        lastCursorBlink = cursorBlink;

        long uploadStartedAt = PerformanceMetrics.Now;
        canvas.DrawCanvas(BackBuffer, 0, 0);
        PerformanceMetrics.UploadTicks = PerformanceMetrics.Now - uploadStartedAt;

        long overlayStartedAt = PerformanceMetrics.Now;
        DrawCursor(cursorX, cursorY, cursorBlink);
        PerformanceMetrics.OverlayTicks = PerformanceMetrics.Now - overlayStartedAt;

        long displayStartedAt = PerformanceMetrics.Now;
        canvas.Display();
        PerformanceMetrics.DisplayTicks = PerformanceMetrics.Now - displayStartedAt;
    }

    private bool firstPresent = true;
    private int lastCursorX = int.MinValue;
    private int lastCursorY = int.MinValue;
    private bool lastStarting;
    private bool lastCursorBlink;

    private void DrawCursor(int x, int y, bool isHiddenByBlink)
    {
        if (isHiddenByBlink) return;
        canvas.DrawImage(Cursors.arrow, x, y);
    }
}
