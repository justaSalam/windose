using System.Drawing;

public class Button : Component
{
    public Color color1;
    private Color darkenColor1;

    public bool useBackground = true;
    public bool useBorders = false;
    private bool isPressed = false;
    public Color borderColor = Color.White;
    public string text = "";
    public int fontSize = 0;
    public Color textColor = Color.Black;

    public Action leftMousePress;
    public Action leftMouseHold;
    public Action leftMouseRelease;

    public Button(Color color, int x, int y, int width, int height) : base(x, y, width, height)
    {
        color1 = color;
        darkenColor1 = GUIFeatures.Darken(color1, 0.15f);
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
            if (isPressed) DrawFilledRectangle(darkenColor1, 0, 0, Width, Height);
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

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {

        switch (mouse.left)
        {
            case MouseEvents.Press:
                isPressed = true;
                MarkDirty();
                leftMousePress?.Invoke();
                return true;

            case MouseEvents.Hold:
                leftMouseHold?.Invoke();
                return true;

            case MouseEvents.Release:
                leftMouseRelease?.Invoke();
                isPressed = false;
                MarkDirty();
                return true;
        }

        return true;
    }

    public override string GetName() => "Button";
}
