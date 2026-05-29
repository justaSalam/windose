
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
        base.Start();
        Name = "Shell Explorer";
        Description = "shell explorer";
        canvas = Kernel.Instance.canvas;


        childIds.Add(ProcessManger.Start(new Taskbar(canvas) { parentId = id }));
    }

    public override void Tick()
    {
        canvas.Clear(Color.FromArgb(0, 80, 128));
        for (int i = 0; i < childIds.Count; i++)
        {
            canvas.DrawString($"Shell Child[{i}] | {childIds[i].Name}", PCScreenFont.DefaultFont, Color.White, 10, 35 + (i * 35));

        }
        base.Tick();
    }

    public override void Stop()
    {

    }

}