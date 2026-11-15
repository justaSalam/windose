using Cosmos.Kernel.System.Input;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Windose.System.GUI.Layout;

public class Button : Component
{
    public ImageDisplayMode imageDisplayMode = ImageDisplayMode.None;
    public Label? label;
    public Image? image;


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
    public bool isStartOrb;
    public bool isTaskbarButton;

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
        if (isStartOrb)
        {
            DrawStartOrb();
            return;
        }

        if (isTaskbarButton)
        {
            DrawTaskbarButton();
            return;
        }

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

    private void DrawStartOrb()
    {
        int cx = Width / 2;
        int cy = Height / 2;
        int radius = Math.Min(Width, Height) / 2 - 2;
        if (radius < 8) radius = 8;

        bool hovered = state == State.Highlighted;

        // 1. Ethereal cyan radial bloom on hover
        if (hovered)
        {
            int glowR = radius + 3;
            for (int y = -glowR; y <= glowR; y++)
            {
                int dy2 = y * y;
                int maxDx = (int)Math.Sqrt(Math.Max(0, glowR * glowR - dy2));
                for (int x = -maxDx; x <= maxDx; x++)
                {
                    double dist = Math.Sqrt(x * x + dy2) / glowR;
                    if (dist > 0.65 && dist <= 1.0)
                    {
                        byte alpha = (byte)(80 * (1.0 - (dist - 0.65) / 0.35));
                        buffer.SetPixelAlpha(cx + x, cy + y, Color.FromArgb(alpha, 60, 200, 255).ToArgb());
                    }
                }
            }
        }

        // 2. 3D Spherical pearl fill with radiant sapphire blue gradient
        int lightX = cx - 2;
        int lightY = cy - 3;
        if (isPressed) { lightX += 1; lightY += 1; }

        for (int y = -radius; y <= radius; y++)
        {
            int py = cy + y;
            int dy2 = y * y;
            int maxDx = (int)Math.Sqrt(Math.Max(0, radius * radius - dy2));
            for (int x = -maxDx; x <= maxDx; x++)
            {
                int px = cx + x;
                double distFromLight = Math.Sqrt((px - lightX) * (px - lightX) + (py - lightY) * (py - lightY)) / (radius * 1.5);
                distFromLight = Math.Clamp(distFromLight, 0.0, 1.0);

                int rCol, gCol, bCol;
                if (isPressed)
                {
                    rCol = (int)(20 + (1.0 - distFromLight) * 45);
                    gCol = (int)(45 + (1.0 - distFromLight) * 75);
                    bCol = (int)(110 + (1.0 - distFromLight) * 85);
                }
                else
                {
                    rCol = (int)(15 + (1.0 - distFromLight) * 65);
                    gCol = (int)(55 + (1.0 - distFromLight) * 115);
                    bCol = (int)(135 + (1.0 - distFromLight) * 120);
                }

                buffer.SetPixelAlpha(px, py, Color.FromArgb(255, (byte)rCol, (byte)gCol, (byte)bCol).ToArgb());
            }
        }

        // 3. Inner bright rim and outer dark edge
        Color outerEdge = Color.FromArgb(200, 15, 30, 55);
        Color innerRing = hovered ? Color.FromArgb(170, 140, 220, 255) : Color.FromArgb(110, 100, 180, 245);
        DrawCircle(outerEdge, cx, cy, radius);
        if (radius > 2)
            DrawCircle(innerRing, cx, cy, radius - 1);

        // 4. Windows Logo / Emblem in center
        if (image != null)
        {
            int emblemSize = Math.Min(22, radius * 2 - 8);
            int embX = cx - emblemSize / 2 + (isPressed ? 1 : 0);
            int embY = cy - emblemSize / 2 + (isPressed ? 1 : 0);
            DrawImageStretch(image, new Rectangle(embX, embY, emblemSize, emblemSize));
        }

        // 5. Specular curved highlight arc across upper hemisphere
        int specH = radius * 2 / 3;
        for (int y = -radius + 2; y <= -radius + specH; y++)
        {
            int py = cy + y;
            int dy2 = y * y;
            int maxDx = (int)Math.Sqrt(Math.Max(0, (radius - 2) * (radius - 2) - dy2));
            double yRatio = 1.0 - (double)(y + radius - 2) / specH;
            byte specAlpha = (byte)(115 * yRatio);

            for (int x = -maxDx; x <= maxDx; x++)
            {
                int px = cx + x;
                buffer.SetPixelAlpha(px, py, Color.FromArgb(specAlpha, 255, 255, 255).ToArgb());
            }
        }
    }

    private void DrawTaskbarButton()
    {
        int r = 3;

        if (isSelected)
        {
            // Windows 7 Aero focused taskbar button: vibrant glowing glass plate
            DrawOverGradientVerticalRounded(Color.FromArgb(140, 90, 175, 250), Color.FromArgb(190, 40, 110, 195), 0, 0, Width, Height, r);
            DrawRoundedRectangle(Color.FromArgb(160, 130, 200, 255), 0, 0, Width, Height, r);
            if (Width > 4 && Height > 2)
                DrawHorizontalSpan(Color.FromArgb(210, 255, 255, 255), 2, 1, Width - 4);
            if (Width > 6 && Height > 2)
                DrawHorizontalSpan(Color.FromArgb(240, 80, 215, 255), 3, Height - 2, Width - 6);
        }
        else if (state == State.Highlighted)
        {
            // Windows 7 Aero hovered taskbar button: soft frosted cyan glow
            DrawOverGradientVerticalRounded(Color.FromArgb(80, 210, 235, 255), Color.FromArgb(45, 140, 195, 245), 0, 0, Width, Height, r);
            DrawRoundedRectangle(Color.FromArgb(120, 150, 215, 255), 0, 0, Width, Height, r);
            if (Width > 4 && Height > 2)
                DrawHorizontalSpan(Color.FromArgb(160, 255, 255, 255), 2, 1, Width - 4);
        }
        else
        {
            // Subtle translucent dark glass plate
            DrawOverGradientVerticalRounded(Color.FromArgb(45, 45, 65, 85), Color.FromArgb(70, 20, 32, 48), 0, 0, Width, Height, r);
            DrawRoundedRectangle(Color.FromArgb(55, 100, 145, 185), 0, 0, Width, Height, r);
        }

        // Draw icon & text
        if (image != null)
        {
            int imgY = Math.Max(0, (Height - iconSize) / 2);
            DrawImageStretch(image, new Rectangle(6, imgY, iconSize, iconSize));
        }

        if (label != null && !string.IsNullOrEmpty(label.text))
        {
            int textX = image != null ? iconSize + 12 : 6;
            int textW = Math.Max(0, Width - textX - 6);
            Rectangle textRect = new Rectangle(textX, 0, textW, Height);

            int fSize = label != null && label.fontSize > 0 ? label.fontSize : 13;
            DrawAlignedText(label.text, Color.FromArgb(160, 0, 0, 0), fSize,
                new Rectangle(textRect.X + 1, textRect.Y + 1, textRect.Width, textRect.Height),
                HorizontalAlignment.Left, VerticalAlignment.Center);

            DrawAlignedText(label.text, Color.White, fSize, textRect,
                HorizontalAlignment.Left, VerticalAlignment.Center);
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
