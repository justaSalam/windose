using System.Drawing;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Input;
using Cosmos.Kernel.System.Timers;


/// <summary>
/// TODO Implement multi line fields
/// </summary>
public class TextField : Component
{
    public bool useBackground = true;
    public bool truncate = true;
    public bool readOnly;
    public bool Multiline;

    public Color textColor = Color.Black;
    private bool cursorVisible = true;
    private bool selected;

    private SoftwareTimer ?cursorTimer;
    private bool timerStopped;

    /// <summary>Fired whenever the text changes (typing, paste, cut, programmatic set).</summary>
    public event Action<string> TextChanged;

    /// <summary>Raised when Enter is pressed in a single-line field (e.g. address bar).</summary>
    public event Action EnterPressed;

    /// <summary>Maximum text length. 0 means unlimited.</summary>
    public int MaxLength { get; set; }

    /// <summary>Mask character for password fields. '\0' disables masking.</summary>
    public char PasswordChar { get; set; }

    /// <summary>Greyed hint shown when the field is empty.</summary>
    public string PlaceholderText { get; set; } = "";

    private int caret;
    private int anchor = -1;
    private bool dragging;
    private int goalColumn = -1;

    public int SelectionStart => anchor < 0 ? caret : Math.Min(anchor, caret);
    public int SelectionLength => anchor < 0 ? 0 : Math.Abs(caret - anchor);
    public bool HasSelection => SelectionLength > 0;

    public string SelectedText
    {
        get
        {
            if (!HasSelection) return "";
            int start = SelectionStart;
            int len = Math.Min(SelectionLength, Math.Max(0, text.Length - start));
            return len <= 0 ? "" : text.Substring(start, len);
        }
    }

    public void SelectAll()
    {
        anchor = 0;
        caret = text.Length;
        MarkDirty();
    }

