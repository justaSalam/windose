using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Timer;
using System.Collections.Concurrent;
using System.Drawing;

public sealed class TerminalView : Component
{
    public override bool HandlesMouseWheel => true;

    private readonly List<string> lines = new List<string>();
    private readonly List<string> history = new List<string>();
    // Enter on the input line pushes here. ReadLineSync (called from a
    // command's own worker thread) blocks on Take() until something arrives.
    private readonly BlockingCollection<string> pendingInput = new BlockingCollection<string>();

    private int scrollLine;
    private int historyIndex;
    private bool cursorVisible = true;
    private SoftwareTimer? cursorTimer;

    private string inputText = "";
    private bool isReadingLine;     // true while some command's ReadLineSync is blocked
    private string activePrompt = "> ";

    public int fontSize = 16;
    public int maxLines = 500;

    // Called with the finished line ONLY when nobody is inside ReadLineSync —
    // i.e. this is a normal top-level command entry. Wire this to whatever
    // dispatches onto the per-window/command worker thread; do NOT call
    // CommandRegistryV2.Execute directly from in here, or the first command
    // still blocks this input-delivery thread.
    public Action<string>? OnTopLevelSubmit;

    // Supplies the prompt text for normal top-level input, re-evaluated every
    // draw and every submit, so it always reflects live state such as the
    // current directory. Not used while a command is inside ReadLineSync;
    // that command's own prompt (e.g. "DISKPART> ") wins.
    public Func<string>? PromptProvider;

    private string CurrentPrompt() => isReadingLine ? activePrompt : (PromptProvider?.Invoke() ?? "> ");

    private TrueTypeFont font = SystemFonts.msSansSerif;

    public TerminalView(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
        Margin = new Thickness(0);

        cursorTimer = TimerManager.ScheduleRecurring(() =>
        {
            cursorVisible = !cursorVisible;
            MarkDirty();
        }, TimeSpan.FromMilliseconds(500));
    }

    // ---- output, unchanged from TerminalView ----
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

    // ---- blocking read for interactive commands (diskpart, uac elevate) ----
    // Call this FROM the command's own worker thread, never from the thread
    // that delivers keyboard events, or it deadlocks itself.
    public string ReadLineSync(string prompt)
    {
        activePrompt = prompt;
        isReadingLine = true;
        MarkDirty();
        string result = pendingInput.Take();
        isReadingLine = false;
        activePrompt = "> ";
        return result;
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

        // Reserve the last visible row for the live input line.
        int scrollbackRows = Math.Max(0, visibleLines - 1);
        for (int i = scrollLine; i < lines.Count && i < scrollLine + scrollbackRows; i++)
        {
            DrawString(lines[i], Palette.ControlWhite, 4, y, fontSize);
            y += lineHeight;
        }

        DrawInputLine(y);

        if (MaxScrollLine() > 0)
        {
            int barHeight = Math.Max(12, Height * scrollbackRows / Math.Max(1, lines.Count));
            int travel = Math.Max(0, Height - barHeight);
            int barY = MaxScrollLine() == 0 ? 0 : scrollLine * travel / MaxScrollLine();
            DrawFilledRectangle(Palette.ControlShadow, Width - 4, barY, 4, barHeight);
        }
    }

    private void DrawInputLine(int y)
    {
        string prefix = CurrentPrompt();
        int prefixWidth = font.MeasureString(prefix);
        DrawString(prefix, font, fontSize, Color.FromArgb(128, 255, 128), 4, y);

        int available = Math.Max(1, Width - prefixWidth - 14);
        string visible = inputText ?? "";
        while (visible.Length > 0 && font.MeasureString(visible) > available)
            visible = visible.Substring(1);

        int textX = 6 + prefixWidth;
        DrawString(visible, font, fontSize, Palette.ControlWhite, textX, y);

        if (cursorVisible)
            DrawString("_", font, fontSize, Palette.ControlWhite, textX + font.MeasureString(visible), y);
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInsideAbsolute(mouseX, mouseY)) return false;
        if (Mouse.scroll != 0)
        {
            scrollLine = Math.Max(0, Math.Min(MaxScrollLine(), scrollLine + (int)Mouse.scroll * 3));
            MarkDirty();
        }
        return true;
    }

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        bool isControlPressed = IsControlPressed(keyEvent);

        if (isControlPressed)
        {
            if (keyEvent.Key == ConsoleKeyEx.C)
                WindoseClipboard.SetText(inputText ?? "");
            else if (keyEvent.Key == ConsoleKeyEx.V && WindoseClipboard.HasText)
                inputText += WindoseClipboard.Text.Replace("\r", "").Replace("\n", " ");

            MarkDirty();
            return;
        }

        switch (keyEvent.Key)
        {
            case ConsoleKeyEx.Enter:
                Submit();
                return;

            case ConsoleKeyEx.Backspace:
                if (!string.IsNullOrEmpty(inputText)) inputText = inputText.Substring(0, inputText.Length - 1);
                MarkDirty();
                return;

            case ConsoleKeyEx.UpArrow:
                if (history.Count > 0)
                {
                    historyIndex = Math.Max(0, historyIndex - 1);
                    inputText = history[historyIndex];
                    MarkDirty();
                }
                return;

            case ConsoleKeyEx.DownArrow:
                if (history.Count > 0)
                {
                    historyIndex = Math.Min(history.Count, historyIndex + 1);
                    inputText = historyIndex == history.Count ? "" : history[historyIndex];
                    MarkDirty();
                }
                return;

            default:
                char printable = GetPrintableCharacter(keyEvent);
                if (printable != '\0')
                {
                    inputText += printable;
                    MarkDirty();
                }
                return;
        }
    }

    private void Submit()
    {
        string submitted = inputText ?? "";
        inputText = "";
        historyIndex = history.Count;
        if (submitted.Length > 0) history.Add(submitted);

        // Echo what was typed into the scrollback — this is the part the
        // old two-widget setup never did.
        WriteLine(CurrentPrompt() + submitted);

        if (isReadingLine)
        {
            // Feeds whatever command is blocked inside ReadLineSync. This is
            // just a queue push — it does not run any command logic itself,
            // so it can't deadlock no matter what thread we're on.
            pendingInput.Add(submitted);
        }
        else
        {
            // Top-level command line. Hand off to a worker thread — do not
            // invoke CommandRegistryV2.Execute directly from this method.
            OnTopLevelSubmit?.Invoke(submitted);
        }

        MarkDirty();
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
    private int MaxScrollLine() => Math.Max(0, lines.Count - Math.Max(0, VisibleLineCount() - 1));
    private void ScrollToBottom() => scrollLine = MaxScrollLine();

    public override bool IsOpaqueForCopy() => true;
    public override string GetComponentName() => "TerminalConsole";

    public override void Dispose()
    {
        TimerManager.Cancel(cursorTimer);
        // Unblocks any thread currently sitting in ReadLineSync (e.g. a
        // diskpart session left open when the window closes) with a clean
        // exception instead of leaving it blocked forever after this object
        // is gone.
        pendingInput.CompleteAdding();
        pendingInput.Dispose();
        base.Dispose();
    }
}
