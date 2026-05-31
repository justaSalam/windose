
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Windose;


public class ShellExplorer : Process
{
    private Canvas canvas;
    private Taskbar taskbar;

    public override void Start()
    {
        Name = "Shell Explorer";
        Description = "shell explorer";
        base.Start();
        canvas = Kernel.Instance.canvas;

    }

    public override void Update()
    {
        //canvas.Clear(Color.FromArgb(0, 80, 128));
    }

    public override void Stop()
    {

    }

}

