using System.Drawing;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;

public class Desktop : Component
{
    public Desktop(int x, int y, int width, int height) : base(x, y, width, height)
    {
        MarkDirty();
    }


    public override void Update()
    {
        base.Update();

    }
    public override void Draw()
    {
        base.Draw();
        DrawFilledRectangle(Color.Blue, X, Y, Width, Height);
    }

}