using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Mouse;
using System.Drawing;
using Windose.Drivers;
using Windose.Installer;
using Windose.Programs.Breeze;
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
    public static Canvas canvas;




    private WindowManager windowManager = null!;
    public CosmosDisplayDriver displayDriver = null!;
    protected override void BeforeRun()
    {

        try
        {
            InitializeKernel();
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception.Message);
            Log.WriteString("KERNEL FAILED TO INIT\n");
        }
    }

    private async void InitializeKernel()
    {
        Instance = this;
        Palette.Initialize();


        //TODO: 
        //main partition, copy files, general setup
        Setup.Run();


        canvas = Canvas.GetFullScreen();

        Console.WriteLine("Canvas:     " + canvas.Name);
        Console.WriteLine("Resolution: " + canvas.Width + "x" + canvas.Height);
        Console.WriteLine("Refresh:    " + canvas.RefreshRate + " Hz");


        MouseManager.SetScreenSize(canvas.Width, canvas.Height);

        DriverManager.StartAll();

        Global.screenHeight = canvas.Height;
        Global.screenWidth = canvas.Width;
        Registry.SetRuntimeValue("System/Display/CurrentWidth", (long)canvas.Width);
        Registry.SetRuntimeValue("System/Display/CurrentHeight", (long)canvas.Height);


        Explorer explorer = new Explorer(canvas);
        windowManager = new WindowManager();




        ProcessManger.Start(explorer);
        ProcessManger.Start(windowManager);
        ProcessManger.Start(new HotkeyManager());


        Log.WriteString("KERNEL INIT\n");
        Directory.CreateDirectory("/mnt/Programs");
        Directory.CreateDirectory("/mnt/Apps");
        File.WriteAllText("/mnt/Programs/ControlTest.breeze", ControlTest.data);

        BreezeCapabilityPolicy.Grant("/mnt/Apps/main.breeze", "service.control");


        HotkeyManager.RegisterHotkey(new KeyEvent { Key = ConsoleKeyEx.Tab, Modifiers = ConsoleModifiers.Alt }, WindowManager.SwapFocusedWindow);

        SystemLogger.WriteLine("Kernel", "Boot completed successfully", ConsoleMessageType.Log);

        //File.WriteAllBytes("/mnt/System/kbReadTest.bin", new byte[1024]);
        //File.WriteAllBytes("/mnt/System/mbReadTest.bin", new byte[1024 * 1024]);
    }

    private long lastFrameTicks;
    public static double DeltaTimeMs;
    public static double DeltaTimeSeconds;
    public static int Fps;
    protected override void Run()
    {
        try
        {
            canvas.Clear();
            System.Drivers.Keyboard.BeginFrame();
            Tick();

            PerformanceMetrics.BeginFrame();

            long processStartedAt = PerformanceMetrics.Now;

            ProcessManger.Update();

            PerformanceMetrics.ProcessTicks = PerformanceMetrics.Now - processStartedAt;

            displayDriver.Present(MouseManager.X, MouseManager.Y);
            canvas.Display();
        }
        catch (Exception ex)
        {
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
}
