
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Windose;


public class ShellExplorer : Process
{
    private Taskbar taskbar;
    private Canvas canvas;

    public override void Start(int processId)
    {
        Name = "Shell Explorer";
        Description = "shell explorer";
        canvas = FullScreenCanvas.GetCurrentFullScreenCanvas();
        base.Start(processId);

    }


    public override void Update()
    {
        canvas.DrawFilledRectangle(Color.FromArgb(0, 80, 128), 0, 0, canvas.Width, canvas.Height);
    }

    public override void Stop()
    {

    }

}

