using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Windose.System.GUI.Layout;

public class Button : Component
{
    public ImageDisplayMode imageDisplayMode = ImageDisplayMode.None;
    public Label? label;
    public Image? image;
    public string text = "";

    public bool useBackground = true;
    public bool useBorders = false;
    private bool isPressed = false;
    private bool isSelected;


    public Color borderColor = Palette.ControlHighlight;
    public Color textColor = Palette.ControlBlack;
    public int iconSize = 16;
    public int iconTextGap = 6;
    public HorizontalAlignment contentAlignment = HorizontalAlignment.Center;
    public bool imageOnRight;

    public Action leftMousePress;
    public Action leftMouseHold;

    public Button(string text, int x, int y, int width, int height) : base(x, y, width, height)
    {
        this.text = text;
        label = new Label(0, 0, width, height)
        {
            text = this.text,
            useBackground = false,
            useForeground = false,
            textColor = textColor,
            leftClickAction = leftClickAction,
            horizontalTextAlignment = HorizontalAlignment.Center,
            verticalAlignment = VerticalAlignment.Center
        };

        AddChild(label);

    }

    public Button(Image image, int x, int y, int width, int height) : base(x, y, width, height)
    {
        this.image = image;
        this.text = "";
        label = new Label(0, 0, width, height)
        {
            text = this.text,
            useBackground = false,
            useForeground = false,
            textColor = textColor,
            horizontalTextAlignment = HorizontalAlignment.Center,
            verticalTextAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
        };
        AddChild(label);
    }

    public Button(Image image, string text, int x, int y, int width, int height) : this(image, x, y, width, height)
    {
        this.text = text;
        if (label != null) label.text = this.text;
    }


    public override void DrawLocal()
    {
        if (label != null)
        {
            if (!string.IsNullOrEmpty(text))
                label.text = text;
            label.textColor = textColor;
        }

        if (useBackground)
        {
            if (isPressed || isSelected) DrawSunkenRectangle(0, 0, Width, Height);
            else DrawRaisedRectangle(0, 0, Width, Height);

        }

        Rectangle content = GetContentBounds();
        if (image != null && label != null && !string.IsNullOrEmpty(label.text))
        {
            DrawImageAndLabel(content);
        }
        else if (image != null && (label == null || string.IsNullOrEmpty(label.text)))
        {
            DrawButtonImage(content);
        }
        else if (label != null)
        {
            label.SetBounds(0, 0, Width, Height);
            DrawChild(label);
        }
    }

    public void SetSelected(bool selected)
    {
        if (isSelected == selected) return;

        isSelected = selected;
        MarkDirty();
    }

    private void DrawImageAndLabel(Rectangle content)
    {
        int size = Math.Max(1, Math.Min(iconSize, Math.Min(content.Height, content.Width)));
        int gap = Math.Max(0, iconTextGap);
        int textWidth = Math.Max(0, content.Width - size - gap);
        int groupWidth = Math.Min(content.Width, size + gap + Math.Min(textWidth, MeasureStringWidth(label.text, label.fontSize)));
        int groupX = contentAlignment switch
        {
            HorizontalAlignment.Left => content.X,
            HorizontalAlignment.Right => content.Right - groupWidth,
            _ => content.X + (content.Width - groupWidth) / 2,
        };
        int iconX = imageOnRight ? groupX + groupWidth - size : groupX;
        int textX = imageOnRight ? groupX : groupX + size + gap;
        int actualTextWidth = Math.Max(0, groupWidth - size - gap);
        int iconY = content.Y + (content.Height - size) / 2;

        DrawImageStretch(image, new Rectangle(iconX, iconY, size, size));
        label.SetBounds(textX, content.Y, actualTextWidth, content.Height);
        label.horizontalTextAlignment = imageOnRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        label.textColor = textColor;
        DrawChild(label);
    }

    private void DrawButtonImage(Rectangle content)
    {
        int size = Math.Max(1, Math.Min(content.Width, content.Height));
        Rectangle imageBounds = imageDisplayMode switch
        {
            ImageDisplayMode.Stretch => content,
            ImageDisplayMode.Fill => new Rectangle(content.X, content.Y, size, size),
            _ => new Rectangle(content.X + (content.Width - Math.Min(size, image.Width)) / 2,
                                content.Y + (content.Height - Math.Min(size, image.Height)) / 2,
                                Math.Min(size, image.Width), Math.Min(size, image.Height)),
        };

        if (imageBounds.Width > 0 && imageBounds.Height > 0)
            DrawImageStretch(image, imageBounds);
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
        if (mouse.right == MouseEvents.Release) rightClickAction?.Invoke();


        return true;
    }

    public override string GetComponentName() => "Button";
}
