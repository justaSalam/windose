using System.Drawing;

public class Label : Component
{

    public bool useBackground = true;
    public int fontSize = 0;

    public Label(int x, int y, int width, int height) : base(x, y, width, height)
    {
    }



    public override void Draw()
    {
        base.Draw();
    }

    public override void DrawLocal()
    {

        if (useBackground)
        {
            DrawSunkenRectangle(0, 0, Width, Height);
        }


        if (text != "")
        {
            int effectiveFontSize = fontSize > 0 ? fontSize : Math.Max(1, Height - 4);
            int textY = Math.Max(0, (Height - MeasureStringHeight(effectiveFontSize)) / 2);

            DrawString(text, Palette.ControlWhite, 2, textY, effectiveFontSize);
        }
    }


    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        return true;
    }

    public override bool IsOpaqueForCopy() => useBackground;
}
