using Cosmos.Kernel.System.Graphics.Fonts;
using System.Drawing;

public class Label : Component
{

    public bool useBackground = true;
    public bool useForeground = false;
    public int fontSize = 16;
    public Color textColor = Palette.ControlBlack;

    public TrueTypeFont font = new TrueTypeFont("/mnt/System/Fonts/ARIAL.ttf");

    public HorizontalAlignment horizontalTextAlignment;
    public VerticalAlignment verticalTextAlignment;


    public Label(int x, int y, int width, int height) : base(x, y, width, height)
    {
        capturesInput = false;
        horizontalAlignment = HorizontalAlignment.Center;
        verticalAlignment = VerticalAlignment.Center;
    }

    public override void Draw()
    {
        base.Draw();
    }

    public override void DrawLocal()
    {
        if (useBackground)
        {
            DrawSunkenRectangle(0, 0, Width, Height);
        }
        else if (useForeground)
        {
            DrawRaisedRectangle(0, 0, Width, Height);
        }

        if (string.IsNullOrEmpty(text))
            return;

        int effectiveFontSize = fontSize > 0 ? fontSize : (font.SizePx > 0 ? font.SizePx : Math.Max(1, Height - Padding.top - Padding.bottom));
        Rectangle content = GetContentBounds();

        string[] lines = text.Split("\n");

        int lineHeight = effectiveFontSize + 2;
        int totalTextHeight = lines.Length * lineHeight;

        int startY = verticalTextAlignment switch
        {
            VerticalAlignment.Top => content.Y,

            VerticalAlignment.Center => content.Y + (content.Height - totalTextHeight) / 2,

            VerticalAlignment.Bottom => content.Bottom - totalTextHeight,

            _ => 0
        };

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            string displayLine = FitTextToWidth(line, content.Width, effectiveFontSize, font);
            int textWidth = MeasureTextWidth(displayLine, effectiveFontSize, font);

            int x = horizontalTextAlignment switch
            {
                HorizontalAlignment.Left =>
                    content.X,

                HorizontalAlignment.Center =>
                    content.X + (content.Width - textWidth) / 2,

                HorizontalAlignment.Right =>
                    content.Right - textWidth,

                _ => 2
            };

            int y = startY + (i * lineHeight);

            x = Math.Max(content.X, x);
            y = Math.Max(content.Y, y);

            if (font != null)
            {
                DrawString(displayLine, font, effectiveFontSize, textColor, x, y);
            }
        }
    }
    public override bool IsOpaqueForCopy() => useBackground;
}