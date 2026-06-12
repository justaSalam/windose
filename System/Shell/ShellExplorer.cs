
using System.Drawing;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Mouse;
using Windose;


public class Explorer : SingleThreadedProcess
{
    private static Taskbar taskbar;
    private static Desktop desktop;
    private Canvas canvas;

    public Explorer(Canvas canvas) : base("Explorer", ProcessType.Program)
    {
        this.canvas = canvas;
    }

    public override void Start()
    {
        base.Start();
        desktop = new Desktop(0, 0, Kernel.canvas.Width, Kernel.canvas.Height);
        taskbar = new Taskbar(Color.Gray, 0, Kernel.canvas.Height - 35, canvas.Width, 35);

    }

    public override void Update()
    {
        desktop.Update();
        taskbar.Update();
    }

    public override void Dispose()
    {
        base.Dispose();
    }

}

