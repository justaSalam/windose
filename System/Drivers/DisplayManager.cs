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
    private Rectangle lastCursorRect;
    private bool cursorDrawn;

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

        // Skip when neither the backbuffer nor the cursor state changed.
        bool cursorChanged = cursorX != lastCursorX || cursorY != lastCursorY
            || currentCursor != lastCursorType
            || isStarting != lastStarting
            || (isStarting && cursorBlink != lastCursorBlink);

        if (!firstPresent && !contentChanged && !cursorChanged)
            return;

        lastCursorX = cursorX;
        lastCursorY = cursorY;
        lastCursorType = currentCursor;
        lastStarting = isStarting;
        lastCursorBlink = cursorBlink;

        long uploadStartedAt = PerformanceMetrics.Now;

        if (firstPresent || contentChanged)
        {
            firstPresent = false;
            canvas.DrawCanvas(BackBuffer, 0, 0);
        }
        else if (cursorDrawn && lastCursorRect.Width > 0 && lastCursorRect.Height > 0)
        {
            // Sub-rect background restore: restore only the area previously covered by the cursor
            // from the pristine BackBuffer, avoiding copying the entire 1920x1080 frame buffer.
            RestoreCursorBackground(lastCursorRect);
        }

        PerformanceMetrics.UploadTicks = PerformanceMetrics.Now - uploadStartedAt;

        long overlayStartedAt = PerformanceMetrics.Now;
        DrawCursor(cursorX, cursorY, cursorBlink);
        PerformanceMetrics.OverlayTicks = PerformanceMetrics.Now - overlayStartedAt;

        long displayStartedAt = PerformanceMetrics.Now;
        canvas.Display();
        PerformanceMetrics.DisplayTicks = PerformanceMetrics.Now - displayStartedAt;
    }

    private void RestoreCursorBackground(Rectangle rect)
    {
        int screenW = canvas.Width;
        int screenH = canvas.Height;
        int clipX1 = Math.Max(0, rect.X);
        int clipY1 = Math.Max(0, rect.Y);
        int clipX2 = Math.Min(screenW, rect.Right);
        int clipY2 = Math.Min(screenH, rect.Bottom);
        int copyW = clipX2 - clipX1;
        int copyH = clipY2 - clipY1;

        if (copyW <= 0 || copyH <= 0) return;

        int[] cBuf = canvas.GetBuffer();
        int[] bBuf = BackBuffer.Buffer;
        if (cBuf == null || bBuf == null) return;

        for (int y = clipY1; y < clipY2; y++)
        {
            int offset = y * screenW + clipX1;
            Array.Copy(bBuf, offset, cBuf, offset, copyW);
        }
    }

    private void DrawCursor(int x, int y, bool isHiddenByBlink)
    {
        if (isHiddenByBlink)
        {
            cursorDrawn = false;
            lastCursorRect = Rectangle.Empty;
            return;
        }

        CursorType cursorType = CursorManager.Current;
        var (hotspotX, hotspotY) = CursorManager.GetHotspot(cursorType);
        Image img = CursorManager.GetCursorImage(cursorType);
        int drawX = x - hotspotX;
        int drawY = y - hotspotY;

        canvas.DrawImage(img, drawX, drawY);
        lastCursorRect = new Rectangle(drawX, drawY, (int)img.Width, (int)img.Height);
        cursorDrawn = true;
    }
}
