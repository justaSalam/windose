using System.Drawing;
using Cosmos.Kernel.Core.IO;

public class Taskbar : Component
{
    public Color color1;
    public Color color2;

    public bool useBorders = false;
    private bool useGradient = false;
    public Color borderColor = Color.White;
    public string text = "";

    public Taskbar(Color color, int x, int y, int width, int height) : base(x, y, width, height)
    {
        color1 = color;

        useGradient = false;

    }

    public Taskbar(Color color1, Color color2, int x, int y, int width, int height) : base(x, y, width, height)
    {
        this.color1 = color1;
        this.color2 = color2;

        useGradient = true;

        MarkDirty();
    }
    public override void Update()
    {
        base.Update();
    }
    public override void Draw()
    {
        if (useGradient) DrawGradient(color1, color2, X, Y, Width, Height);
        else DrawFilledRectangle(color1, 0, 0, Width, Height);

        Serial.WriteString($"[DRAW CALL] Color: {color1} for: {GetName()}\n");


        if (useBorders) DrawRectangle(borderColor, X, Y, Width, Height);
        if (text != "") DrawString(text, X, Y);

        base.Draw();
    }

    public override string GetName() => "Taskbar";

}