using System.Drawing;
using Cosmos.Kernel.System.Graphics;

public class Button : Component
{
    public Label? label;
    public Image? image;

    public bool useBackground = true;
    public bool useBorders = false;
    private bool isPressed = false;


    public Color borderColor = Palette.ControlHighlight;
    public Color textColor = Palette.ControlBlack;

    public Action leftMousePress;
    public Action leftMouseHold;


    public bool stretchImage = false;


    public Button(string text, int x, int y, int width, int height) : base(x, y, width, height)
    {
        label = new Label(0, 0, width, height)
        {
            text = text,
            useBackground = false,
            useForeground = false,
            textColor = textColor,
            leftClickAction = leftClickAction
        };

        AddChild(label);


    }

    public Button(Image image, int x, int y, int width, int height) : base(x, y, width, height)
    {
        this.image = image;
    }

    public override void DrawLocal()
    {

        if (useBackground)
        {
            if (isPressed) DrawSunkenRectangle(0, 0, Width, Height);
            else DrawRaisedRectangle(0, 0, Width, Height);

        }


        if (label == null && image != null)
        {
            if (stretchImage)
            {
                DrawImageStretch(image, new Rectangle(0, 0, Width, Height));
            }
            else
            {
                DrawImage(image, (int)((Width / 2) - (image.Width / 2)), (int)((Height / 2) - (image.Height / 2)));
            }
        }
        else
        {
            DrawChild(label);
        }


        /* effectiveFontSize = fontSize > 0 ? fontSize : Math.Max(1, Height - 4);
        int textY = Math.Max(0, (Height - MeasureStringHeight(effectiveFontSize)) / 2);
        if (MeasureStringWidth(label.text, font) > Width && ellipsize)
        {
            int maxWidth = Width - 4; // Leave padding
            string ellipsizedText = label.text;
            while (MeasureStringWidth(ellipsizedText + "...", font) > maxWidth && ellipsizedText.Length > 0)
            {
                ellipsizedText = ellipsizedText.Substring(0, ellipsizedText.Length - 1);
            }
            ellipsizedText += "...";
            DrawString(ellipsizedText, font, textColor, 2, textY);
        }
        else
        {
            DrawString(label.text, font, textColor, 2, textY);
        }*/
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {

        isPressed = mouse.left == MouseEvents.Press || mouse.left == MouseEvents.Hold;

        switch (mouse.left)
        {
            case MouseEvents.Press:
                MarkDirty();
                leftMousePress?.Invoke();
                break;

            case MouseEvents.Hold:
                leftMouseHold?.Invoke();
                break;

            case MouseEvents.Release:
                MarkDirty();
                leftClickAction?.Invoke();
                break;
        }

        return true;

    }

    public override string GetComponentName() => "Button";
}
