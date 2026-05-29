using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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