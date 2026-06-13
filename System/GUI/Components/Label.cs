using System.Drawing;

public class Label : Component
{
    public Color color1;

    public bool useBackground = true;
    public bool useBorders = true;
    public Color borderColor = Color.White;
    public int fontSize = 0;
    public Color textColor = Color.Black;

    public Label(Color color, int x, int y, int width, int height) : base(x, y, width, height)
    {
        color1 = color;
    }



    public override void Draw()
    {
        DrawLocal();
        base.Draw();
    }

    public override void DrawLocal()
    {

        if (useBackground)
        {
            DrawFilledRectangle(color1, 0, 0, Width, Height);
        }

        if (useBorders) DrawRectangle(borderColor, 0, 0, Width, Height);

        if (text != "")
        {
            int effectiveFontSize = fontSize > 0 ? fontSize : Math.Max(1, Height - 4);
            int textY = Math.Max(0, (Height - MeasureStringHeight(effectiveFontSize)) / 2);

            DrawString(text, textColor, 2, textY, effectiveFontSize);
        }
    }


    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        return true;
    }
}