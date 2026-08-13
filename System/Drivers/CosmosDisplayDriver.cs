using System;
using System.Drawing;
using Cosmos.Kernel;
using Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;
using Cosmos.Kernel.HAL.Pci;
using Cosmos.Kernel.HAL.Pci.Enums;
using Cosmos.Kernel.System.Graphics;
using Windose.System.System_Calls;

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
        PciDevice? device = PciManager.GetDevice(VendorId.VmWare, DeviceId.SvgaiiAdapter);

        if (device == null)
        {
            SystemLogger.WriteLine("Display Driver", "PCI device not found. Ensure that the SVGAII driver is loaded and the device is present.", ConsoleMessageType.Error);
            return;
        }
        uint width = (uint)SystemRegistry.GetInteger("System/Display/Width", 1920);
        uint height = (uint)SystemRegistry.GetInteger("System/Display/Height", 1080);

        Mode mode = new Mode(width, height, ColorDepth.ColorDepth32);

        Canvas = new SVGAII3DCanvas(device, mode);
        
        

        if (Canvas.HasHardwareCursor)
        {
            try
            {
                Canvas.DefineAlphaCursor(0, 0, (int)Cursors.arrow.Width, (int)Cursors.arrow.Height, Cursors.arrow.RawData);
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
        Canvas.Clear(Color.Black);

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
