using System.Drawing;
using Cosmos.Kernel.System.Graphics.Fonts;
using Windose;

public class Button : Component
{
    public bool useBackground = true;
    public bool useBorders = false;
    public bool useCustomFace = false;
    private bool isPressed = false;

    private float hoverBlend;
    public bool ellipsize = false;

    public int fontSize = 0;

    public Color borderColor = Palette.ControlHighlight;
    public Color textColor = Palette.ControlBlack;
    public Color customFaceColor = Palette.ControlFace;

    public Action leftMousePress;
    public Action leftMouseHold;
    public Action leftMouseRelease;

    public Font ?font;

    public Button(int x, int y, int width, int height) : base(x, y, width, height)
    {
        if(font == null) font = SystemFonts.spleen8x16;
        fontSize = font.Width;
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
            if(MeasureStringWidth(text, font) > Width && ellipsize)
            {
                int maxWidth = Width - 4; // Leave padding
                string ellipsizedText = text;
                while (MeasureStringWidth(ellipsizedText + "...", font) > maxWidth && ellipsizedText.Length > 0)
                {
                    ellipsizedText = ellipsizedText.Substring(0, ellipsizedText.Length - 1);
                }
                ellipsizedText += "...";
                DrawString(ellipsizedText, font, textColor, 2, textY);
            }
            else
            {
                DrawString(text, font, textColor, 2, textY);
            }
            

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
