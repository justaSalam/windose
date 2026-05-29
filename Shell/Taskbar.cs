using System.Drawing;
using Cosmos.Kernel.System.Graphics;

public class Taskbar : Process
{
    private Canvas canvas;
    private int height = 20;

    public Taskbar(Canvas canvas)
    {
        this.canvas = canvas;
    }

    public override void Start()
    {
        base.Start();
        Name = "Taskbar";
        Description = "Shell explorer taskbar process";
    }

    public override void Tick()
    {
        base.Tick();
        canvas.DrawFilledRectangle(Color.Gray, 0, Global.screenHeight - height, Global.screenWidth, height);

    }

    public override void Stop()
    {

    }
}