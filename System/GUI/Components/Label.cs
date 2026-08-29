using Cosmos.Kernel.System.Graphics.Fonts;
using System.Drawing;

public class Label : Component
{

    public bool useBackground = true;
    public bool useForeground = false;
    public int fontSize = 0;
    public Color textColor = Palette.ControlBlack;

    public Font? font;

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

        int effectiveFontSize = fontSize > 0
            ? fontSize
            : Math.Max(1, Height - 4);

        string[] lines = text.Split("\n");

        int lineHeight = effectiveFontSize + 2;
        int totalTextHeight = lines.Length * lineHeight;

        int startY = verticalTextAlignment switch
        {
            VerticalAlignment.Top => 0,

            VerticalAlignment.Center => (Height - totalTextHeight) / 2,

            VerticalAlignment.Bottom => Height - totalTextHeight,

            _ => 0
        };

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            int textWidth = MeasureStringWidth(line, effectiveFontSize);

            int x = horizontalTextAlignment switch
            {
                HorizontalAlignment.Left =>
                    2,

                HorizontalAlignment.Center =>
                    (Width - textWidth) / 2,

                HorizontalAlignment.Right =>
                    Width - textWidth - 2,

                _ => 2
            };

            int y = startY + (i * lineHeight);

            x = Math.Max(0, x);
            y = Math.Max(0, y);

            if (font != null)
            {
                DrawString(line, font, textColor, x, y);
            }
            else
            {
                DrawString(line, textColor, x, y, effectiveFontSize);
            }
        }
    }
    public override bool IsOpaqueForCopy() => useBackground;
}