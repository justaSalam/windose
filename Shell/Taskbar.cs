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
        Name = "Taskbar";
        Description = "taskbar";
        base.Start();
    }

    public override void Update()
    {
        //canvas.DrawFilledRectangle(Color.Gray, 0, Global.screenHeight - height, Global.screenWidth, height);

    }

    public override void Stop()
    {

    }
}