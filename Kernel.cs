using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Mouse;
using System.Drawing;
using Wacs.Core;
using Wacs.Core.Runtime;
using Windose.Drivers;
using Windose.Installer;
using Windose.Programs.Breeze;
using Windose.System.ABI.WIN;
using Windose.System.Kernel.FileSystem;
using Windose.System.System_Calls;
using Windose.System.WASM;
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
            SystemLogger.WriteLine("BOOT", "BeforeRun starting", ConsoleMessageType.Log);

            InitializeKernel();

        }
        catch (Exception exception)
        {
            SystemLogger.WriteLine("BOOT", "Error occurred while initializing kernel, " + exception.Message, ConsoleMessageType.Error);
            Log.WriteString("KERNEL FAILED TO INIT\n");
        }
    }
    private WasmRuntime runtime;
    private void InitializeKernel()
    {
        Instance = this;

        Palette.Initialize();

        SystemLogger.WriteLine("BOOT", "FileSystemManager.Setup() starting", ConsoleMessageType.Log);
        FileSystemManager.Setup();
        
        runtime = new WasmRuntime();
        WinHost host = new WinHost(runtime);
        host.Register();
        SystemLogger.WriteLine("WASM", "Runtime initialized", ConsoleMessageType.Log, true);


        SystemLogger.WriteLine("BOOT", "Canvas starting", ConsoleMessageType.Log);

        canvas = Canvas.GetFullScreen(new Mode(1920, 1080, ColorDepth.ColorDepth32));

        Console.WriteLine("Canvas:     " + canvas.Name);
        Console.WriteLine("Resolution: " + canvas.Width + "x" + canvas.Height);
        Console.WriteLine("Refresh:    " + canvas.RefreshRate + " Hz");


        MouseManager.SetScreenSize(canvas.Width, canvas.Height);


        displayDriver = new CosmosDisplayDriver();
        DriverManager.Register(displayDriver);
        DriverManager.StartAll();
        mainBuffer = displayDriver.BackBuffer;

        Global.screenHeight = canvas.Height;
        Global.screenWidth = canvas.Width;
        Registry.SetRuntimeValue("System/Display/CurrentWidth", (long)canvas.Width);
        Registry.SetRuntimeValue("System/Display/CurrentHeight", (long)canvas.Height);


        Explorer explorer = new Explorer(canvas);
        windowManager = new WindowManager();




        ProcessManger.Start(explorer);
        ProcessManger.Start(windowManager);
        ProcessManger.Start(new HotkeyManager());


        
        Directory.CreateDirectory("/mnt/Programs");
        Directory.CreateDirectory("/mnt/Apps");
        File.WriteAllText("/mnt/Programs/ControlTest.breeze", ControlTest.data);

        BreezeCapabilityPolicy.Grant("/mnt/Apps/main.breeze", "service.control");


        HotkeyManager.RegisterHotkey(new KeyEvent { Key = ConsoleKeyEx.Tab, Modifiers = ConsoleModifiers.Alt }, WindowManager.SwapFocusedWindow);
        HotkeyManager.RegisterHotkey(new KeyEvent { Key = ConsoleKeyEx.E & ConsoleKeyEx.LWin }, () => WindowManager.PostRegister(new FileExplorer(100, 100, 800, 500, "File Explorer")));
        HotkeyManager.RegisterHotkey(new KeyEvent { Key = ConsoleKeyEx.Escape, Modifiers = ConsoleModifiers.Control | ConsoleModifiers.Shift }, () => WindowManager.PostRegister(new PerformanceMonitor(100, 100)));

        SystemLogger.WriteLine("Kernel", "Boot completed successfully", ConsoleMessageType.Log);
        

        if (IO.TryLoadFile("/mnt/Programs/win.wasm", out byte[] program))
        {
            //using var stream = new MemoryStream(program);

            //Module module = BinaryModuleParser.ParseWasm(stream);

            //var instance = runtime.InstantiateModule(module);

            //runtime.RegisterModule("app", instance);

            //if (runtime.TryGetExportedFunction(("app", "main"), out var mainAddr))
            //{
                //Func<Value> main = runtime.CreateInvokerFunc<Value>(mainAddr);
                //main();
               
            //}
        }

    }

    private long lastFrameTicks;
    public static double DeltaTimeMs;
    public static double DeltaTimeSeconds;
    public static int Fps;


    public static ulong startWall;
    public static ulong endWall;
    public static ulong wallDelta;

    public static ulong startBusy;
    public static ulong endBusy;
    public static ulong busyDelta;

    public static double utilization;
    protected override void Run()
    {
        try
        {
            startWall = Clock.Nanoseconds;
            startBusy = SchedulerInfo.BusyCpuTimeNs;

            Mouse.Update();
            System.Drivers.Keyboard.BeginFrame();
            Tick();

            PerformanceMetrics.BeginFrame();

            long processStartedAt = PerformanceMetrics.Now;

            ProcessManger.Update();

            PerformanceMetrics.ProcessTicks = PerformanceMetrics.Now - processStartedAt;

            displayDriver.Present(MouseManager.X, MouseManager.Y);
            canvas.DrawString($"Util.: {utilization}%", SystemFonts.msSansSerif, Color.Black, 10, 10);

            endWall = Clock.Nanoseconds;
            endBusy = SchedulerInfo.BusyCpuTimeNs;

            wallDelta = endWall - startWall;
            busyDelta = endBusy - startBusy;

            utilization = (double)busyDelta / (wallDelta * SchedulerInfo.CpuCount) * 100;




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
