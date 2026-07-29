using System.Drawing;
using Cosmos.Kernel.Core.IO;
using Windose;

public class Checkbox : Component
{
    public bool useBorders = true;
    private bool isPressed = false;
    public Color borderColor = Color.White;
    public int fontSize = 0;
    public Color textColor = Color.Black;
    public event Action<bool> CheckedChanged;
    public Action Click;

    public bool Checked
    {
        get => isPressed;
        set
        {
            if (isPressed == value) return;
            isPressed = value;
            MarkDirty();
            CheckedChanged?.Invoke(isPressed);
        }
    }

    public Checkbox(int x, int y) : base(x, y, 25, 25)
    {
    }



    public override void Draw()
    {
        base.Draw();
    }

    public override void DrawLocal()
    {


        if (isPressed) DrawSunkenRectangle(0, 0, Width, Height);
        else DrawRaisedRectangle(0, 0, Width, Height);

        if (string.IsNullOrEmpty(text)) return;

        int effectiveFontSize = fontSize > 0 ? fontSize : Math.Max(1, Height - 4);
        int textY = Math.Max(0, (Height - MeasureStringHeight(effectiveFontSize)) / 2);

        DrawString(text, textColor, 2 + Width, textY, effectiveFontSize);

    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (mouse.left == MouseEvents.Release)
        {
            Checked = !Checked;
            Click?.Invoke();
            return true;
        }


        return true;
    }

    public override string GetName() => "Checkbox";
}