    public void Select(int start, int length)
    {
        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);
        anchor = length == 0 ? -1 : start;
        caret = start + length;
        MarkDirty();
    }

    public void ClearSelection()
    {
        anchor = -1;
        MarkDirty();
    }


    private TrueTypeFont font = new TrueTypeFont("/mnt/System/Fonts/ARIAL.ttf");

    public TextField(int x, int y, int width, int height = 20) : base(x, y, width, height)
    {
        TabStop = true;
        cursorTimer = TimerManager.ScheduleRecurring(() =>
        {
            if (timerStopped) return;
            cursorVisible = !cursorVisible;
            MarkDirty();
        }, TimeSpan.FromMilliseconds(500));
    }

    public override void DrawLocal()
    {
        if (useBackground)
        {
            DrawSunkenRectangle(0, 0, Width, Height);
        }

        int effectiveFontSize = font.SizePx > 0 ? font.SizePx : Math.Max(1, Height - Padding.top - Padding.bottom);
        Rectangle content = GetContentBounds();
        Color displayColor = IsInputEnabled() ? textColor : Color.Gray;

        if (text == "" && PlaceholderText != "")
        {
            string hint = truncate && !Multiline
                ? FitTextToWidth(PlaceholderText, content.Width, effectiveFontSize, font)
                : PlaceholderText;
            int hintY = content.Y + Math.Max(0, (content.Height - effectiveFontSize) / 2);
            DrawString(hint, font, effectiveFontSize, Color.Gray, content.X, hintY);
        }
        else
        {
            DrawTextLines(content, effectiveFontSize, displayColor);
        }

        if (!readOnly && cursorVisible && (selected || Focused) && IsInputEnabled())
            DrawCaret(content, effectiveFontSize);
    }

    private string Masked(string s)
    {
        return PasswordChar != '\0' && s != "" ? new string(PasswordChar, s.Length) : s;
    }

    private string[] VisualLines()
    {
        if (Multiline) return text.Split('\n');
        return new string[] { text };
    }

    private void DrawTextLines(Rectangle content, int fontSize, Color color)
    {
        string[] lines = VisualLines();
        int selStart = SelectionStart;
        int selEnd = selStart + SelectionLength;
        int offset = 0;

        for (int li = 0; li < lines.Length; li++)
        {
            int y = content.Y + li * (fontSize + 2);
            if (y >= content.Bottom) break;
            string line = Masked(lines[li]);
            int lineStart = offset;

            if (!HasSelection || selEnd <= lineStart || selStart >= lineStart + line.Length)
            {
                string shown = truncate && !Multiline ? FitTextToWidth(line, content.Width, fontSize, font) : line;
                DrawString(shown, font, fontSize, color, content.X, y);
            }
            else
            {
                int a = Math.Max(selStart, lineStart) - lineStart;
                int b = Math.Min(selEnd, lineStart + line.Length) - lineStart;
                DrawTextSegment(line, 0, a, content.X, y, fontSize, color, false);
                int ax = content.X + font.MeasureString(line.Substring(0, a), fontSize);
                DrawTextSegment(line, a, b, ax, y, fontSize, color, true);
                int bx = content.X + font.MeasureString(line.Substring(0, b), fontSize);
                DrawTextSegment(line, b, line.Length, bx, y, fontSize, color, false);
            }

            offset += lines[li].Length + 1;
        }
    }

    private void DrawTextSegment(string line, int from, int to, int x, int y, int fontSize, Color color, bool selectedSeg)
    {
        if (to <= from) return;
        if (selectedSeg)
        {
            int w = font.MeasureString(line.Substring(from, to - from), fontSize);
            buffer.DrawFilledRectangle(Palette.Highlight, x, y, w, fontSize + 2);
        }
        DrawString(line.Substring(from, to - from), font, fontSize,
            selectedSeg ? Palette.HighlightText : color, x, y);
    }

    private void DrawCaret(Rectangle content, int fontSize)
    {
        CaretLineCol(out int line, out int col);
        string[] lines = VisualLines();
        int y = content.Y + line * (fontSize + 2);
        if (y >= content.Bottom) return;
        string shown = line < lines.Length ? Masked(lines[line]) : "";
        if (col > shown.Length) col = shown.Length;
        int x = content.X + (col == 0 ? 0 : font.MeasureString(shown.Substring(0, col), fontSize));
        if (x < content.Right)
            DrawString("_", font, fontSize, Color.Black, x, y);
    }

    private void CaretLineCol(out int line, out int col)
    {
        line = 0;
        col = caret;
        if (!Multiline) return;
        int offset = 0;
        string[] lines = VisualLines();
        for (int i = 0; i < lines.Length; i++)
        {
            if (caret <= offset + lines[i].Length) { line = i; col = caret - offset; return; }
            offset += lines[i].Length + 1;
        }
        line = lines.Length - 1;
        col = lines[line].Length;
    }

    private int CaretFromLineCol(int line, int col)
    {
        string[] lines = VisualLines();
        if (line < 0) line = 0;
        if (line >= lines.Length) line = lines.Length - 1;
        if (col < 0) col = 0;
        if (col > lines[line].Length) col = lines[line].Length;
        int offset = 0;
        for (int i = 0; i < line; i++) offset += lines[i].Length + 1;
        return offset + col;
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInputEnabled()) return false;

        selected = base.HandleInput(mouseX, mouseY, mouse);
        if (selected) RequestFocus();

        if (mouse.left == MouseEvents.Press && IsInsideAbsolute(mouseX, mouseY))
        {
            int tick = Environment.TickCount;
            int next = CaretFromPoint(mouseX - AbsoluteX, mouseY - AbsoluteY);
            if (unchecked(tick - lastClickTick) >= 0 && unchecked(tick - lastClickTick) <= DoubleClickMs)
            {
                lastClickTick = 0;
                SelectWordAt(next);
                dragging = false;
                goalColumn = -1;
                MarkDirty();
            }
            else
            {
                lastClickTick = tick;
                caret = next;
                anchor = -1;
                dragging = true;
                goalColumn = -1;
                MarkDirty();
            }
        }
        else if (mouse.left == MouseEvents.Hold && dragging)
        {
            int next = CaretFromPoint(mouseX - AbsoluteX, mouseY - AbsoluteY);
            if (next != caret)
            {
                if (anchor < 0) anchor = caret;
                caret = next;
                if (anchor == caret) anchor = -1;
                goalColumn = -1;
                MarkDirty();
            }
        }
        else if (mouse.left == MouseEvents.Release || mouse.left == MouseEvents.None)
        {
            dragging = false;
        }

        return selected;
    }

    private int CaretFromPoint(int localX, int localY)
    {
        int fontSize = font.SizePx > 0 ? font.SizePx : Math.Max(1, Height - Padding.top - Padding.bottom);
        Rectangle content = GetContentBounds();
        string[] lines = VisualLines();
        int line = 0;
        if (Multiline)
            line = Math.Clamp((localY - content.Y) / Math.Max(1, fontSize + 2), 0, lines.Length - 1);
        string shown = Masked(lines[line]);
        int target = localX - content.X;
        int best = 0;
        int bestDist = target < 0 ? -target : target;
        for (int i = 1; i <= shown.Length; i++)
        {
            int w = font.MeasureString(shown.Substring(0, i), fontSize);
            int dist = w >= target ? w - target : target - w;
            if (dist < bestDist) { bestDist = dist; best = i; }
        }
        return CaretFromLineCol(line, best);
    }

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        if (!IsInputEnabled() || readOnly) return;

        bool shift = IsShiftPressed(keyEvent);

        if (IsControlPressed(keyEvent))
        {
            if (keyEvent.Key == Key.LeftArrow) { MoveCaret(WordStart(caret), shift); return; }
            if (keyEvent.Key == Key.RightArrow) { MoveCaret(WordEnd(caret), shift); return; }
            if (keyEvent.Key == Key.C)
            {
                WindoseClipboard.SetText(HasSelection ? SelectedText : text);
                return;
            }
            if (keyEvent.Key == Key.X)
            {
                WindoseClipboard.SetText(HasSelection ? SelectedText : text);
                if (text.Length == 0) return;
                if (!HasSelection) SelectAll();
                DeleteSelection();
                return;
            }
            if (keyEvent.Key == Key.V)
            {
                if (!WindoseClipboard.HasText) return;
                InsertAtCaret(WindoseClipboard.Text);
                return;
            }
            if (keyEvent.Key == Key.A)
            {
                SelectAll();
                return;
            }
            if (keyEvent.Key == Key.Backspace)
            {
                if (HasSelection) DeleteSelection();
                else SetTextKeepCaret(text.Remove(WordStart(caret), caret - WordStart(caret)), WordStart(caret));
                return;
            }
            if (keyEvent.Key == Key.Delete)
            {
                if (HasSelection) DeleteSelection();
                else if (caret < text.Length)
                    SetTextKeepCaret(text.Remove(caret, WordEnd(caret) - caret), caret);
                return;
            }
            return;
        }

        switch (keyEvent.Key)
        {
            case Key.Backspace:
                if (HasSelection)
                    DeleteSelection();
                else if (caret > 0)
                {
                    SetTextKeepCaret(text.Remove(caret - 1, 1), caret - 1);
                }
                break;

            case Key.Delete:
                if (HasSelection)
                    DeleteSelection();
                else if (caret < text.Length)
                {
                    SetTextKeepCaret(text.Remove(caret, 1), caret);
                }
                break;

            case Key.LeftArrow:
                MoveCaret(IsControlPressed(keyEvent) ? WordStart(caret) : caret - 1, shift);
                break;

            case Key.RightArrow:
                MoveCaret(IsControlPressed(keyEvent) ? WordEnd(caret) : caret + 1, shift);
                break;

            case Key.Home:
                MoveCaret(Multiline ? CaretFromLineCol(LineOfCaret(), 0) : 0, shift);
                break;

            case Key.End:
                if (Multiline)
                {
                    string[] lines = VisualLines();
                    int line = LineOfCaret();
                    MoveCaret(CaretFromLineCol(line, lines[line].Length), shift);
                }
                else MoveCaret(text.Length, shift);
                break;

            case Key.UpArrow:
                if (Multiline) MoveCaretVertical(-1, shift);
                break;

            case Key.DownArrow:
                if (Multiline) MoveCaretVertical(1, shift);
                break;

            case Key.Enter:
                if (Multiline)
                    InsertAtCaret("\n");
                else
                    EnterPressed?.Invoke();
                break;

            default:
                char printable = GetPrintableCharacter(keyEvent);
                if (printable != '\0')
                    InsertAtCaret(printable.ToString());
                break;
        }
    }

    private int LineOfCaret()
    {
        CaretLineCol(out int line, out int unused);
        return line;
    }

    private static bool IsWordChar(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
        (c >= '0' && c <= '9') || c == '_';

    private int WordStart(int pos)
    {
        pos = Math.Clamp(pos, 0, text.Length);
        while (pos > 0 && !IsWordChar(text[pos - 1])) pos--;
        while (pos > 0 && IsWordChar(text[pos - 1])) pos--;
        return pos;
    }

    private int WordEnd(int pos)
    {
        pos = Math.Clamp(pos, 0, text.Length);
        while (pos < text.Length && !IsWordChar(text[pos])) pos++;
        while (pos < text.Length && IsWordChar(text[pos])) pos++;
        return pos;
    }

    /// <summary>Selects the word under pos (never crossing into neighboring words).</summary>
    private void SelectWordAt(int pos)
    {
        pos = Math.Clamp(pos, 0, text.Length);
        int s = pos;
        while (s < text.Length && !IsWordChar(text[s])) s++;
        if (s >= text.Length) { anchor = -1; caret = pos; return; }
        int e = s;
        while (s > 0 && IsWordChar(text[s - 1])) s--;
        while (e < text.Length && IsWordChar(text[e])) e++;
        anchor = s;
        caret = e;
    }

    private int lastClickTick;
    private const int DoubleClickMs = 500;

    private void MoveCaret(int next, bool extend)
    {
        next = Math.Clamp(next, 0, text.Length);
        if (extend)
        {
            if (anchor < 0) anchor = caret;
            caret = next;
            if (anchor == caret) anchor = -1;
        }
        else
        {
            caret = next;
            anchor = -1;
        }
        goalColumn = -1;
        MarkDirty();
    }

    private void MoveCaretVertical(int dir, bool extend)
    {
        CaretLineCol(out int line, out int col);
        if (goalColumn < 0)
        {
            string[] lines = VisualLines();
            string shown = Masked(lines[line]);
            int fontSize = font.SizePx > 0 ? font.SizePx : Math.Max(1, Height - Padding.top - Padding.bottom);
            goalColumn = col == 0 ? 0 : font.MeasureString(shown.Substring(0, Math.Min(col, shown.Length)), fontSize);
        }
        {
            string[] lines = VisualLines();
            int fontSize = font.SizePx > 0 ? font.SizePx : Math.Max(1, Height - Padding.top - Padding.bottom);
            int nextLine = Math.Clamp(line + dir, 0, lines.Length - 1);
            string shown = Masked(lines[nextLine]);
            int nextCol = 0;
            for (int i = 1; i <= shown.Length; i++)
            {
                if (font.MeasureString(shown.Substring(0, i), fontSize) <= goalColumn) nextCol = i;
                else break;
            }
            int pos = CaretFromLineCol(nextLine, nextCol);
            if (extend)
            {
                if (anchor < 0) anchor = caret;
                caret = pos;
                if (anchor == caret) anchor = -1;
            }
            else
            {
                caret = pos;
                anchor = -1;
            }
            MarkDirty();
        }
    }

    private void DeleteSelection()
    {
        if (!HasSelection) return;
        int start = SelectionStart;
        SetTextKeepCaret(text.Remove(start, SelectionLength), start);
    }

    private void InsertAtCaret(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        int start = SelectionStart;
        string next = HasSelection ? text.Remove(start, SelectionLength) : text;
        next = next.Insert(start, s);
        if (MaxLength > 0 && next.Length > MaxLength)
            next = next.Substring(0, MaxLength);
        text = next;
        caret = Math.Min(start + s.Length, text.Length);
        anchor = -1;
        goalColumn = -1;
        MarkDirty();
        TextChanged?.Invoke(text);
    }

    private void SetTextKeepCaret(string next, int newCaret)
    {
        if (MaxLength > 0 && next.Length > MaxLength)
            next = next.Substring(0, MaxLength);
        text = next;
        caret = Math.Clamp(newCaret, 0, text.Length);
        anchor = -1;
        goalColumn = -1;
        MarkDirty();
        TextChanged?.Invoke(text);
    }

    /// <summary>Sets the text, enforcing MaxLength and notifying listeners.</summary>
    public void SetText(string value)
    {
        string next = value ?? "";
        if (MaxLength > 0 && next.Length > MaxLength)
            next = next.Substring(0, MaxLength);
        if (next == text) return;
        text = next;
        caret = text.Length;
        anchor = -1;
        goalColumn = -1;
        MarkDirty();
        TextChanged?.Invoke(text);
    }

    public override void Dispose()
    {
        timerStopped = true;
        cursorTimer = null;
        base.Dispose();
    }

    public override string GetComponentName() => "TextField";
}
