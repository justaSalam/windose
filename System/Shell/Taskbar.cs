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

    public List<Button> windows = new List<Button>();

    public Taskbar(Color color, int x, int y, int width, int height) : base(x, y, width, height)
    {
        color1 = color;

        useGradient = false;
        zLayer = DrawLayer.Taskbar;

    }

    public Taskbar(Color color1, Color color2, int x, int y, int width, int height) : base(x, y, width, height)
    {
        this.color1 = color1;
        this.color2 = color2;

        useGradient = true;

        zLayer = DrawLayer.Taskbar;

        MarkDirty();
    }
    public override void Update()
    {
        base.Update();
    }
    public override void Draw()
    {
        if (useGradient) DrawGradient(color1, color2, 0, 0, Width, Height);
        else DrawFilledRectangle(color1, 0, 0, Width, Height);

        Serial.WriteString($"[DRAW CALL] Color: {color1} for: {GetName()}\n");


        if (useBorders) DrawRectangle(borderColor, 0, 0, Width, Height);
        if (text != "") DrawString(text, 0, 0);

        base.Draw();
    }

    public override string GetName() => "Taskbar";

}
