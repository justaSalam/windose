using System.Drawing;
using Cosmos.Kernel.System.Graphics.Fonts;
using Windose;

public class Button : Component
{
    public bool useBackground = true;
    public bool useBorders = false;
    public bool useCustomFace = false;
    public Color customFaceColor = Palette.ControlFace;
    private bool isPressed = false;
    private float hoverBlend;
    public Color borderColor = Palette.ControlHighlight;
    public int fontSize = 0;
    public Color textColor = Palette.ControlBlack;

    public Action leftMousePress;
    public Action leftMouseHold;
    public Action leftMouseRelease;

    public Button(int x, int y, int width, int height) : base(x, y, width, height)
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



    public override void Draw()
    {
        base.Draw();
    }

    Color face = Palette.ControlFace;
    Color highlight = Palette.ControlHighlight;
    Color shadow = Palette.ControlShadow;
    Color darkShadow = Palette.ControlBlack;

    public override void DrawLocal()
    {

        if (useBackground)
        {
            if (isPressed) DrawSunkenRectangle(0, 0, Width, Height, face, darkShadow, shadow, highlight);
            else DrawRaisedRectangle(0, 0, Width, Height, face, highlight, shadow, darkShadow);

        }
        if (text != "")
        {
            int effectiveFontSize = fontSize > 0 ? fontSize : Math.Max(1, Height - 4);
            int textY = Math.Max(0, (Height - MeasureStringHeight(effectiveFontSize)) / 2);

            //DrawString(text, textColor, 2, textY, effectiveFontSize);
            //DrawString(text, textColor, 2, textY, 16);
            DrawString(text, PCScreenFont.DefaultFont, textColor, 2, textY, effectiveFontSize);
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
