using System.Drawing;

public class ApiDocumentView : Component
{
    private sealed class DisplayLine
    {
        public string text;
        public int style;
        public int height;
    }

    private readonly List<DisplayLine> displayLines = new List<DisplayLine>();
    private string documentTitle = "API Reference";
    private string documentBody = "";
    private int scrollY;
    private int contentHeight;
    private int layoutWidth;

    public int fontSize = 16;
    public int scrollbarSize = 16;

    public ApiDocumentView(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
        Margin = new Thickness(0);
    }

    public void SetDocument(string title, string body)
    {
        documentTitle = title ?? "API Reference";
        documentBody = body ?? "";
        scrollY = 0;
        RebuildLayout();
        MarkDirty();
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        RebuildLayout();
    }

    public override void DrawLocal()
    {
        if (layoutWidth != Width) RebuildLayout();

        DrawFilledRectangle(Palette.ControlWhite, 0, 0, Width, Height);
        DrawSunkenRectangle(0, 0, Width, Height);

        int viewportWidth = Math.Max(1, Width - scrollbarSize - 4);
        DrawFilledRectangle(Palette.ActiveTitle, 2, 2, viewportWidth, 30);
        DrawString(documentTitle, Palette.HighlightText, 10, 9, 16);

        int y = 40 - scrollY;
        for (int i = 0; i < displayLines.Count; i++)
        {
            DisplayLine line = displayLines[i];
            if (y + line.height >= 34 && y < Height - 2)
                DrawDisplayLine(line, y, viewportWidth);
            y += line.height;
        }

        DrawScrollbar();
    }

    private void DrawDisplayLine(DisplayLine line, int y, int width)
    {
        switch (line.style)
        {
            case 1:
                DrawFilledRectangle(Palette.ControlFace, 6, y, Math.Max(1, width - 10), line.height - 2);
                DrawLine(Palette.ControlShadow, 6, y + line.height - 3, width - 5, y + line.height - 3);
                DrawString(line.text, Palette.ActiveTitle, 12, y + 3, fontSize);
                break;

            case 2:
                DrawFilledRectangle(Color.FromArgb(232, 232, 232), 12, y, Math.Max(1, width - 22), line.height - 2);
                DrawString(line.text, Color.FromArgb(0, 0, 128), 18, y + 2, fontSize);
                break;

            case 3:
                DrawString("-", Palette.ControlBlack, 14, y + 1, fontSize);
                DrawString(line.text, Palette.ControlBlack, 30, y + 1, fontSize);
                break;

            default:
                if (line.text != "") DrawString(line.text, Palette.ControlBlack, 14, y + 1, fontSize);
                break;
        }
    }

    private void DrawScrollbar()
    {
        int x = Width - scrollbarSize;
        DrawFilledRectangle(Palette.ControlFace, x, 0, scrollbarSize, Height);
        DrawRaisedRectangle(x, 0, scrollbarSize, scrollbarSize);
        DrawRaisedRectangle(x, Height - scrollbarSize, scrollbarSize, scrollbarSize);
        DrawString("^", Palette.ControlBlack, x + 4, 0, 14);
        DrawString("v", Palette.ControlBlack, x + 4, Height - scrollbarSize, 14);

        Rectangle thumb = GetThumb();
        DrawRaisedRectangle(thumb.X, thumb.Y, thumb.Width, thumb.Height);
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInsideAbsolute(mouseX, mouseY)) return false;

        if (Mouse.scroll != 0)
        {
            ScrollBy(-(int)Mouse.scroll * 48);
            return true;
        }

        if (mouse.left == MouseEvents.Press)
        {
            int localX = mouseX - AbsoluteX;
            int localY = mouseY - AbsoluteY;
            if (localX >= Width - scrollbarSize)
            {
                if (localY < scrollbarSize) ScrollBy(-48);
                else if (localY >= Height - scrollbarSize) ScrollBy(48);
                else if (localY < GetThumb().Y) ScrollBy(-ViewportHeight());
                else if (localY > GetThumb().Bottom) ScrollBy(ViewportHeight());
                return true;
            }
        }

        return true;
    }

    private void ScrollBy(int amount)
    {
        scrollY = Math.Max(0, Math.Min(MaxScroll(), scrollY + amount));
        MarkDirty();
    }

    private Rectangle GetThumb()
    {
        int trackTop = scrollbarSize;
        int trackHeight = Math.Max(1, Height - scrollbarSize * 2);
        int totalHeight = Math.Max(ViewportHeight(), contentHeight);
        int thumbHeight = Math.Max(16, trackHeight * ViewportHeight() / Math.Max(1, totalHeight));
        int travel = Math.Max(0, trackHeight - thumbHeight);
        int thumbY = trackTop + (MaxScroll() == 0 ? 0 : scrollY * travel / MaxScroll());
        return new Rectangle(Width - scrollbarSize, thumbY, scrollbarSize, thumbHeight);
    }

    private int ViewportHeight() => Math.Max(1, Height - 40);
    private int MaxScroll() => Math.Max(0, contentHeight - ViewportHeight());

    private void RebuildLayout()
    {
        displayLines.Clear();
        layoutWidth = Width;

        int characterWidth = Math.Max(1, MeasureStringWidth("W", fontSize));
        int maxCharacters = Math.Max(12, (Width - scrollbarSize - 36) / characterWidth);
        string[] sourceLines = documentBody.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        for (int i = 0; i < sourceLines.Length; i++)
        {
            string source = sourceLines[i];
            int style = 0;
            int height = 19;

            if (source.StartsWith("## ")) { style = 1; height = 25; source = source.Substring(3); }
            else if (source.StartsWith("$ ")) { style = 2; height = 21; source = source.Substring(2); }
            else if (source.StartsWith("- ")) { style = 3; source = source.Substring(2); }
            else if (source == "") { displayLines.Add(new DisplayLine { text = "", style = 0, height = 10 }); continue; }

            AddWrapped(source, style, height, maxCharacters);
        }

        contentHeight = 0;
        for (int i = 0; i < displayLines.Count; i++) contentHeight += displayLines[i].height;
        scrollY = Math.Max(0, Math.Min(scrollY, MaxScroll()));
    }

    private void AddWrapped(string text, int style, int height, int maxCharacters)
    {
        string remaining = text;
        bool first = true;

        while (remaining.Length > maxCharacters)
        {
            int split = remaining.LastIndexOf(' ', maxCharacters);
            if (split <= 0) split = maxCharacters;
            displayLines.Add(new DisplayLine { text = remaining.Substring(0, split), style = first ? style : 0, height = height });
            remaining = remaining.Substring(split).TrimStart();
            first = false;
        }

        displayLines.Add(new DisplayLine { text = remaining, style = first ? style : 0, height = height });
    }

    public override bool IsOpaqueForCopy() => true;
    public override string GetName() => "ApiDocumentView";
}
