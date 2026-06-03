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


    }
    public override void Stop()
    {

    }
}