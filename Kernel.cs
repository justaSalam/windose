using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Graphics;
using Windose.Programs.Breeze;
using Windose.System.Kernel.Subsystem;
using Windose.System.System_Calls;
using Sys = Cosmos.Kernel.System;

///TODO Implement lua scripts instead of hardcoded programs for terminal;
using Cosmos.Executable.Lua;
using Windose.System.Kernel.FileSystem;
using Cosmos.Kernel.Core.Scheduler;
using Windose.System.Scheduling;
using Cosmos.Kernel.System.Input;
using Windose.Compositing;
namespace Windose;

/// <summary>
/// Main kernel class - inherits from Cosmos.Kernel.System.Kernel.
/// </summary>
public class Kernel : Sys.Kernel
{
    public static Kernel Instance = null!;
    public static ComponentBuffer mainBuffer;
    public static Canvas canvas;

    private WindowManager windowManager;
    public Drivers.DisplayManager displayDriver;

    protected override void OnBoot()
    {
        base.OnBoot();
        SchedulerManager.SetScheduler(new MlfqScheduler());
    }

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
    private void InitializeKernel()
    {
        Instance = this;

        Palette.Initialize();

        SystemLogger.WriteLine("FSM", "Starting", ConsoleMessageType.Log);
        FileSystemManager.Setup();

        canvas = Canvas.GetFullScreen(new Mode(1920, 1080, ColorDepth.ColorDepth32));

        displayDriver = new Drivers.DisplayManager();

        Console.WriteLine("Canvas:     " + canvas.Name);
        Console.WriteLine("Resolution: " + canvas.Width + "x" + canvas.Height);
        Console.WriteLine("Refresh:    " + canvas.RefreshRate + " Hz");


        MouseManager.SetScreenSize(canvas.Width, canvas.Height);

        mainBuffer = displayDriver.BackBuffer;

        CompositorSelfTests.Run();

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
        Directory.CreateDirectory(SystemPaths.SystemLua);
        File.WriteAllText(SystemPaths.SystemLua + "/init.lua", "terminal.write('^blueHello From Lua!')\nterminal.write('Running From: ' .. terminal.currentDirectory())\n ");
        File.WriteAllText("/mnt/Programs/ControlTest.breeze", ControlTest.data);
        File.WriteAllText("/mnt/System/Lua/demo.lua", "windose.open(\"explorer\")");

        BreezeCapabilityPolicy.Grant("/mnt/Apps/main.breeze", "service.control");


        HotkeyManager.RegisterHotkey(new KeyEvent { Key = Key.Tab, Modifiers = ConsoleModifiers.Alt }, WindowManager.SwapFocusedWindow);
        HotkeyManager.RegisterHotkey(new KeyEvent { Key = Key.E & Key.LWin }, () => WindowManager.PostRegister(new FileExplorer(100, 100, 800, 500, "File Explorer")));
        HotkeyManager.RegisterHotkey(new KeyEvent { Key = Key.Escape, Modifiers = ConsoleModifiers.Control | ConsoleModifiers.Shift }, () => WindowManager.PostRegister(new PerformanceMonitor(100, 100)));

        SystemLogger.WriteLine("Kernel", "Boot completed successfully", ConsoleMessageType.Log);
        FileAssociations.DiscoverPrograms(typeof(Kernel).Assembly);

    }

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

            displayDriver.Present(MouseManager.X, MouseManager.Y);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

    }

}
