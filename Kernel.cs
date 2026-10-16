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
using Windose.System.Elf;
using Windose.System.Kernel;
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
        WindoseBridge.Install();

        
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

        if (UserAccount.accounts.Count > 0)
            LockManager.Lock();



        Directory.CreateDirectory("/mnt/Programs");
        Directory.CreateDirectory("/mnt/Apps");
        Directory.CreateDirectory(SystemPaths.SystemLua);
        File.WriteAllText(SystemPaths.SystemLua + "/init.lua", "terminal.write('^blueHello From Lua!')\nterminal.write('Running From: ' .. terminal.currentDirectory())\n ");
        File.WriteAllText("/mnt/Programs/ControlTest.breeze", ControlTest.data);
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

        ResourceLoader.WriteStorage("/mnt/Programs/window.elf", "Windose.System.ELF.programs.bin.window.elf", true);
        ResourceLoader.WriteStorage("/mnt/Programs/hello.elf", "Windose.System.ELF.programs.bin.hello.elf", true);
        ResourceLoader.WriteStorage("/mnt/Programs/canvas.elf", "Windose.System.ELF.programs.bin.canvas.elf", true);
        ResourceLoader.WriteStorage("/mnt/Programs/explorer.elf", "Windose.System.ELF.programs.bin.explorer.elf", true);
        ResourceLoader.WriteStorage("/mnt/Programs/search.elf", "Windose.System.ELF.programs.bin.search.elf", true);
        ResourceLoader.WriteStorage("/mnt/Programs/run.elf", "Windose.System.ELF.programs.bin.run.elf", true);
        ResourceLoader.WriteStorage("/mnt/Programs/pager.elf", "Windose.System.ELF.programs.bin.pager.elf", true);
        ResourceLoader.WriteStorage("/mnt/Programs/widgets.elf", "Windose.System.ELF.programs.bin.widgets.elf", true);
        ResourceLoader.WriteStorage("/mnt/Programs/libctest.elf", "Windose.System.ELF.programs.bin.libctest.elf", true);
        ResourceLoader.WriteStorage("/mnt/Programs/memhog.elf", "Windose.System.ELF.programs.bin.memhog.elf", true);
        ResourceLoader.WriteStorage("/mnt/Programs/spin.elf", "Windose.System.ELF.programs.bin.spin.elf", true);

        ResourceLoader.WriteStorage("/mnt/Programs/ctype.h", "Windose.System.ELF.programs.include.ctype.h", true);
        ResourceLoader.WriteStorage("/mnt/Programs/kernel.h", "Windose.System.ELF.programs.include.kernel.h", true);
        ResourceLoader.WriteStorage("/mnt/Programs/stdio.h", "Windose.System.ELF.programs.include.stdio.h", true);
        ResourceLoader.WriteStorage("/mnt/Programs/stdlib.h", "Windose.System.ELF.programs.include.stdlib.h", true);
        ResourceLoader.WriteStorage("/mnt/Programs/string.h", "Windose.System.ELF.programs.include.string.h", true);

        ResourceLoader.WriteStorage("/mnt/Programs/crt0.c", "Windose.System.ELF.programs.libc.crt0.c", true);
        ResourceLoader.WriteStorage("/mnt/Programs/format.c", "Windose.System.ELF.programs.libc.format.c", true);
        ResourceLoader.WriteStorage("/mnt/Programs/malloc.c", "Windose.System.ELF.programs.libc.malloc.c", true);
        ResourceLoader.WriteStorage("/mnt/Programs/stdio.c", "Windose.System.ELF.programs.libc.stdio.c", true);
        ResourceLoader.WriteStorage("/mnt/Programs/stdlib.c", "Windose.System.ELF.programs.libc.stdlib.c", true);
        ResourceLoader.WriteStorage("/mnt/Programs/string.c", "Windose.System.ELF.programs.libc.string.c", true);


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

            ProcessManger.Update();

            PerformanceMetrics.ProcessTicks = PerformanceMetrics.Now - processStartedAt;

            displayDriver.Present(MouseManager.X, MouseManager.Y, WindowManager.TakeFrameComposed());
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

    }

}
