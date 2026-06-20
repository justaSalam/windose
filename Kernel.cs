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

    public static Color Gray = Color.FromArgb(123, 126, 121);
    public static Color Blue = Color.FromArgb(0, 0, 128);

    private const int gcrate = 1800;
    public static Kernel Instance = null!;
    public static DirectBitmap mainBuffer;
    public static Canvas canvas = null!;
    private DirectBitmap performanceOverlay;

    private WindowManager windowManager = null!;
    int tick;


    protected override void BeforeRun()
    {
        Console.WriteLine("Booting Windose");

        Instance = this;
        GarbageCollector.Initialize();


        canvas = Canvas.GetFullScreen();
        mainBuffer = new DirectBitmap(canvas.Width, canvas.Height);
        performanceOverlay = new DirectBitmap(Math.Max(1, Math.Min(800, canvas.Width - 20)), 52);

        MouseManager.Initialize();
        MouseManager.SetScreenSize(canvas.Width, canvas.Height);
        Global.screenHeight = canvas.Height;
        Global.screenWidth = canvas.Width;

        Console.WriteLine("Windose booted successfully");

        Explorer explorer = new Explorer(canvas);
        windowManager = new WindowManager();



        ProcessManger.Start(explorer);
        ProcessManger.Start(windowManager);


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

            PerformanceMetrics.BeginFrame();
            long processStartedAt = PerformanceMetrics.Now;
            ProcessManger.Update();
            PerformanceMetrics.ProcessTicks = PerformanceMetrics.Now - processStartedAt;

            long uploadStartedAt = PerformanceMetrics.Now;
            canvas.DrawArray(mainBuffer.GetBuffer(), 0, 0, canvas.Width, canvas.Height);
            PerformanceMetrics.UploadTicks = PerformanceMetrics.Now - uploadStartedAt;

            long overlayStartedAt = PerformanceMetrics.Now;

            canvas.DrawFilledCircle(Color.Black, MouseManager.X, MouseManager.Y, 3);
            canvas.DrawFilledCircle(Color.White, MouseManager.X, MouseManager.Y, 2);
            PerformanceMetrics.OverlayTicks = PerformanceMetrics.Now - overlayStartedAt;

            long displayStartedAt = PerformanceMetrics.Now;
            canvas.Display();
            PerformanceMetrics.DisplayTicks = PerformanceMetrics.Now - displayStartedAt;


            Tick();
            if (tick > 0 && tick % gcrate == 0) GarbageCollector.Collect();


            tick++;
        }
        catch (Exception ex)
        {
            string message = "Kernel frame error: " + ex.Message;
            Serial.WriteString($"{message}\n");
            Console.WriteLine(message);
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

}
