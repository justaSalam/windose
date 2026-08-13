using System.Drawing;
public class Panel : Component
{
    public Color color1;
    public Color color2;

    public bool useBackground = false;
    public bool useBorders = false;
    private bool useGradient = false;
    public Color borderColor = Color.White;
    public string text = "";
    public int fontSize = 0;
    public int textOffsetX = 0;
    public Color textColor = Color.Black;
    public bool wrapText = true;

    public Panel(Color color1, Color color2, int x, int y, int width, int height) : base(x, y, width, height)
    {
        this.color1 = color1;
        this.color2 = color2;

        useGradient = true;
    }

    public Panel(Color color, int x, int y, int width, int height) : base(x, y, width, height)
    {
        color1 = color;

        useGradient = false;
    }

    public override void Draw()
    {
        base.Draw();
    }

    public override void DrawLocal()
    {

        if (useBackground)
        {
            if (useGradient) DrawGradient(color1, color2, 0, 0, Width, Height);
            else DrawFilledRectangle(color1, 0, 0, Width, Height);
        }

        if (useBorders) DrawRectangle(borderColor, 0, 0, Width, Height);

        if (text != "")
        {
            int effectiveFontSize = fontSize > 0 ? fontSize : Math.Max(1, Height - 4);
            int lineHeight = MeasureStringHeight(effectiveFontSize);
            int availableWidth = Math.Max(1, Width - 4 - textOffsetX);

            List<string> lines = wrapText
                ? WrapText(text, effectiveFontSize, availableWidth)
                : new List<string> { text };

            int totalTextHeight = lines.Count * lineHeight;
            int startY = Math.Max(0, (Height - totalTextHeight) / 2);

            for (int i = 0; i < lines.Count; i++)
            {
                DrawString(lines[i], textColor, 2 + textOffsetX, startY + i * lineHeight, effectiveFontSize);
            }
        }

        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            DrawChild(child);
        }
    }

    private List<string> WrapText(string input, int fontSize, int maxWidth)
    {
        List<string> lines = new List<string>();
        string[] words = input.Split(' ');

        string currentLine = "";

        foreach (string word in words)
        {
            string candidate = currentLine.Length == 0 ? word : currentLine + " " + word;

            if (MeasureStringWidth(candidate, fontSize) <= maxWidth)
            {
                currentLine = candidate;
            }
            else
            {
                if (currentLine.Length > 0)
                {
                    lines.Add(currentLine);
                    currentLine = word;

                    // Single word alone still too wide - hard break it
                    if (MeasureStringWidth(currentLine, fontSize) > maxWidth)
                    {
                        currentLine = HardBreakWord(currentLine, fontSize, maxWidth, lines);
                    }
                }
                else
                {
                    // First word on the line already too wide - hard break it
                    currentLine = HardBreakWord(word, fontSize, maxWidth, lines);
                }
            }
        }

        if (currentLine.Length > 0)
            lines.Add(currentLine);

        if (lines.Count == 0)
            lines.Add("");

        return lines;
    }

    private string HardBreakWord(string word, int fontSize, int maxWidth, List<string> lines)
    {
        string remaining = word;

        while (MeasureStringWidth(remaining, fontSize) > maxWidth && remaining.Length > 1)
        {
            int splitAt = remaining.Length - 1;

            while (splitAt > 1 && MeasureStringWidth(remaining.Substring(0, splitAt), fontSize) > maxWidth)
                splitAt--;

            lines.Add(remaining.Substring(0, splitAt));
            remaining = remaining.Substring(splitAt);
        }

        return remaining;
    }

    public override bool IsOpaqueForCopy() => useBackground;
}