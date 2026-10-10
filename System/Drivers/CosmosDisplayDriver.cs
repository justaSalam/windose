using System.Drawing;

using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Timers;
using Windose.System.Kernel;

namespace Windose.Drivers;

public sealed class CosmosDisplayDriver : IWindoseDriver
{
    public string Name => "Cosmos GOP Display";
    public WindoseDriverState State { get; private set; } = WindoseDriverState.Created;

    public Canvas canvas { get; private set; }
    public DirectBitmap BackBuffer { get; private set; }
    public int Width => canvas == null ? 0 : canvas.Width;
    public int Height => canvas == null ? 0 : canvas.Height;



    public void Start()
    {
        canvas = Kernel.canvas;


        int width = (int)Registry.GetInteger("System/Display/Width", 1920);
        int height = (int)Registry.GetInteger("System/Display/Height", 1080);
        int depth = (int)Registry.GetInteger("System/Display/BitsPerPixel", 32);

        BackBuffer = new DirectBitmap(canvas.Width, canvas.Height);
        State = WindoseDriverState.Started;

        TimerManager.ScheduleRecurring(() =>
        {
            isCursorVisible = !isCursorVisible;
        }, TimeSpan.FromMilliseconds(83));
    }

    public void Present(int cursorX, int cursorY)
    {
        if (State != WindoseDriverState.Started || canvas == null) return;
        long uploadStartedAt = PerformanceMetrics.Now;
        canvas.DrawCanvas(BackBuffer, 0, 0);
        canvas.DrawString($"Util.: {Kernel.utilization:F3}%", SystemFonts.msSansSerif, Color.White, 10, 10);

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


    //console shares the same canvas, don't disable
    public void Stop()
    {
        State = WindoseDriverState.Stopped;
    }

    private bool isCursorVisible = true;

    private void DrawCursor(int x, int y)
    {
        if (!isCursorVisible && LaunchTracker.isStarting) return;
        canvas.DrawImage(Cursors.arrow, x, y);
    }

}
