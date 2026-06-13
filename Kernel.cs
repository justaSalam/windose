using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.Core.Memory.Heap;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Mouse;
using Sys = Cosmos.Kernel.System;


namespace Windose;

/// <summary>
/// Main kernel class - inherits from Cosmos.Kernel.System.Kernel.
/// </summary>
public class Kernel : Sys.Kernel
{
    private const int gcrate = 150;
    public static Kernel Instance = null!;
    public static DirectBitmap mainBuffer;
    public static Canvas canvas = null!;

    private WindowManager windowManager = null!;
    private const string versionString = $"Windose NativeAOT {VersionString}";
    int tick;

    protected override void BeforeRun()
    {
        Instance = this;
        GarbageCollector.Initialize();
        Console.WriteLine("Cosmos booted successfully!");

        canvas = Canvas.GetFullScreen();
        mainBuffer = new DirectBitmap(canvas.Width, canvas.Height);

        MouseManager.Initialize();
        MouseManager.SetScreenSize(canvas.Width, canvas.Height);
        Global.screenHeight = canvas.Height;
        Global.screenWidth = canvas.Width;

        Explorer explorer = new Explorer(canvas);
        windowManager = new WindowManager();



        ProcessManger.Start(explorer);
        ProcessManger.Start(windowManager);

        windowManager.Register(new Window(100, 100, 250, 250, "Test Window Component", true));
        //windowManager.Register(new Window(350, 200, 200, 200, "Window 2", true));
    }

    private long lastFrameTicks;
    public static double DeltaTimeMs;
    public static double DeltaTimeSeconds;
    public static int Fps;
    protected override void Run()
    {
        try
        {
            Mouse.Update();


            ProcessManger.Update();



            canvas.DrawArray(mainBuffer.GetBuffer(), 0, 0, canvas.Width, canvas.Height);
            canvas.DrawString(versionString, PCScreenFont.DefaultFont, Color.White, 10, 10);
            canvas.DrawString($"Frametime: {DeltaTimeMs}ms | FPS: {Fps}", PCScreenFont.DefaultFont, Color.White, 10, 45);

            canvas.DrawFilledCircle(Color.White, MouseManager.X, MouseManager.Y, 2);
            canvas.Display();


            Tick();
            if (tick % gcrate == 0) GarbageCollector.Collect();


            tick++;
        }
        catch (Exception ex)
        {
            canvas.Disable();
            Console.WriteLine(ex.Message);
        }

    }

    private void Tick()
    {
        long now = DateTime.UtcNow.Ticks;

        if (lastFrameTicks != 0)
        {
            long deltaTicks = now - lastFrameTicks;

            DeltaTimeMs = deltaTicks / 10000.0;
            DeltaTimeSeconds = deltaTicks / 10_000_000.0;

            if (DeltaTimeSeconds > 0)
                Fps = (int)(1.0 / DeltaTimeSeconds);
        }

        lastFrameTicks = now;
    }


    private int collections, freed, timeInGC;
    private ulong heap;
    private void GCINFO()
    {
        GarbageCollector.GetStats(out collections, out freed);
        heap = GarbageCollector.GetHeapSizeBytes();
        timeInGC = GarbageCollector.GetLastGCPercentTimeInGC();
    }
}
