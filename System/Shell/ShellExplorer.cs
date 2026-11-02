using System;
using Cosmos.Kernel.System.Graphics;
using Windose;
using Windose.System.Kernel;

public class Explorer : SingleThreadedProcess
{
    public static Taskbar taskbar;
    public static Desktop desktop;
    public static StartMenu startMenu;
    private Canvas canvas;
    public static Explorer? Instance { get; private set; }

    public Explorer(Canvas canvas) : base("Explorer", ProcessType.System)
    {
        this.canvas = canvas;
        Instance = this;
        canTerminate = true;
        startInfo.RestartFactory = () => new Explorer(this.canvas ?? Kernel.canvas);
    }

    private const int taskbarHeight = 36;
    public override void Start()
    {
        Instance = this;
        base.Start();
        Canvas activeCanvas = canvas ?? Kernel.canvas;
        desktop = new Desktop(0, 0, activeCanvas.Width, activeCanvas.Height);
        taskbar = new Taskbar(0, Math.Max(0, activeCanvas.Height - taskbarHeight), activeCanvas.Width, taskbarHeight);
        startMenu = new StartMenu(taskbar.X, taskbar.Y - 500, 360, 500, "Start Menu", false);
        LaunchTracker.Start(() => startMenu);
        WindowManager.RebuildTaskbarButtons();
    }

    public override void Update()
    {
        base.Update();
        desktop?.Update();
        taskbar?.Update();
    }

    public override void Dispose()
    {
        try
        {
            if (startMenu != null)
            {
                startMenu.Dispose();
                startMenu = null!;
            }
            if (taskbar != null)
            {
                taskbar.Dispose();
                taskbar = null!;
            }
            if (desktop != null)
            {
                desktop.Dispose();
                desktop = null!;
            }
        }
        catch { }
        base.Dispose();
    }

    public static void RestartShell()
    {
        if (Instance != null)
        {
            ProcessManager.QueueRestart(Instance, force: true);
        }
        else
        {
            Explorer replacement = new Explorer(Kernel.canvas);
            ProcessManager.QueueStart(replacement);
        }
    }
}
