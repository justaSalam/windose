using Cosmos.Kernel.System.Graphics;
using Windose;


public class Explorer : SingleThreadedProcess
{
    public static Taskbar taskbar;
    public static Desktop desktop;
    public static StartMenu startMenu;
    private Canvas canvas;

    public Explorer(Canvas canvas) : base("Explorer", ProcessType.System)
    {
        this.canvas = canvas;
    }

    public override void Start()
    {
        base.Start();
        desktop = new Desktop(0, 0, Kernel.canvas.Width, Kernel.canvas.Height);
        taskbar = new Taskbar(0, Kernel.canvas.Height - 20, canvas.Width, 20);
        startMenu = new StartMenu(taskbar.X, taskbar.Y - 500, 300, 500, "Start Menu", false);
        WindowManager.Register(startMenu);

    }

    public override void Update()
    {
        base.Update();
        desktop.Update();
        taskbar.Update();
    }

    public override void Dispose()
    {
        base.Dispose();
    }

}

