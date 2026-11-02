using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using Cosmos.Kernel.System.Network;
using Windose.Programs.Breeze;
using Windose.System.Elf;
using Windose.System.Features;
using Windose.System.Kernel;
using Windose.System.Kernel.FileSystem;
using Windose.System.Kernel.Subsystem;
using Windose.System.Scheduling;
using Windose.System.System_Calls;
using Sys = Cosmos.Kernel.System;
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

    public static void RhDebugBreak()
    {
    }

    protected override void OnBoot()
    {
        base.OnBoot();
        //SchedulerManager.SetScheduler(new MlfqScheduler());
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
        WindoseBridge.Install();

        new Thread(() =>
        {
            try
            {
                DhcpClient dhcpClient = new();
                if (dhcpClient.SendDiscoverPacket() != -1)
                {
                    IPConfig? config = NetworkManager.Primary.IPConfig;
                    if (config != null)
                        SystemLogger.WriteLine("DHCP", $"Configured IP: {config.Address}", ConsoleMessageType.Log);
                }
            }
            catch (Exception ex)
            {
                SystemLogger.WriteLine("DHCP", $"DHCP configuration failed: {ex.Message}", ConsoleMessageType.Warning);
            }
        }).Start();

        Palette.Initialize();

        SystemLogger.WriteLine("FSM", "Starting", ConsoleMessageType.Log);
        FileSystemManager.Setup();

        bool safeMode = Environment.GetCommandLineArgs().Contains("safemode");
        if (safeMode)
        {
            SystemLogger.WriteLine("BOOT", "Safe mode enabled, using 800x600 resolution", ConsoleMessageType.Log);
            canvas = Canvas.GetFullScreen(new Mode(800, 600, ColorDepth.ColorDepth32));
        }
        else
        {
            canvas = Canvas.GetFullScreen(new Mode(1920, 1080, ColorDepth.ColorDepth32));
        }

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




        ProcessManager.Start(explorer);
        ProcessManager.Start(windowManager);
        ProcessManager.Start(new HotkeyManager());

        if (UserAccount.accounts.Count > 0)
            LockManager.Lock();



        Directory.CreateDirectory("/mnt/Programs");
        Directory.CreateDirectory("/mnt/Apps");
        Directory.CreateDirectory(SystemPaths.SystemLua);
        if (!File.Exists(SystemPaths.SystemLua + "/init.lua"))
            File.WriteAllText(SystemPaths.SystemLua + "/init.lua", "terminal.write('^blueHello From Lua!')\nterminal.write('Running From: ' .. terminal.currentDirectory())\n ");
        if (!File.Exists("/mnt/Programs/ControlTest.breeze"))
            File.WriteAllText("/mnt/Programs/ControlTest.breeze", ControlTest.data);
        if (!File.Exists("/mnt/System/Lua/demo.lua"))
            File.WriteAllText("/mnt/System/Lua/demo.lua", "windose.open(\"explorer\")");

        BreezeCapabilityPolicy.Grant("/mnt/Apps/main.breeze", "service.control");


        HotkeyManager.RegisterHotkey(new KeyEvent { Key = Key.Tab, Modifiers = ConsoleModifiers.Alt }, TaskSwitcher.Cycle);
        HotkeyManager.RegisterHotkey(new KeyEvent { Key = Key.LeftArrow, Modifiers = ConsoleModifiers.Alt }, () => WindowManager.SnapFocused(0));
        HotkeyManager.RegisterHotkey(new KeyEvent { Key = Key.RightArrow, Modifiers = ConsoleModifiers.Alt }, () => WindowManager.SnapFocused(1));
        HotkeyManager.RegisterHotkey(new KeyEvent { Key = Key.UpArrow, Modifiers = ConsoleModifiers.Alt }, () => WindowManager.SnapFocused(2));
        HotkeyManager.RegisterHotkey(new KeyEvent { Key = Key.D, Modifiers = ConsoleModifiers.Control | ConsoleModifiers.Alt }, WindowManager.ToggleShowDesktop);
        HotkeyManager.RegisterHotkey(new KeyEvent { Key = Key.L, Modifiers = ConsoleModifiers.Control | ConsoleModifiers.Alt }, LockManager.Lock);
        HotkeyManager.RegisterHotkey(new KeyEvent { Key = Key.LeftArrow, Modifiers = ConsoleModifiers.Control | ConsoleModifiers.Alt }, WindowManager.PrevDesktop);
        HotkeyManager.RegisterHotkey(new KeyEvent { Key = Key.RightArrow, Modifiers = ConsoleModifiers.Control | ConsoleModifiers.Alt }, WindowManager.NextDesktop);
        HotkeyManager.RegisterHotkey(new KeyEvent { Key = Key.R, Modifiers = ConsoleModifiers.Alt }, () =>
        {
            // Never block here: this runs on the window manager's input thread.
            // Spawn starts the program and returns; Run would wait for it to exit and freeze the desktop.
            string error;
            ElfProgram program = ElfRunner.Spawn(File.ReadAllBytes("/mnt/Programs/run.elf"), "Run", new string[] { "run" }, out error);
            if (program == null)
                SystemLogger.WriteLine("Hotkey", "Could not start Run: " + error, ConsoleMessageType.Error);
        });
        HotkeyManager.RegisterHotkey(new KeyEvent { Key = Key.E, Modifiers = ConsoleModifiers.Alt }, () => WindowManager.PostRegister(new FileExplorer(100, 100, 800, 500, "File Explorer")));
        HotkeyManager.RegisterHotkey(new KeyEvent { Key = Key.Escape, Modifiers = ConsoleModifiers.Control | ConsoleModifiers.Shift }, () => WindowManager.PostRegister(new PerformanceMonitor(100, 100)));

        SystemLogger.WriteLine("Kernel", "Boot completed successfully", ConsoleMessageType.Log);
        FileAssociations.DiscoverPrograms(typeof(Kernel).Assembly);


        ResourceLoader.SysRequired();
    }


    public static double DeltaTimeMs;
    public static double DeltaTimeSeconds;
    public static int Fps;
    public static long FrameCount;
    private int lastFrameTick;
    private int fpsWindowTick;
    private int fpsWindowFrames;

    protected override void Run()
    {
        try
        {
            if (!MemoryCheck.Pass()) return;

            int nowTick = Environment.TickCount;
            if (FrameCount > 0)
            {
                DeltaTimeMs = Math.Max(0, unchecked(nowTick - lastFrameTick));
                DeltaTimeSeconds = DeltaTimeMs / 1000.0;
            }
            lastFrameTick = nowTick;
            FrameCount++;

            fpsWindowFrames++;
            if (unchecked(nowTick - fpsWindowTick) >= 1000)
            {
                Fps = fpsWindowFrames;
                fpsWindowFrames = 0;
                fpsWindowTick = nowTick;
            }

            Mouse.Update();

            PerformanceMetrics.BeginFrame();

            long processStartedAt = PerformanceMetrics.Now;

            ProcessManager.Update();

            PerformanceMetrics.ProcessTicks = PerformanceMetrics.Now - processStartedAt;

            displayDriver.Present(MouseManager.X, MouseManager.Y, WindowManager.TakeFrameComposed());
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

    }

}
