using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using System.Drawing;
using Windose;
using System.Numerics;

public class Viewport : Component
{

    public Viewport(int x, int y, int width, int height) : base(x, y, width, height)
    {
        capturesInput = true;
        horizontalAlignment = HorizontalAlignment.Stretch;
        verticalAlignment = VerticalAlignment.Stretch;
    }

    public override void DrawLocal()
    {


    }
}