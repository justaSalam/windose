using System.Drawing;
using Windose;

public class RadioButton : Component
{
    private bool _checked;
    private bool isPressed;
    private float hoverBlend;

    public bool useBorders = true;
    public Color borderColor = Palette.ControlShadow;
    public int fontSize = 0;
    public Color textColor = Palette.ControlBlack;
    public Color circleColor = Palette.Highlight;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            MarkDirty();
            CheckedChanged?.Invoke(_checked);
        }
    }

    public event Action<bool> CheckedChanged;
    public event Action Click;

    /// <summary>
    /// Group name for mutual exclusion. RadioButtons with the same group
    /// automatically uncheck each other when one is selected.
    /// </summary>
    public string Group { get; set; } = "";

    public RadioButton(int x, int y) : base(x, y, 25, 25)
    {
    }

    public override void Update()
    {
        base.Update();

        float target = state == State.Highlighted || isPressed ? 1f : 0f;
        if (Math.Abs(hoverBlend - target) < 0.01f)
        {
            hoverBlend = target;
            return;
        }

        float step = (float)Math.Clamp(Kernel.DeltaTimeMs / 120.0, 0.02, 0.35);
        hoverBlend += target > hoverBlend ? step : -step;
        hoverBlend = Math.Clamp(hoverBlend, 0f, 1f);
        MarkDirty();
    }

    public override void DrawLocal()
    {
        int centerX = Width / 2;
        int centerY = Height / 2;
        int radius = Math.Min(Width, Height) / 2 - 2;
        int dotRadius = Math.Max(3, radius - 5);


        // Classic: sunken circle
        DrawFilledCircle(Palette.ControlWhite, centerX, centerY, radius);
        DrawCircle(Palette.ControlShadow, centerX, centerY, radius);
        DrawCircle(Palette.ControlBlack, centerX, centerY, radius - 1);

        if (_checked)
            DrawFilledCircle(Palette.ControlBlack, centerX, centerY, dotRadius);


        if (text != "")
        {
            int effectiveFontSize = fontSize > 0 ? fontSize : Math.Max(1, Height - 4);
            int textY = Math.Max(0, (Height - MeasureStringHeight(effectiveFontSize)) / 2);
            DrawString(text, textColor, Width + 2, textY, effectiveFontSize);
        }
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (mouse.left == MouseEvents.Press)
        {
            isPressed = true;
            MarkDirty();
            return true;
        }

        if (mouse.left == MouseEvents.Release)
        {
            isPressed = false;
            if (!_checked)
            {
                UncheckSiblingsInGroup();
                Checked = true;
            }
            Click?.Invoke();
            MarkDirty();
            return true;
        }

        return true;
    }

    private void UncheckSiblingsInGroup()
    {
        if (string.IsNullOrEmpty(Group) || Parent == null) return;

        for (int i = 0; i < Parent.children.Count; i++)
        {
            if (Parent.children[i] is RadioButton sibling &&
                sibling != this &&
                sibling.Group == Group)
            {
                sibling._checked = false;
                sibling.MarkDirty();
                sibling.CheckedChanged?.Invoke(false);
            }
        }
    }

    public override string GetComponentName() => "RadioButton";
}