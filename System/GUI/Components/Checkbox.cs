using System.Drawing;
using Cosmos.Kernel.Core.IO;

public class Checkbox : Component
{
    public bool useBorders = true;
    private bool isPressed = false;
    public Color borderColor = Color.White;
    public int fontSize = 0;
    public Color textColor = Color.Black;

    public Checkbox(int x, int y) : base(x, y, 25, 25)
    {
    }



    public override void Draw()
    {
        DrawLocal();
        base.Draw();
    }

    public override void DrawLocal()
    {

        DrawFilledRectangle(Color.Gray, 0, 0, Width, Height);

        if (isPressed) DrawFilledRectangle(Color.DarkGray, 6, 6, Width - 12, Height - 12);


        if (useBorders) DrawRectangle(borderColor, 0, 0, Width, Height);

        if (text != "")
        {
            int effectiveFontSize = fontSize > 0 ? fontSize : Math.Max(1, Height - 4);
            int textY = Math.Max(0, (Height - MeasureStringHeight(effectiveFontSize)) / 2);

            DrawString(text, textColor, 2 + Width, textY, effectiveFontSize);
        }
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (mouse.left == MouseEvents.Release)
        {
            isPressed = !isPressed;
            MarkDirty();
            Serial.WriteString("Checkbox\n");
            return true;
        }


        return true;
    }

    public override string GetName() => "Checkbox";
}
