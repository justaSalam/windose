using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Network.Config;
using Cosmos.Kernel.System.Network.IPv4.UDP.DHCP;
using System.Drawing;
using Windose.Drivers;
using Windose.Installer;
using Windose.Programs.Breeze;
using Windose.System.Features;
using Windose.System.System_Calls;
using Sys = Cosmos.Kernel.System;


namespace Windose;

/// <summary>
/// Main kernel class - inherits from Cosmos.Kernel.System.Kernel.
/// </summary>
public class Kernel : Sys.Kernel
{
    public static Color Gray = Color.FromArgb(123, 126, 121);
    public static Color Blue = Color.FromArgb(0, 0, 128);

    public static Kernel Instance = null!;
    public static DirectBitmap mainBuffer;
    public static SVGAII3DCanvas canvas;
    
    
    

    private WindowManager windowManager = null!;
    public CosmosDisplayDriver displayDriver = null!;
    private CosmosMouseDriver mouseDriver = null!;
    int tick;


    protected override void BeforeRun()
    {
        
        KernelConsole.Default.Font = SystemFonts.lucida;
        KernelPanic.Install();
        try
        {
            InitializeKernel();
        }
        catch (Exception exception)
        {
            KernelPanic.Show("KERNEL_INITIALIZATION_FAILURE", exception);
        }
    }

    private async void InitializeKernel()
    {
        Instance = this;
        GarbageCollector.Initialize();
        Palette.Initialize();


        //TODO: 
        //An acutall setup should run before getting to the main os
        //main partition, copy files, general setup
        Setup.Run();

        displayDriver = new CosmosDisplayDriver();

        DriverManager.Register(displayDriver);
        DriverManager.Start(displayDriver);

        canvas = displayDriver.Canvas;
        mainBuffer = displayDriver.BackBuffer;



        mouseDriver = new CosmosMouseDriver(displayDriver.Width, displayDriver.Height);
        DriverManager.Register(mouseDriver);
        DriverManager.StartAll();

        Global.screenHeight = displayDriver.Height;
        Global.screenWidth = displayDriver.Width;
        SystemRegistry.SetRuntimeValue("System/Display/CurrentWidth", (long)displayDriver.Width);
        SystemRegistry.SetRuntimeValue("System/Display/CurrentHeight", (long)displayDriver.Height);

        SystemLogger.WriteLine("Kernel", "Boot completed successfully", ConsoleMessageType.Log);

        Background.Load();
        Explorer explorer = new Explorer(canvas);
        windowManager = new WindowManager();



        ProcessManger.Start(explorer);
        ProcessManger.Start(windowManager);

        Directory.CreateDirectory("/mnt/Programs");
        File.WriteAllText("/mnt/Programs/ControlTest.breeze", ControlTest.data);

    }

    private long lastFrameTicks;
    public static double DeltaTimeMs;
    public static double DeltaTimeSeconds;
    public static int Fps;
    protected override void Run()
    {
        try
        {
            mouseDriver.Update();

            Tick();

            PerformanceMetrics.BeginFrame();
            long processStartedAt = PerformanceMetrics.Now;
            ProcessManger.Update();
            PerformanceMetrics.ProcessTicks = PerformanceMetrics.Now - processStartedAt;

            displayDriver.Present(mainBuffer, mouseDriver.X, mouseDriver.Y);

            tick++;

        }
        catch (Exception ex)
        {
            KernelPanic.Show("KERNEL_FRAME_FAILURE", ex);
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
