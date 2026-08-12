using Cosmos.Kernel.System.Graphics.Fonts;
using System.Drawing;

public class Label : Component
{

    public bool useBackground = true;
    public bool useForeground = false;
    public int fontSize = 0;
    public Color textColor = Palette.ControlBlack;

    public Font ?font;

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
        else if (useForeground)
        {
            DrawRaisedRectangle(0, 0, Width, Height);
        }

        int currentOffset = 0;
        if (text != "")
        {
            int effectiveFontSize = fontSize > 0 ? fontSize : Math.Max(1, Height - 4);
            int textY = Math.Max(0, (Height - MeasureStringHeight(effectiveFontSize)) / 2);

            string[] formatted = text.Split("\n");

            foreach (string line in formatted)
            {
                if (font != null) DrawString(text, font, textColor, 2, textY + currentOffset);
                else DrawString(text, textColor, 2, textY + currentOffset, effectiveFontSize);
                currentOffset += effectiveFontSize + 2;
            }
        }
    }


    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        return true;
    }

    public override bool IsOpaqueForCopy() => useBackground;
}
