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
        //base.Update();


        if (dirty)
        {
            foreach (Component component in components)
            {
                if (!component.Visible) continue;

                if (!component.rectangle.IntersectsWith(rectangle)) continue;

                component.MarkDirty();
            }
        }
    }
    public override void Draw()
    {
        DrawFilledRectangle(Color.FromArgb(0, 128, 128), X, Y, Width, Height);
        base.Draw();
    }

}