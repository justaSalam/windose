using System.Drawing;
using Cosmos.Kernel.System.Keyboard;
using Windose;

public sealed class TerminalView : Component
{
    public override bool HandlesMouseWheel => true;
    private readonly List<string> lines = new List<string>();
    private int scrollLine;
    public int fontSize = 16;
    public int maxLines = 500;

    public TerminalView(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
        Margin = new Thickness(0);
    }

    public void WriteLine(string text = "")
    {
        string[] sourceLines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < sourceLines.Length; i++) AddWrappedLine(sourceLines[i]);
        while (lines.Count > maxLines) lines.RemoveAt(0);
        ScrollToBottom();
        MarkDirty();
    }

    public void Clear()
    {
        lines.Clear();
        scrollLine = 0;
        MarkDirty();
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        scrollLine = Math.Min(scrollLine, MaxScrollLine());
    }

    public override void DrawLocal()
    {
        DrawFilledRectangle(Color.Black, 0, 0, Width, Height);
        int lineHeight = LineHeight();
        int visibleLines = VisibleLineCount();
        int y = 3;
        for (int i = scrollLine; i < lines.Count && i < scrollLine + visibleLines; i++)
        {
            DrawString(lines[i], Palette.ControlWhite, 4, y, fontSize);
            y += lineHeight;
        }

        if (MaxScrollLine() > 0)
        {
            int barHeight = Math.Max(12, Height * visibleLines / Math.Max(1, lines.Count));
            int travel = Math.Max(0, Height - barHeight);
            int barY = MaxScrollLine() == 0 ? 0 : scrollLine * travel / MaxScrollLine();
            DrawFilledRectangle(Palette.ControlShadow, Width - 4, barY, 4, barHeight);
        }
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInsideAbsolute(mouseX, mouseY)) return false;
        if (Mouse.scroll != 0)
        {
            scrollLine = Math.Max(0, Math.Min(MaxScrollLine(), scrollLine - (int)Mouse.scroll * 3));
            MarkDirty();
        }
        return true;
    }

    private void AddWrappedLine(string value)
    {
        int characterWidth = Math.Max(1, MeasureStringWidth("W", fontSize));
        int maxCharacters = Math.Max(1, (Width - 10) / characterWidth);
        string remaining = value ?? "";
        if (remaining.Length == 0) { lines.Add(""); return; }

        while (remaining.Length > maxCharacters)
        {
            lines.Add(remaining.Substring(0, maxCharacters));
            remaining = remaining.Substring(maxCharacters);
        }
        lines.Add(remaining);
    }

    private int LineHeight() => Math.Max(12, MeasureStringHeight(fontSize) + 2);
    private int VisibleLineCount() => Math.Max(1, (Height - 6) / LineHeight());
    private int MaxScrollLine() => Math.Max(0, lines.Count - VisibleLineCount());
    private void ScrollToBottom() => scrollLine = MaxScrollLine();
    public override bool IsOpaqueForCopy() => true;
    public override string GetName() => "TerminalView";
}

public sealed class CommandLineInput : Component
{
    private readonly List<string> history = new List<string>();
    private int historyIndex;
    public Func<string> prompt;
    public Action<string> submitted;
    public int fontSize = 16;

    public CommandLineInput(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
        Margin = new Thickness(0);
    }

    public override void DrawLocal()
    {
        DrawFilledRectangle(Color.Black, 0, 0, Width, Height);
        string prefix = prompt?.Invoke() ?? ">";
        int prefixWidth = MeasureStringWidth(prefix, fontSize);
        DrawString(prefix, Color.FromArgb(128, 255, 128), 4, 3, fontSize);

        int available = Math.Max(1, Width - prefixWidth - 14);
        string visible = text ?? "";
        while (visible.Length > 0 && MeasureStringWidth(visible, fontSize) > available)
            visible = visible.Substring(1);

        int textX = 6 + prefixWidth;
        DrawString(visible, Palette.ControlWhite, textX, 3, fontSize);
        DrawString("_", Palette.ControlWhite, textX + MeasureStringWidth(visible, fontSize), 3, fontSize);
        DrawLine(Palette.ControlShadow, 0, 0, Width - 1, 0);
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse) => IsInsideAbsolute(mouseX, mouseY);

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        if (KeyboardManager.ControlPressed)
        {
            if (keyEvent.Key == ConsoleKeyEx.C) WindoseClipboard.SetText(text ?? "");
            else if (keyEvent.Key == ConsoleKeyEx.V && WindoseClipboard.HasText) text += WindoseClipboard.Text.Replace("\r", "").Replace("\n", " ");
            MarkDirty();
            return;
        }

        switch (keyEvent.Key)
        {
            case ConsoleKeyEx.Enter:
                string command = text ?? "";
                if (!string.IsNullOrWhiteSpace(command))
                {
                    history.Add(command);
                    if (history.Count > 100) history.RemoveAt(0);
                }
                historyIndex = history.Count;
                text = "";
                submitted?.Invoke(command);
                MarkDirty();
                return;

            case ConsoleKeyEx.Backspace:
                if (!string.IsNullOrEmpty(text)) text = text.Substring(0, text.Length - 1);
                MarkDirty();
                return;

            case ConsoleKeyEx.UpArrow:
                if (history.Count > 0)
                {
                    historyIndex = Math.Max(0, historyIndex - 1);
                    text = history[historyIndex];
                    MarkDirty();
                }
                return;

            case ConsoleKeyEx.DownArrow:
                if (history.Count > 0)
                {
                    historyIndex = Math.Min(history.Count, historyIndex + 1);
                    text = historyIndex == history.Count ? "" : history[historyIndex];
                    MarkDirty();
                }
                return;

            default:
                if (keyEvent.KeyChar != '\0')
                {
                    text += keyEvent.KeyChar;
                    MarkDirty();
                }
                return;
        }
    }

    public override bool IsOpaqueForCopy() => true;
    public override string GetName() => "CommandLineInput";
}
