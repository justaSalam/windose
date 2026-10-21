using Cosmos.Kernel.System.Input;
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
    public bool isCloseButton;
    public bool isCaptionButton;
    public bool isMaximizeButton;
    public bool isMinimizeButton;

    public Action leftMousePress;
    public Action leftMouseHold;

    public Button(string text, int x, int y, int width, int height) : base(x, y, width, height)
    {
        this.text = text;
        TabStop = true;
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
        TabStop = true;
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
        if (isCloseButton)
        {
            DrawCloseButton();
            return;
        }

        if (isCaptionButton || isMaximizeButton || isMinimizeButton)
        {
            DrawCaptionButton();
            return;
        }

        if (label != null)
        {
            if (!string.IsNullOrEmpty(text))
                label.text = text;
            label.textColor = IsInputEnabled() ? textColor : Color.Gray;
        }

        if (useBackground)
        {
            if (Palette.FlatControls)
            {
                int r = 3;
                if (isSelected)
                {
                    DrawGradientVerticalRounded(Color.FromArgb(220, 238, 252), Color.FromArgb(185, 220, 248), 0, 0, Width, Height, r);
                    DrawRoundedRectangle(Color.FromArgb(60, 127, 177), 0, 0, Width, Height, r);
                    if (Width > 4 && Height > 2)
                        DrawHorizontalSpan(Color.FromArgb(255, 255, 255), 2, 1, Width - 4);
                }
                else if (isPressed)
                {
                    DrawGradientVerticalRounded(Color.FromArgb(194, 228, 252), Color.FromArgb(165, 210, 246), 0, 0, Width, Height, r);
                    DrawRoundedRectangle(Color.FromArgb(44, 98, 139), 0, 0, Width, Height, r);
                }
                else if (state == State.Highlighted)
                {
                    DrawGradientVerticalRounded(Color.FromArgb(235, 244, 253), Color.FromArgb(200, 230, 252), 0, 0, Width, Height, r);
                    DrawRoundedRectangle(Color.FromArgb(60, 127, 177), 0, 0, Width, Height, r);
                    if (Width > 4 && Height > 2)
                        DrawHorizontalSpan(Color.FromArgb(255, 255, 255), 2, 1, Width - 4);
                }
                else
                {
                    DrawGradientVerticalRounded(Color.FromArgb(246, 246, 246), Color.FromArgb(232, 232, 232), 0, 0, Width, Height, r);
                    DrawRoundedRectangle(Color.FromArgb(172, 172, 172), 0, 0, Width, Height, r);
                    if (Width > 4 && Height > 2)
                        DrawHorizontalSpan(Color.FromArgb(255, 255, 255), 2, 1, Width - 4);
                }
            }
            else
            {
                if (isPressed || isSelected) DrawSunkenRectangle(0, 0, Width, Height);
                else DrawRaisedRectangle(0, 0, Width, Height);
            }
        }

        if (Focused)
        {
            if (Palette.FlatControls)
                DrawRoundedRectangle(Color.FromArgb(100, 60, 140, 220), 1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2), 2);
            else
                buffer.DrawDottedRectangle(Color.Black, 2, 2, Math.Max(1, Width - 4), Math.Max(1, Height - 4));
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

    private void DrawCloseButton()
    {
        int r = 3;
        int cx = Width / 2;
        int cy = Height / 2;

        if (Palette.FlatControls)
        {
            if (isPressed)
            {
                // Windows 7 Aero pressed close button: deep ruby red gradient with rounded bottom
                DrawGradientVerticalRoundedBottom(Color.FromArgb(195, 30, 30), Color.FromArgb(130, 10, 10), 0, 0, Width, Height, r);
                DrawRoundedBottomOutline(Color.FromArgb(100, 10, 10), 0, 0, Width, Height, r);
                DrawCloseGlyph(cx + 1, cy + 1, Color.FromArgb(240, 240, 240));
            }
            else if (state == State.Highlighted)
            {
                // Windows 7 Aero hovered close button: vibrant crimson red glow with rounded bottom
                DrawGradientVerticalRoundedBottom(Color.FromArgb(240, 100, 100), Color.FromArgb(200, 30, 30), 0, 0, Width, Height, r);
                if (Height > 6)
                    DrawOverGradientVertical(Color.FromArgb(160, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), 1, 1, Width - 2, Height / 2);
                if (Width > 4 && Height > 2)
                    DrawHorizontalSpan(Color.FromArgb(60, 255, 200, 200), 2, Height - 2, Width - 4);
                DrawRoundedBottomOutline(Color.FromArgb(140, 20, 20), 0, 0, Width, Height, r);
                DrawCloseGlyph(cx, cy, Color.White);
            }
            else
            {
                // Normal unhovered state: seamless translucent glass with anti-aliased dark halo
                DrawCloseHalo(cx, cy, Color.FromArgb(45, 0, 0, 0));
                DrawCloseGlyph(cx, cy, Color.FromArgb(245, 245, 245));
            }
        }
        else
        {
            if (isPressed)
            {
                DrawSunkenRectangle(0, 0, Width, Height);
                DrawCloseGlyph(cx + 1, cy + 1, Palette.ControlBlack);
            }
            else
            {
                DrawRaisedRectangle(0, 0, Width, Height);
                DrawCloseGlyph(cx, cy, Palette.ControlBlack);
            }
        }
    }

    private void DrawCaptionButton()
    {
        int r = 3;
        int cx = Width / 2;
        int cy = Height / 2;

        if (Palette.FlatControls)
        {
            if (isPressed)
            {
                // Windows 7 Aero pressed caption button: deep cool cyan-blue with rounded bottom
                DrawGradientVerticalRoundedBottom(Color.FromArgb(130, 160, 200), Color.FromArgb(80, 110, 160), 0, 0, Width, Height, r);
                DrawRoundedBottomOutline(Color.FromArgb(50, 80, 120), 0, 0, Width, Height, r);
                if (isMaximizeButton) DrawMaximizeGlyph(cx + 1, cy + 1, Color.FromArgb(240, 240, 240));
                else DrawMinimizeGlyph(cx + 1, cy + 1, Color.FromArgb(240, 240, 240));
            }
            else if (state == State.Highlighted)
            {
                // Windows 7 Aero hovered caption button: soft translucent cyan glass glow
                DrawOverGradientVerticalRoundedBottom(Color.FromArgb(180, 220, 240, 255), Color.FromArgb(100, 140, 190, 235), 0, 0, Width, Height, r);
                if (Height > 6)
                    DrawOverGradientVertical(Color.FromArgb(140, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), 1, 1, Width - 2, Height / 2);
                DrawRoundedBottomOutline(Color.FromArgb(120, 100, 160, 220), 0, 0, Width, Height, r);
                if (isMaximizeButton) DrawMaximizeGlyph(cx, cy, Color.White);
                else DrawMinimizeGlyph(cx, cy, Color.White);
            }
            else
            {
                // Normal unhovered state: seamless on glass
                if (isMaximizeButton)
                {
                    DrawMaximizeHalo(cx, cy, Color.FromArgb(45, 0, 0, 0));
                    DrawMaximizeGlyph(cx, cy, Color.FromArgb(245, 245, 245));
                }
                else
                {
                    DrawMinimizeHalo(cx, cy, Color.FromArgb(45, 0, 0, 0));
                    DrawMinimizeGlyph(cx, cy, Color.FromArgb(245, 245, 245));
                }
            }
        }
        else
        {
            if (isPressed)
            {
                DrawSunkenRectangle(0, 0, Width, Height);
                if (isMaximizeButton) DrawMaximizeGlyph(cx + 1, cy + 1, Palette.ControlBlack);
                else DrawMinimizeGlyph(cx + 1, cy + 1, Palette.ControlBlack);
            }
            else
            {
                DrawRaisedRectangle(0, 0, Width, Height);
                if (isMaximizeButton) DrawMaximizeGlyph(cx, cy, Palette.ControlBlack);
                else DrawMinimizeGlyph(cx, cy, Palette.ControlBlack);
            }
        }
    }

    private void DrawCloseGlyph(int cx, int cy, Color color)
    {
        // 11x9 crisp symmetric diagonal cross
        buffer.DrawPoint(color, cx - 5, cy - 4);
        buffer.DrawPoint(color, cx - 4, cy - 4);
        buffer.DrawPoint(color, cx + 4, cy - 4);
        buffer.DrawPoint(color, cx + 5, cy - 4);

        buffer.DrawPoint(color, cx - 4, cy - 3);
        buffer.DrawPoint(color, cx - 3, cy - 3);
        buffer.DrawPoint(color, cx + 3, cy - 3);
        buffer.DrawPoint(color, cx + 4, cy - 3);

        buffer.DrawPoint(color, cx - 3, cy - 2);
        buffer.DrawPoint(color, cx - 2, cy - 2);
        buffer.DrawPoint(color, cx + 2, cy - 2);
        buffer.DrawPoint(color, cx + 3, cy - 2);

        buffer.DrawPoint(color, cx - 2, cy - 1);
        buffer.DrawPoint(color, cx - 1, cy - 1);
        buffer.DrawPoint(color, cx + 1, cy - 1);
        buffer.DrawPoint(color, cx + 2, cy - 1);

        // Center 3px solid bridge: eliminates pinched waist
        buffer.DrawPoint(color, cx - 1, cy);
        buffer.DrawPoint(color, cx, cy);
        buffer.DrawPoint(color, cx + 1, cy);

        buffer.DrawPoint(color, cx - 2, cy + 1);
        buffer.DrawPoint(color, cx - 1, cy + 1);
        buffer.DrawPoint(color, cx + 1, cy + 1);
        buffer.DrawPoint(color, cx + 2, cy + 1);

        buffer.DrawPoint(color, cx - 3, cy + 2);
        buffer.DrawPoint(color, cx - 2, cy + 2);
        buffer.DrawPoint(color, cx + 2, cy + 2);
        buffer.DrawPoint(color, cx + 3, cy + 2);

        buffer.DrawPoint(color, cx - 4, cy + 3);
        buffer.DrawPoint(color, cx - 3, cy + 3);
        buffer.DrawPoint(color, cx + 3, cy + 3);
        buffer.DrawPoint(color, cx + 4, cy + 3);

        buffer.DrawPoint(color, cx - 5, cy + 4);
        buffer.DrawPoint(color, cx - 4, cy + 4);
        buffer.DrawPoint(color, cx + 4, cy + 4);
        buffer.DrawPoint(color, cx + 5, cy + 4);
    }

    private void DrawCloseHalo(int cx, int cy, Color color)
    {
        DrawCloseGlyph(cx - 1, cy, color);
        DrawCloseGlyph(cx + 1, cy, color);
        DrawCloseGlyph(cx, cy - 1, color);
        DrawCloseGlyph(cx, cy + 1, color);
    }

    private void DrawMaximizeGlyph(int cx, int cy, Color color)
    {
        for (int y = 0; y < 2; y++)
            for (int x = -4; x <= 4; x++)
                buffer.DrawPoint(color, cx + x, cy - 4 + y);

        for (int x = -4; x <= 4; x++)
            buffer.DrawPoint(color, cx + x, cy + 4);

        for (int y = -2; y <= 3; y++)
        {
            buffer.DrawPoint(color, cx - 4, cy + y);
            buffer.DrawPoint(color, cx + 4, cy + y);
        }
    }

    private void DrawMaximizeHalo(int cx, int cy, Color color)
    {
        DrawMaximizeGlyph(cx - 1, cy, color);
        DrawMaximizeGlyph(cx + 1, cy, color);
        DrawMaximizeGlyph(cx, cy - 1, color);
        DrawMaximizeGlyph(cx, cy + 1, color);
    }

    private void DrawMinimizeGlyph(int cx, int cy, Color color)
    {
        for (int y = 0; y < 2; y++)
            for (int x = -4; x <= 4; x++)
                buffer.DrawPoint(color, cx + x, cy + 2 + y);
    }

    private void DrawMinimizeHalo(int cx, int cy, Color color)
    {
        DrawMinimizeGlyph(cx - 1, cy, color);
        DrawMinimizeGlyph(cx + 1, cy, color);
        DrawMinimizeGlyph(cx, cy - 1, color);
        DrawMinimizeGlyph(cx, cy + 1, color);
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
        if (!IsInputEnabled()) return false;

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

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        if (!IsInputEnabled()) return;
        if (keyEvent.Key == Key.Enter || GetPrintableCharacter(keyEvent) == ' ')
            leftClickAction?.Invoke();
    }
}
