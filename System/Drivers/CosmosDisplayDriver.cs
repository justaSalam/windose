using System;
using System.Drawing;
using Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;
using Cosmos.Kernel.HAL.Pci;
using Cosmos.Kernel.HAL.Pci.Enums;
using Cosmos.Kernel.System.Graphics;

namespace Windose.Drivers;

public sealed class CosmosDisplayDriver : IWindoseDriver
{
    public string Name => "Cosmos GOP Display";
    public WindoseDriverState State { get; private set; } = WindoseDriverState.Created;

    public SVGAII3DCanvas Canvas { get; private set; }
    public DirectBitmap BackBuffer { get; private set; }
    public DirectBitmap PerformanceOverlay { get; private set; }
    public int Width => Canvas == null ? 0 : Canvas.Width;
    public int Height => Canvas == null ? 0 : Canvas.Height;

    public void Start()
    {
        Canvas = new SVGAII3DCanvas(PciManager.GetDevice(VendorId.VmWare, DeviceId.SvgaiiAdapter), new Mode(1920, 1080, ColorDepth.ColorDepth32));

        if (Canvas.HasHardwareCursor)
        {
            try
            {
                Canvas.CreateCursor();
                //Canvas.DefineAlphaCursor(0, 0, (int)Cursors.arrow.Width, (int)Cursors.arrow.Height, Cursors.arrow.RawData);
            }
            catch (Exception ex)
            {
                Console.BackgroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
            }
        }

        BackBuffer = new DirectBitmap(Canvas.Width, Canvas.Height);
        PerformanceOverlay = new DirectBitmap(Math.Max(1, Math.Min(800, Canvas.Width - 20)), 52);
        State = WindoseDriverState.Started;
    }

    public void Present(DirectBitmap frame, int cursorX, int cursorY)
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

        Canvas.SetCursor(true, x, y);

    }
}
