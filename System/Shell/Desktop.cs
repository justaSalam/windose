using System.Drawing;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;

public class Desktop : Component
{
    public Desktop(int x, int y, int width, int height) : base(x, y, width, height)
    {
        zLayer = DrawLayer.Desktop;
    }


    public override void Update()
    {
        // The desktop is a background layer; the compositor handles redraw dependencies.
    }
    public override void Draw()
    {
        DrawFilledRectangle(Color.FromArgb(0, 128, 128), 0, 0, Width, Height);
        base.Draw();
    }

}
