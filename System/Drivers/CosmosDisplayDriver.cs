using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace Windose.Drivers;

public sealed class CosmosDisplayDriver : IWindoseDriver
{
    public string Name => "Cosmos GOP Display";
    public WindoseDriverState State { get; private set; } = WindoseDriverState.Created;

    public Canvas Canvas { get; private set; }
    public global::DirectBitmap BackBuffer { get; private set; }
    public global::DirectBitmap PerformanceOverlay { get; private set; }
    public int Width => Canvas == null ? 0 : Canvas.Width;
    public int Height => Canvas == null ? 0 : Canvas.Height;

    public void Start()
    {
        Canvas = Cosmos.Kernel.System.Graphics.Canvas.GetFullScreen();
        BackBuffer = new global::DirectBitmap(Canvas.Width, Canvas.Height);
        PerformanceOverlay = new global::DirectBitmap(Math.Max(1, Math.Min(800, Canvas.Width - 20)), 52);
        State = WindoseDriverState.Started;
    }

    public void Present(global::DirectBitmap frame, int cursorX, int cursorY)
    {
        if (State != WindoseDriverState.Started || Canvas == null || frame == null) return;

        long uploadStartedAt = PerformanceMetrics.Now;
        Canvas.DrawArray(frame.GetBuffer(), 0, 0, Canvas.Width, Canvas.Height);
        PerformanceMetrics.UploadTicks = PerformanceMetrics.Now - uploadStartedAt;

        long overlayStartedAt = PerformanceMetrics.Now;
        DrawCursor(cursorX, cursorY);
        PerformanceMetrics.OverlayTicks = PerformanceMetrics.Now - overlayStartedAt;

        long displayStartedAt = PerformanceMetrics.Now;
        Canvas.Display();
        PerformanceMetrics.DisplayTicks = PerformanceMetrics.Now - displayStartedAt;
    }

    public void Stop()
    {
        State = WindoseDriverState.Stopped;
    }

    private void DrawCursor(int x, int y)
    {
        Canvas.DrawFilledCircle(Color.Black, x, y, 3);
        Canvas.DrawFilledCircle(Color.White, x, y, 2);
    }
}
