
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Mouse;
using Windose;


public class ShellExplorer : Process
{
    private Taskbar taskbar;

    public override void Start()
    {
        Name = "Shell Explorer";
        Description = "shell explorer";



        base.Start();
    }


    public override void Update()
    {

    }

    public override void Stop()
    {

    }

    protected override void DrawCall(Canvas canvas)
    {

        canvas.DrawFilledRectangle(Color.FromArgb(0, 80, 128), 0, 0, Global.screenWidth, Global.screenHeight); //Draw Wallpaper later

    }

}

