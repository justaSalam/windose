using System.Drawing;
using Cosmos.Kernel.Core.IO;

public class Panel : Component
{
    public Color color1;
    public Color color2;

    public bool useBackground = true;
    public bool useBorders = false;
    private bool useGradient = false;
    public Color borderColor = Color.White;
    public string text = "";
    public int fontSize = 0;
    public Color textColor = Color.Black;

    public Panel(Color color1, Color color2, int x, int y, int width, int height) : base(x, y, width, height)
    {
        this.color1 = color1;
        this.color2 = color2;

        useGradient = true;
    }

    public Panel(Color color, int x, int y, int width, int height) : base(x, y, width, height)
    {
        color1 = color;

        useGradient = false;


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
            if (useGradient) DrawGradient(color1, color2, 0, 0, Width, Height);
            else DrawFilledRectangle(color1, 0, 0, Width, Height);
        }

        if (useBorders) DrawRectangle(borderColor, 0, 0, Width, Height);

        if (text != "")
        {
            int effectiveFontSize = fontSize > 0 ? fontSize : Math.Max(1, Height - 4);
            int textY = Math.Max(0, (Height - MeasureStringHeight(effectiveFontSize)) / 2);

            DrawString(text, textColor, 2, textY, effectiveFontSize);
        }
    }
}
