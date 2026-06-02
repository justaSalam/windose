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

    public override void Start(int processId)
    {
        Name = "Taskbar";
        Description = "taskbar";
        base.Start(processId);
    }

    public override void Update()
    {
        canvas.DrawFilledRectangle(Color.Gray, 0, canvas.Height - height, canvas.Width, height);
    }
    public override void Stop()
    {

    }
}