using System.Drawing;
using Windose;

public class Button : Component
{
    public bool useBackground = true;
    public bool useBorders = false;
    private bool isPressed = false;
    public Color borderColor = Palette.ControlHighlight;
    public int fontSize = 0;
    public Color textColor = Palette.ControlBlack;

    public Action leftMousePress;
    public Action leftMouseHold;
    public Action leftMouseRelease;

    public Button(int x, int y, int width, int height) : base(x, y, width, height)
    {
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
            if (isPressed) DrawSunkenRectangle(0, 0, Width, Height);
            else DrawRaisedRectangle(0, 0, Width, Height);

        }
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
