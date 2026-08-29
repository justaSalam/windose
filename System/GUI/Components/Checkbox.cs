using System.Drawing;
using Cosmos.Kernel.Core.IO;
using Windose;

public class Checkbox : Component
{
    private const int BoxSize = 15;
    private const int TextGap = 6;

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

    public Checkbox(int x, int y) : base(x, y, 15, 15)
    {
        clampSize = false;
    }

    public override void Update()
    {
        PrepareLayout();
        base.Update();
    }

    public override void PrepareLayout()
    {
        EnsureTextFits();
    }

    public override void DrawLocal()
    {
        EnsureTextFits();

        int boxY = Math.Max(0, (Height - BoxSize) / 2);

        DrawSunkenRectangle(0, boxY, BoxSize, BoxSize);
        if(isPressed)
        {
            DrawString("X", Color.Black, 2, boxY + 2);
        }
        

        if (string.IsNullOrEmpty(text)) return;

        int effectiveFontSize = GetEffectiveFontSize();
        int textY = Math.Max(0, (Height - MeasureStringHeight(effectiveFontSize)) / 2);

        DrawString(text, textColor, BoxSize + TextGap, textY, effectiveFontSize);
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

    private void EnsureTextFits()
    {
        if (string.IsNullOrEmpty(text)) return;

        int desiredWidth = BoxSize + TextGap + MeasureStringWidth(text, GetEffectiveFontSize()) + 2;
        if (Width < desiredWidth)
            Resize(desiredWidth, Height);
    }

    private int GetEffectiveFontSize() => fontSize > 0 ? fontSize : Math.Max(1, Height - 8);

    public override string GetComponentName() => "Checkbox";
}
