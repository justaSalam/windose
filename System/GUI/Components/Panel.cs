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

    public Panel(Color color1, Color color2, int x, int y, int width, int height) : base(x, y, width, height)
    {
        this.color1 = color1;
        this.color2 = color2;

        useGradient = true;
        Init();
    }

    public Panel(Color color, int x, int y, int width, int height) : base(x, y, width, height)
    {
        color1 = color;

        useGradient = false;
        Init();
    }

    private void Init()
    {
        useBackground = true;
        useBorders = false;
        text = "";
    }

    public override void Draw()
    {
        base.Draw();
        if (useBackground)
        {
            if (useGradient) DrawGradient(color1, color2, X, Y, Width, Height);
            else DrawFilledRectangle(color1, X, Y, Width, Height);
        }

        if (useBorders) DrawRectangle(borderColor, X, Y, Width, Height);
        if (text != "") DrawString(text, X, Y);

    }
}