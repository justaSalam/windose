using System.Drawing;

using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Timers;
using Windose.Compositing;
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

        TimerManager.ScheduleRecurring(() =>
        {
            isCursorVisible = !isCursorVisible;
        }, TimeSpan.FromMilliseconds(83));
    }


    public void Present(int cursorX, int cursorY)
    {
        long uploadStartedAt = PerformanceMetrics.Now;
        canvas.DrawCanvas(BackBuffer, 0, 0);

        PerformanceMetrics.UploadTicks = PerformanceMetrics.Now - uploadStartedAt;

        long overlayStartedAt = PerformanceMetrics.Now;
        DrawCursor(cursorX, cursorY);
        canvas.DrawString($"Starting Count: {LaunchTracker.startingCount}", SystemFonts.msSansSerif, Color.White, 10, 30);
        canvas.DrawString($"Is starting: {LaunchTracker.isStarting}", SystemFonts.msSansSerif, Color.White, 10, 50);
        PerformanceMetrics.OverlayTicks = PerformanceMetrics.Now - overlayStartedAt;



        long displayStartedAt = PerformanceMetrics.Now;
        canvas.Display();

        PerformanceMetrics.DisplayTicks = PerformanceMetrics.Now - displayStartedAt;
    }

    private bool isCursorVisible = true;

    private void DrawCursor(int x, int y)
    {
        if (!isCursorVisible && LaunchTracker.isStarting) return;
        canvas.DrawImage(Cursors.arrow, x, y);
    }

}
