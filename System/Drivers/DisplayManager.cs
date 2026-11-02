using System;
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

    private bool firstPresent = true;
    private int lastCursorX = int.MinValue;
    private int lastCursorY = int.MinValue;
    private CursorType lastCursorType = CursorType.Arrow;
    private bool lastStarting;
    private bool lastCursorBlink;

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
        CursorType currentCursor = CursorManager.Current;

        // Skip the fullscreen upload when neither the backbuffer nor the cursor state changed.
        bool cursorChanged = cursorX != lastCursorX || cursorY != lastCursorY
            || currentCursor != lastCursorType
            || isStarting != lastStarting
            || (isStarting && cursorBlink != lastCursorBlink);

        if (!firstPresent && !contentChanged && !cursorChanged)
            return;

        firstPresent = false;
        lastCursorX = cursorX;
        lastCursorY = cursorY;
        lastCursorType = currentCursor;
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

    private void DrawCursor(int x, int y, bool isHiddenByBlink)
    {
        if (isHiddenByBlink) return;
        CursorType cursorType = CursorManager.Current;
        var (hotspotX, hotspotY) = CursorManager.GetHotspot(cursorType);
        Image img = CursorManager.GetCursorImage(cursorType);
        int drawX = x - hotspotX;
        int drawY = y - hotspotY;
        canvas.DrawImage(img, drawX, drawY);
    }
}
