using System.Drawing;
using Cosmos.Kernel.System.Keyboard;

public class CodeEditor : Component
{
    private readonly List<string> lines = new List<string>();
    private readonly List<List<SyntaxSpan>> highlightCache = new List<List<SyntaxSpan>>();
    private int cursorLine;
    private int cursorColumn;
    private int firstVisibleLine;
    private int firstVisibleColumn;
    private int selectionAnchorLine;
    private int selectionAnchorColumn;
    private bool hasSelection;
    private bool mouseSelecting;
    private readonly List<string> completionMatches = new List<string>();
    private readonly List<string> variableSymbols = new List<string>();
    private readonly List<string> functionSymbols = new List<string>();
    private bool completionVisible;
    private int completionSelection;
    private int completionStartColumn;
    private Rectangle completionBounds;

    private static readonly string[] CompletionItems =
    {
        "add", "button", "close", "dock", "dockPanel", "else", "false", "function",
        "findProcess", "if", "let", "list", "listAdd", "listClear", "listCount", "listGet", "listItem",
        "listMode", "listRemove", "listSet", "listView", "loadDirectory", "menu", "menuBar",
        "menuItem", "on", "panel", "print", "process", "return", "scrollView", "send", "set", "show", "stack",
        "stackPanel", "statusBar", "statusPanel", "textField", "toolbar", "toolbarButton", "treeChild",
        "startTimer", "stopTimer", "timer", "treeRoot", "treeView", "true", "value", "while", "window", "windowRoot", "stopProcess",
    };

    private static readonly string[] MemberItems =
    {
        "canMaximize", "canMinimize", "canResize", "click", "doubleClick", "expanded",
        "active", "data", "fontSize", "height", "interval", "isFolder", "message", "name", "path", "running",
        "select", "sender", "size", "text", "tick", "type", "update", "visible", "width",
    };

    public int fontSize = 16;
    public int lineHeight = 18;
    public int gutterWidth = 48;
    public Color backgroundColor = Color.White;
    public Color textColor = Color.Black;
    public Color gutterColor = Palette.ControlFace;
    public Color lineNumberColor = Palette.ControlShadow;
    public Color keywordColor = Color.FromArgb(0, 0, 192);
    public Color stringColor = Color.FromArgb(160, 32, 32);
    public Color numberColor = Color.FromArgb(0, 128, 128);
    public Color commentColor = Color.FromArgb(0, 128, 0);
    public Color functionColor = Color.FromArgb(128, 0, 128);
    public Color operatorColor = Color.FromArgb(64, 64, 64);
    public Action changed;
    public Action cursorChanged;
    private int diagnosticLine = -1;
    private string diagnosticMessage = "";

    public CodeEditor(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
        Margin = new Thickness(0);
        lines.Add("");
        highlightCache.Add(null);
    }

    public string Source
    {
        get => string.Join("\n", lines);
        set => SetSource(value);
    }

    public int CursorLine => cursorLine + 1;
    public int CursorColumn => cursorColumn + 1;
    public bool HasSelection => hasSelection;
    public string DiagnosticMessage => diagnosticMessage;

    public string SelectedText
    {
        get
        {
            if (!hasSelection) return "";
            GetSelectionBounds(out int startLine, out int startColumn, out int endLine, out int endColumn);
            if (startLine == endLine)
                return lines[startLine].Substring(startColumn, endColumn - startColumn);

            string value = lines[startLine].Substring(startColumn) + "\n";
            for (int line = startLine + 1; line < endLine; line++)
                value += lines[line] + "\n";
            return value + lines[endLine].Substring(0, endColumn);
        }
    }

    public void CopySelection()
    {
        if (hasSelection) WindoseClipboard.SetText(SelectedText);
    }

    public void CutSelection()
    {
        if (!hasSelection) return;
        CopySelection();
        DeleteSelectionAndRefresh();
    }

    public void PasteClipboard()
    {
        if (!WindoseClipboard.HasText) return;
        ReplaceSelection(WindoseClipboard.Text);
    }

    public void SetDiagnostic(int line, string message)
    {
        int nextLine = string.IsNullOrEmpty(message) ? -1 : Math.Max(0, Math.Min(lines.Count - 1, line));
        string nextMessage = message ?? "";
        if (diagnosticLine == nextLine && diagnosticMessage == nextMessage) return;
        diagnosticLine = nextLine;
        diagnosticMessage = nextMessage;
        MarkDirty();
    }

    public void SetDocumentSymbols(List<string> variables, List<string> functions)
    {
        variableSymbols.Clear();
        functionSymbols.Clear();
        AddUniqueSymbols(variableSymbols, variables);
        AddUniqueSymbols(functionSymbols, functions);
    }

    public void SetSource(string source)
    {
        ClearCompletionState();
        diagnosticLine = -1;
        diagnosticMessage = "";
        lines.Clear();
        highlightCache.Clear();
        string normalized = (source ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        string[] sourceLines = normalized.Split('\n');
        for (int i = 0; i < sourceLines.Length; i++)
        {
            lines.Add(sourceLines[i]);
            highlightCache.Add(null);
        }
        if (lines.Count == 0)
        {
            lines.Add("");
            highlightCache.Add(null);
        }

        cursorLine = 0;
        cursorColumn = 0;
        ClearSelection();
        firstVisibleLine = 0;
        firstVisibleColumn = 0;
        MarkDirty();
        cursorChanged?.Invoke();
    }

    public override void DrawLocal()
    {
        DrawFilledRectangle(backgroundColor, 0, 0, Width, Height);
        DrawFilledRectangle(gutterColor, 0, 0, Math.Min(gutterWidth, Width), Height);
        DrawLine(Palette.ControlShadow, gutterWidth - 1, 0, gutterWidth - 1, Height - 1);
        DrawSunkenRectangle(0, 0, Width, Height);

        int visibleLines = Math.Max(1, (Height - 4) / lineHeight);
        int characterWidth = Math.Max(1, MeasureStringWidth("W", fontSize));
        int visibleColumns = Math.Max(1, (Width - gutterWidth - 6) / characterWidth);

        for (int row = 0; row < visibleLines; row++)
            DrawVisibleRow(row, characterWidth, visibleColumns, false);

        DrawCompletionPopup();
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInsideAbsolute(mouseX, mouseY)) return false;

        if (Mouse.scroll != 0)
        {
            ClearCompletionState();
            firstVisibleLine -= (int)Mouse.scroll * 3;
            ClampScroll();
            MarkDirty();
            return true;
        }

        if (mouse.left == MouseEvents.Press)
        {
            int completionRow = GetCompletionRow(mouseX - AbsoluteX, mouseY - AbsoluteY);
            if (completionRow >= 0)
            {
                completionSelection = completionRow;
                AcceptCompletion();
                return true;
            }

            HideCompletions();
            int oldCursorLine = cursorLine;
            int oldSelectionAnchorLine = selectionAnchorLine;
            bool oldHasSelection = hasSelection;
            int oldFirstVisibleLine = firstVisibleLine;
            int oldFirstVisibleColumn = firstVisibleColumn;
            MoveCursorToMouse(mouseX, mouseY);
            selectionAnchorLine = cursorLine;
            selectionAnchorColumn = cursorColumn;
            hasSelection = false;
            mouseSelecting = true;
            EnsureCursorVisible();
            RedrawSelectionChange(oldCursorLine, oldSelectionAnchorLine, oldHasSelection,
                oldFirstVisibleLine, oldFirstVisibleColumn);
            cursorChanged?.Invoke();
        }
        else if (mouse.left == MouseEvents.Hold && mouseSelecting)
        {
            int oldCursorLine = cursorLine;
            int oldCursorColumn = cursorColumn;
            int oldFirstVisibleLine = firstVisibleLine;
            int oldFirstVisibleColumn = firstVisibleColumn;
            MoveCursorToMouse(mouseX, mouseY);
            if (oldCursorLine == cursorLine && oldCursorColumn == cursorColumn) return true;
            hasSelection = cursorLine != selectionAnchorLine || cursorColumn != selectionAnchorColumn;
            EnsureCursorVisible();
            RedrawSelectionChange(oldCursorLine, selectionAnchorLine, true,
                oldFirstVisibleLine, oldFirstVisibleColumn);
            cursorChanged?.Invoke();
        }
        else if (mouse.left == MouseEvents.Release || mouse.left == MouseEvents.None)
        {
            mouseSelecting = false;
        }

        return true;
    }

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        bool shiftPressed = KeyboardManager.ShiftPressed;
        if (KeyboardManager.ControlPressed)
        {
            if (keyEvent.Key == ConsoleKeyEx.C) { CopySelection(); return; }
            if (keyEvent.Key == ConsoleKeyEx.X) { CutSelection(); return; }
            if (keyEvent.Key == ConsoleKeyEx.V) { PasteClipboard(); return; }
            if (keyEvent.Key == ConsoleKeyEx.A)
            {
                HideCompletions();
                selectionAnchorLine = 0;
                selectionAnchorColumn = 0;
                cursorLine = lines.Count - 1;
                cursorColumn = lines[cursorLine].Length;
                hasSelection = cursorLine != 0 || cursorColumn != 0;
                EnsureCursorVisible();
                MarkDirty();
                cursorChanged?.Invoke();
            }
            return;
        }

        if (completionVisible && shiftPressed)
            HideCompletions();

        if (completionVisible)
        {
            if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                completionSelection = completionSelection <= 0 ? completionMatches.Count - 1 : completionSelection - 1;
                RedrawCompletionArea(completionBounds);
                return;
            }
            if (keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                completionSelection = (completionSelection + 1) % completionMatches.Count;
                RedrawCompletionArea(completionBounds);
                return;
            }
            if (keyEvent.Key == ConsoleKeyEx.Tab || keyEvent.Key == ConsoleKeyEx.Enter)
            {
                AcceptCompletion();
                return;
            }
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                HideCompletions();
                return;
            }
        }

        bool modified = false;
        bool lineStructureChanged = false;
        int oldCursorLine = cursorLine;
        int oldCursorColumn = cursorColumn;
        int oldSelectionAnchorLine = selectionAnchorLine;
        bool oldHasSelection = hasSelection;
        int oldFirstVisibleLine = firstVisibleLine;
        int oldFirstVisibleColumn = firstVisibleColumn;

        bool navigationKey = IsNavigationKey(keyEvent.Key);
        if (navigationKey && shiftPressed && !hasSelection)
        {
            selectionAnchorLine = cursorLine;
            selectionAnchorColumn = cursorColumn;
        }

        switch (keyEvent.Key)
        {
            case ConsoleKeyEx.LeftArrow:
                MoveLeft();
                break;
            case ConsoleKeyEx.RightArrow:
                MoveRight();
                break;
            case ConsoleKeyEx.UpArrow:
                if (cursorLine > 0) cursorLine--;
                cursorColumn = Math.Min(cursorColumn, lines[cursorLine].Length);
                break;
            case ConsoleKeyEx.DownArrow:
                if (cursorLine < lines.Count - 1) cursorLine++;
                cursorColumn = Math.Min(cursorColumn, lines[cursorLine].Length);
                break;
            case ConsoleKeyEx.Home:
                cursorColumn = 0;
                break;
            case ConsoleKeyEx.End:
                cursorColumn = lines[cursorLine].Length;
                break;
            case ConsoleKeyEx.PageUp:
                cursorLine = Math.Max(0, cursorLine - VisibleLineCount());
                cursorColumn = Math.Min(cursorColumn, lines[cursorLine].Length);
                break;
            case ConsoleKeyEx.PageDown:
                cursorLine = Math.Min(lines.Count - 1, cursorLine + VisibleLineCount());
                cursorColumn = Math.Min(cursorColumn, lines[cursorLine].Length);
                break;
            case ConsoleKeyEx.Backspace:
                if (hasSelection)
                {
                    lineStructureChanged = SelectionSpansLines();
                    modified = DeleteSelection();
                }
                else
                {
                    lineStructureChanged = cursorColumn == 0 && cursorLine > 0;
                    modified = Backspace();
                }
                break;
            case ConsoleKeyEx.Delete:
                if (hasSelection)
                {
                    lineStructureChanged = SelectionSpansLines();
                    modified = DeleteSelection();
                }
                else
                {
                    lineStructureChanged = cursorColumn == lines[cursorLine].Length && cursorLine < lines.Count - 1;
                    modified = Delete();
                }
                break;
            case ConsoleKeyEx.Enter:
                if (hasSelection) DeleteSelection();
                InsertNewLine();
                modified = true;
                lineStructureChanged = true;
                break;
            case ConsoleKeyEx.Tab:
                lineStructureChanged = SelectionSpansLines();
                if (hasSelection) DeleteSelection();
                InsertText("    ");
                modified = true;
                break;
            default:
                if (keyEvent.KeyChar != '\0')
                {
                    lineStructureChanged = SelectionSpansLines();
                    if (hasSelection) DeleteSelection();
                    InsertText(keyEvent.KeyChar.ToString());
                    modified = true;
                }
                break;
        }

        if (navigationKey)
        {
            if (shiftPressed)
                hasSelection = cursorLine != selectionAnchorLine || cursorColumn != selectionAnchorColumn;
            else
                ClearSelection();
        }

        EnsureCursorVisible();
        bool cursorMoved = oldCursorLine != cursorLine || oldCursorColumn != cursorColumn;
        bool viewportMoved = oldFirstVisibleLine != firstVisibleLine || oldFirstVisibleColumn != firstVisibleColumn;
        if (!modified && !cursorMoved && !viewportMoved) return;

        if (lineStructureChanged)
        {
            ClearCompletionState();
            MarkDirty();
        }
        else
        {
            if (oldHasSelection || hasSelection)
                RedrawSelectionChange(oldCursorLine, oldSelectionAnchorLine, oldHasSelection,
                    oldFirstVisibleLine, oldFirstVisibleColumn);
            else
                RedrawCursorRows(oldCursorLine, oldFirstVisibleLine, oldFirstVisibleColumn);
            if (modified)
                RefreshCompletions();
            else
                HideCompletions();
        }
        cursorChanged?.Invoke();
        if (modified) changed?.Invoke();
    }

    private void MoveCursorToMouse(int mouseX, int mouseY)
    {
        int characterWidth = Math.Max(1, MeasureStringWidth("W", fontSize));
        int localX = mouseX - AbsoluteX;
        int localY = mouseY - AbsoluteY;
        cursorLine = Math.Max(0, Math.Min(lines.Count - 1,
            firstVisibleLine + Math.Max(0, localY - 2) / lineHeight));
        cursorColumn = Math.Max(0, Math.Min(lines[cursorLine].Length,
            firstVisibleColumn + Math.Max(0, localX - gutterWidth - 3) / characterWidth));
    }

    private void ClearSelection()
    {
        hasSelection = false;
        selectionAnchorLine = cursorLine;
        selectionAnchorColumn = cursorColumn;
    }

    private bool SelectionSpansLines()
        => hasSelection && selectionAnchorLine != cursorLine;

    private bool DeleteSelection()
    {
        if (!hasSelection) return false;
        GetSelectionBounds(out int startLine, out int startColumn, out int endLine, out int endColumn);

        if (startLine == endLine)
        {
            lines[startLine] = lines[startLine].Remove(startColumn, endColumn - startColumn);
            InvalidateHighlight(startLine);
        }
        else
        {
            lines[startLine] = lines[startLine].Substring(0, startColumn) + lines[endLine].Substring(endColumn);
            for (int line = endLine; line > startLine; line--)
            {
                lines.RemoveAt(line);
                highlightCache.RemoveAt(line);
            }
            InvalidateHighlight(startLine);
        }

        cursorLine = startLine;
        cursorColumn = startColumn;
        ClearSelection();
        return true;
    }

    private void DeleteSelectionAndRefresh()
    {
        int oldCursorLine = cursorLine;
        int oldAnchorLine = selectionAnchorLine;
        bool structureChanged = SelectionSpansLines();
        if (!DeleteSelection()) return;

        ClearCompletionState();
        EnsureCursorVisible();
        if (structureChanged)
            MarkDirty();
        else
            RedrawSelectionChange(oldCursorLine, oldAnchorLine, true, firstVisibleLine, firstVisibleColumn);
        cursorChanged?.Invoke();
        changed?.Invoke();
    }

    private void ReplaceSelection(string value)
    {
        int oldCursorLine = cursorLine;
        int oldAnchorLine = selectionAnchorLine;
        int oldFirstLine = firstVisibleLine;
        int oldFirstColumn = firstVisibleColumn;
        bool structureChanged = SelectionSpansLines();
        if (hasSelection) DeleteSelection();

        string normalized = (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        string[] insertedLines = normalized.Split('\n');
        if (insertedLines.Length == 1)
        {
            InsertText(insertedLines[0]);
        }
        else
        {
            structureChanged = true;
            string current = lines[cursorLine];
            string before = current.Substring(0, cursorColumn);
            string after = current.Substring(cursorColumn);
            lines[cursorLine] = before + insertedLines[0];
            InvalidateHighlight(cursorLine);
            for (int i = 1; i < insertedLines.Length; i++)
            {
                string lineText = insertedLines[i];
                if (i == insertedLines.Length - 1) lineText += after;
                lines.Insert(cursorLine + i, lineText);
                highlightCache.Insert(cursorLine + i, null);
            }
            cursorLine += insertedLines.Length - 1;
            cursorColumn = insertedLines[insertedLines.Length - 1].Length;
        }

        ClearSelection();
        ClearCompletionState();
        EnsureCursorVisible();
        if (structureChanged)
            MarkDirty();
        else
            RedrawSelectionChange(oldCursorLine, oldAnchorLine, true, oldFirstLine, oldFirstColumn);
        cursorChanged?.Invoke();
        changed?.Invoke();
    }

    private void GetSelectionBounds(out int startLine, out int startColumn, out int endLine, out int endColumn)
    {
        bool anchorFirst = selectionAnchorLine < cursorLine
            || selectionAnchorLine == cursorLine && selectionAnchorColumn <= cursorColumn;
        startLine = anchorFirst ? selectionAnchorLine : cursorLine;
        startColumn = anchorFirst ? selectionAnchorColumn : cursorColumn;
        endLine = anchorFirst ? cursorLine : selectionAnchorLine;
        endColumn = anchorFirst ? cursorColumn : selectionAnchorColumn;
    }

    private static bool IsNavigationKey(ConsoleKeyEx key)
        => key == ConsoleKeyEx.LeftArrow || key == ConsoleKeyEx.RightArrow
        || key == ConsoleKeyEx.UpArrow || key == ConsoleKeyEx.DownArrow
        || key == ConsoleKeyEx.Home || key == ConsoleKeyEx.End
        || key == ConsoleKeyEx.PageUp || key == ConsoleKeyEx.PageDown;

    private void RefreshCompletions()
    {
        Rectangle oldBounds = completionBounds;
        string line = lines[cursorLine];
        int start = cursorColumn;
        while (start > 0 && IsIdentifierCharacter(line[start - 1])) start--;
        string prefix = line.Substring(start, cursorColumn - start);
        bool memberAccess = start > 0 && line[start - 1] == '.';

        completionMatches.Clear();
        if (memberAccess)
        {
            AddCompletionMatches(MemberItems, prefix);
        }
        else if (prefix.Length > 0)
        {
            AddCompletionMatches(CompletionItems, prefix);
            AddCompletionMatches(variableSymbols, prefix);
            AddCompletionMatches(functionSymbols, prefix);
        }

        completionStartColumn = start;
        completionSelection = 0;
        completionVisible = completionMatches.Count > 0;
        completionBounds = completionVisible ? CalculateCompletionBounds() : Rectangle.Empty;
        RedrawCompletionArea(oldBounds);
    }

    private void HideCompletions()
    {
        if (!completionVisible) return;
        Rectangle oldBounds = completionBounds;
        ClearCompletionState();
        RedrawCompletionArea(oldBounds);
    }

    private void ClearCompletionState()
    {
        completionVisible = false;
        completionMatches.Clear();
        completionBounds = Rectangle.Empty;
        completionSelection = 0;
    }

    private void AcceptCompletion()
    {
        if (!completionVisible || completionSelection < 0 || completionSelection >= completionMatches.Count) return;

        Rectangle oldBounds = completionBounds;
        string completion = completionMatches[completionSelection];
        string line = lines[cursorLine];
        int prefixLength = cursorColumn - completionStartColumn;
        string insertion = ShouldAppendParenthesis(completion) ? completion + "(" : completion;
        lines[cursorLine] = line.Remove(completionStartColumn, prefixLength).Insert(completionStartColumn, insertion);
        cursorColumn = completionStartColumn + insertion.Length;
        InvalidateHighlight(cursorLine);
        ClearCompletionState();
        EnsureCursorVisible();
        RedrawLine(cursorLine, Math.Max(1, MeasureStringWidth("W", fontSize)),
            Math.Max(1, (Width - gutterWidth - 6) / Math.Max(1, MeasureStringWidth("W", fontSize))));
        RedrawCompletionArea(oldBounds);
        cursorChanged?.Invoke();
        changed?.Invoke();
    }

    private Rectangle CalculateCompletionBounds()
    {
        int characterWidth = Math.Max(1, MeasureStringWidth("W", fontSize));
        int width = Math.Min(230, Math.Max(80, Width - 4));
        int height = completionMatches.Count * lineHeight + 4;
        int x = gutterWidth + 3 + (cursorColumn - firstVisibleColumn) * characterWidth;
        x = Math.Max(2, Math.Min(x, Width - width - 2));
        int y = 2 + (cursorLine - firstVisibleLine + 1) * lineHeight;
        if (y + height >= Height - 1)
            y = 2 + (cursorLine - firstVisibleLine) * lineHeight - height;
        y = Math.Max(2, Math.Min(y, Height - height - 2));
        return new Rectangle(x, y, width, height);
    }

    private void DrawCompletionPopup()
    {
        if (!completionVisible) return;

        DrawFilledRectangle(Palette.ControlFace, completionBounds.X, completionBounds.Y,
            completionBounds.Width, completionBounds.Height);
        DrawRaisedRectangle(completionBounds.X, completionBounds.Y, completionBounds.Width, completionBounds.Height);
        for (int i = 0; i < completionMatches.Count; i++)
        {
            int y = completionBounds.Y + 2 + i * lineHeight;
            Color color = textColor;
            if (i == completionSelection)
            {
                DrawFilledRectangle(Palette.Highlight, completionBounds.X + 2, y,
                    completionBounds.Width - 4, lineHeight);
                color = Palette.HighlightText;
            }
            DrawString(completionMatches[i], color, completionBounds.X + 6, y, fontSize);
        }
    }

    private void RedrawCompletionArea(Rectangle oldBounds)
    {
        Rectangle area = oldBounds;
        if (completionVisible)
            area = area.IsEmpty ? completionBounds : Rectangle.Union(area, completionBounds);
        if (area.IsEmpty) return;

        int cursorRow = cursorLine - firstVisibleLine;
        if (cursorRow >= 0 && cursorRow < VisibleLineCount())
        {
            int cursorY = 2 + cursorRow * lineHeight;
            Rectangle cursorRowBounds = new Rectangle(1, cursorY, Math.Max(1, Width - 2),
                Math.Max(1, Math.Min(lineHeight, Height - cursorY - 1)));
            area = Rectangle.Union(area, cursorRowBounds);
        }

        int characterWidth = Math.Max(1, MeasureStringWidth("W", fontSize));
        int visibleColumns = Math.Max(1, (Width - gutterWidth - 6) / characterWidth);
        int firstRow = Math.Max(0, (area.Y - 2) / lineHeight);
        int lastRow = Math.Min(VisibleLineCount() - 1, (area.Bottom - 2) / lineHeight);
        for (int row = firstRow; row <= lastRow; row++)
            DrawVisibleRow(row, characterWidth, visibleColumns, true);

        DrawCompletionPopup();
        InvalidateLocalRegion(area);
    }

    private int GetCompletionRow(int x, int y)
    {
        if (!completionVisible || !completionBounds.Contains(x, y)) return -1;
        int row = (y - completionBounds.Y - 2) / lineHeight;
        return row >= 0 && row < completionMatches.Count ? row : -1;
    }

    private static bool IsIdentifierCharacter(char value)
        => char.IsLetterOrDigit(value) || value == '_';

    private void AddCompletionMatches(IEnumerable<string> items, string prefix)
    {
        foreach (string item in items)
        {
            if (completionMatches.Count >= 7) return;
            if (item.Length <= prefix.Length || !item.StartsWith(prefix)) continue;
            if (!completionMatches.Contains(item)) completionMatches.Add(item);
        }
    }

    private static void AddUniqueSymbols(List<string> target, List<string> source)
    {
        if (source == null) return;
        for (int i = 0; i < source.Count; i++)
        {
            string value = source[i];
            if (!string.IsNullOrEmpty(value) && !target.Contains(value)) target.Add(value);
        }
    }

    private bool ShouldAppendParenthesis(string value)
    {
        if (functionSymbols.Contains(value)) return true;
        if (IsKeyword(value)) return false;
        for (int i = 0; i < CompletionItems.Length; i++)
            if (CompletionItems[i] == value) return true;
        return false;
    }

    private void RedrawCursorRows(int oldCursorLine, int oldFirstVisibleLine, int oldFirstVisibleColumn)
    {
        if (oldFirstVisibleLine != firstVisibleLine || oldFirstVisibleColumn != firstVisibleColumn)
        {
            MarkDirty();
            return;
        }

        int characterWidth = Math.Max(1, MeasureStringWidth("W", fontSize));
        int visibleColumns = Math.Max(1, (Width - gutterWidth - 6) / characterWidth);
        RedrawLine(oldCursorLine, characterWidth, visibleColumns);
        if (cursorLine != oldCursorLine)
            RedrawLine(cursorLine, characterWidth, visibleColumns);
    }

    private void RedrawLine(int lineIndex, int characterWidth, int visibleColumns)
    {
        int row = lineIndex - firstVisibleLine;
        if (row < 0 || row >= VisibleLineCount()) return;

        DrawVisibleRow(row, characterWidth, visibleColumns, true);
        int y = 2 + row * lineHeight;
        InvalidateLocalRegion(new Rectangle(1, y, Math.Max(0, Width - 2), Math.Min(lineHeight, Height - y - 1)));
    }

    private void DrawVisibleRow(int row, int characterWidth, int visibleColumns, bool clearRow)
    {
        int y = 2 + row * lineHeight;
        int rowHeight = Math.Min(lineHeight, Height - y - 1);
        if (rowHeight <= 0) return;

        if (clearRow)
        {
            DrawFilledRectangle(gutterColor, 1, y, Math.Max(0, Math.Min(gutterWidth - 1, Width - 2)), rowHeight);
            DrawFilledRectangle(backgroundColor, Math.Min(gutterWidth, Width - 1), y,
                Math.Max(0, Width - Math.Min(gutterWidth, Width - 1) - 1), rowHeight);
            if (gutterWidth > 0 && gutterWidth < Width)
                DrawLine(Palette.ControlShadow, gutterWidth - 1, y, gutterWidth - 1, y + rowHeight - 1);
        }

        int lineIndex = firstVisibleLine + row;
        if (lineIndex >= lines.Count) return;

        string number = (lineIndex + 1).ToString();
        DrawString(number, lineNumberColor, Math.Max(3, gutterWidth - 6 - MeasureStringWidth(number, fontSize)), y, fontSize);

        string line = lines[lineIndex];
        if (firstVisibleColumn < line.Length)
            DrawHighlightedLine(lineIndex, y, characterWidth, visibleColumns);

        DrawSelection(lineIndex, line, y, characterWidth, visibleColumns);
        DrawDiagnostic(lineIndex, line, y, characterWidth, visibleColumns);

        if (lineIndex != cursorLine) return;
        int visibleColumn = cursorColumn - firstVisibleColumn;
        if (visibleColumn < 0 || visibleColumn > visibleColumns) return;

        int cursorX = gutterWidth + 3 + visibleColumn * characterWidth;
        int cursorY = y + fontSize;
        DrawLine(Color.Black, cursorX, cursorY, cursorX + characterWidth - 1, cursorY);
    }

    private void DrawSelection(int lineIndex, string line, int y, int characterWidth, int visibleColumns)
    {
        if (!hasSelection) return;
        GetSelectionBounds(out int startLine, out int startColumn, out int endLine, out int endColumn);
        if (lineIndex < startLine || lineIndex > endLine) return;

        int selectionStart = lineIndex == startLine ? startColumn : 0;
        int selectionEnd = lineIndex == endLine ? endColumn : line.Length + 1;
        int visibleStart = Math.Max(selectionStart, firstVisibleColumn);
        int visibleEnd = Math.Min(selectionEnd, firstVisibleColumn + visibleColumns);
        if (visibleStart >= visibleEnd) return;

        int x = gutterWidth + 3 + (visibleStart - firstVisibleColumn) * characterWidth;
        int width = (visibleEnd - visibleStart) * characterWidth;
        DrawFilledRectangle(Palette.Highlight, x, y, width, lineHeight);
        int textStart = Math.Min(visibleStart, line.Length);
        int textEnd = Math.Min(visibleEnd, line.Length);
        if (textStart < textEnd)
            DrawString(line.Substring(textStart, textEnd - textStart), Palette.HighlightText, x, y, fontSize);
    }

    private void DrawDiagnostic(int lineIndex, string line, int y, int characterWidth, int visibleColumns)
    {
        if (lineIndex != diagnosticLine) return;
        int visibleLength = Math.Min(visibleColumns, Math.Max(1, line.Length - firstVisibleColumn));
        int startX = gutterWidth + 3;
        int endX = Math.Min(Width - 3, startX + visibleLength * characterWidth);
        int underlineY = Math.Min(Height - 2, y + fontSize + 1);
        for (int x = startX; x < endX; x += 4)
            DrawLine(Color.Red, x, underlineY, Math.Min(x + 1, endX), underlineY);
        DrawString("!", Color.Red, Math.Max(2, gutterWidth - 16), y, fontSize);
    }

    private void RedrawSelectionChange(int oldCursorLine, int oldAnchorLine, bool oldHadSelection,
        int oldFirstVisibleLine, int oldFirstVisibleColumn)
    {
        if (oldFirstVisibleLine != firstVisibleLine || oldFirstVisibleColumn != firstVisibleColumn)
        {
            MarkDirty();
            return;
        }

        int firstLine = Math.Min(oldCursorLine, cursorLine);
        int lastLine = Math.Max(oldCursorLine, cursorLine);
        if (oldHadSelection)
        {
            firstLine = Math.Min(firstLine, oldAnchorLine);
            lastLine = Math.Max(lastLine, oldAnchorLine);
        }
        if (hasSelection)
        {
            firstLine = Math.Min(firstLine, selectionAnchorLine);
            lastLine = Math.Max(lastLine, selectionAnchorLine);
        }

        int firstRow = Math.Max(0, firstLine - firstVisibleLine);
        int lastRow = Math.Min(VisibleLineCount() - 1, lastLine - firstVisibleLine);
        if (firstRow > lastRow) return;

        int characterWidth = Math.Max(1, MeasureStringWidth("W", fontSize));
        int visibleColumns = Math.Max(1, (Width - gutterWidth - 6) / characterWidth);
        for (int row = firstRow; row <= lastRow; row++)
            DrawVisibleRow(row, characterWidth, visibleColumns, true);

        int y = 2 + firstRow * lineHeight;
        int height = Math.Min(Height - y - 1, (lastRow - firstRow + 1) * lineHeight);
        InvalidateLocalRegion(new Rectangle(1, y, Math.Max(1, Width - 2), Math.Max(1, height)));
    }

    private void InsertText(string value)
    {
        string line = lines[cursorLine];
        lines[cursorLine] = line.Insert(cursorColumn, value);
        InvalidateHighlight(cursorLine);
        cursorColumn += value.Length;
    }

    private void InsertNewLine()
    {
        string line = lines[cursorLine];
        string indentation = GetIndentation(line);
        string before = line.Substring(0, cursorColumn);
        string after = line.Substring(cursorColumn);

        lines[cursorLine] = before;
        lines.Insert(cursorLine + 1, indentation + after);
        InvalidateHighlight(cursorLine);
        highlightCache.Insert(cursorLine + 1, null);
        cursorLine++;
        cursorColumn = indentation.Length;
    }

    private bool Backspace()
    {
        if (cursorColumn > 0)
        {
            string line = lines[cursorLine];
            lines[cursorLine] = line.Remove(cursorColumn - 1, 1);
            InvalidateHighlight(cursorLine);
            cursorColumn--;
            return true;
        }

        if (cursorLine == 0) return false;
        int previousLength = lines[cursorLine - 1].Length;
        lines[cursorLine - 1] += lines[cursorLine];
        lines.RemoveAt(cursorLine);
        highlightCache.RemoveAt(cursorLine);
        cursorLine--;
        InvalidateHighlight(cursorLine);
        cursorColumn = previousLength;
        return true;
    }

    private bool Delete()
    {
        string line = lines[cursorLine];
        if (cursorColumn < line.Length)
        {
            lines[cursorLine] = line.Remove(cursorColumn, 1);
            InvalidateHighlight(cursorLine);
            return true;
        }

        if (cursorLine >= lines.Count - 1) return false;
        lines[cursorLine] += lines[cursorLine + 1];
        lines.RemoveAt(cursorLine + 1);
        highlightCache.RemoveAt(cursorLine + 1);
        InvalidateHighlight(cursorLine);
        return true;
    }

    private void MoveLeft()
    {
        if (cursorColumn > 0) cursorColumn--;
        else if (cursorLine > 0)
        {
            cursorLine--;
            cursorColumn = lines[cursorLine].Length;
        }
    }

    private void MoveRight()
    {
        if (cursorColumn < lines[cursorLine].Length) cursorColumn++;
        else if (cursorLine < lines.Count - 1)
        {
            cursorLine++;
            cursorColumn = 0;
        }
    }

    private void DrawHighlightedLine(int lineIndex, int y, int characterWidth, int visibleColumns)
    {
        List<SyntaxSpan> spans = GetHighlightSpans(lineIndex);
        string line = lines[lineIndex];
        int visibleEnd = Math.Min(line.Length, firstVisibleColumn + visibleColumns);

        for (int i = 0; i < spans.Count; i++)
        {
            SyntaxSpan span = spans[i];
            int start = Math.Max(span.start, firstVisibleColumn);
            int end = Math.Min(span.start + span.length, visibleEnd);
            if (start >= end) continue;

            int x = gutterWidth + 3 + (start - firstVisibleColumn) * characterWidth;
            string visibleText = start == span.start && end == span.start + span.length
                ? span.text
                : line.Substring(start, end - start);
            DrawString(visibleText, span.color, x, y, fontSize);
        }
    }

    private List<SyntaxSpan> GetHighlightSpans(int lineIndex)
    {
        List<SyntaxSpan> cached = highlightCache[lineIndex];
        if (cached != null) return cached;

        string line = lines[lineIndex];
        List<SyntaxSpan> spans = new List<SyntaxSpan>();
        int position = 0;
        while (position < line.Length)
        {
            int start = position;
            char current = line[position];

            if (char.IsWhiteSpace(current))
            {
                while (position < line.Length && char.IsWhiteSpace(line[position])) position++;
                AddSpan(spans, line, start, position - start, textColor);
                continue;
            }

            if (current == '/' && position + 1 < line.Length && line[position + 1] == '/')
            {
                AddSpan(spans, line, start, line.Length - start, commentColor);
                break;
            }

            if (current == '"')
            {
                position++;
                while (position < line.Length)
                {
                    if (line[position] == '\\' && position + 1 < line.Length) position += 2;
                    else if (line[position++] == '"') break;
                }
                AddSpan(spans, line, start, position - start, stringColor);
                continue;
            }

            if (char.IsDigit(current))
            {
                position++;
                while (position < line.Length && char.IsDigit(line[position])) position++;
                if (position + 1 < line.Length && line[position] == '.' && char.IsDigit(line[position + 1]))
                {
                    position++;
                    while (position < line.Length && char.IsDigit(line[position])) position++;
                }
                AddSpan(spans, line, start, position - start, numberColor);
                continue;
            }

            if (char.IsLetter(current) || current == '_')
            {
                position++;
                while (position < line.Length && (char.IsLetterOrDigit(line[position]) || line[position] == '_')) position++;
                string word = line.Substring(start, position - start);
                Color color = IsKeyword(word) ? keywordColor : IsFunctionCall(line, position) ? functionColor : textColor;
                AddSpan(spans, line, start, position - start, color);
                continue;
            }

            position++;
            if (IsOperator(current))
                while (position < line.Length && IsOperator(line[position])) position++;
            AddSpan(spans, line, start, position - start, IsOperator(current) ? operatorColor : textColor);
        }

        highlightCache[lineIndex] = spans;
        return spans;
    }

    private static void AddSpan(List<SyntaxSpan> spans, string source, int start, int length, Color color)
    {
        if (length <= 0) return;
        if (spans.Count > 0)
        {
            SyntaxSpan previous = spans[spans.Count - 1];
            if (previous.start + previous.length == start && previous.color.ToArgb() == color.ToArgb())
            {
                int combinedLength = previous.length + length;
                spans[spans.Count - 1] = new SyntaxSpan(previous.start, combinedLength, color,
                    source.Substring(previous.start, combinedLength));
                return;
            }
        }
        spans.Add(new SyntaxSpan(start, length, color, source.Substring(start, length)));
    }

    private static bool IsKeyword(string value) => value switch
    {
        "let" => true,
        "set" => true,
        "on" => true,
        "if" => true,
        "else" => true,
        "while" => true,
        "function" => true,
        "return" => true,
        "true" => true,
        "false" => true,
        _ => false,
    };

    private static bool IsFunctionCall(string line, int position)
    {
        while (position < line.Length && char.IsWhiteSpace(line[position])) position++;
        return position < line.Length && line[position] == '(';
    }

    private static bool IsOperator(char value)
        => value == '=' || value == '!' || value == '<' || value == '>' || value == '+' || value == '-'
        || value == '*' || value == '/' || value == '&' || value == '|';

    private void InvalidateHighlight(int lineIndex)
    {
        if (lineIndex >= 0 && lineIndex < highlightCache.Count) highlightCache[lineIndex] = null;
    }

    private void EnsureCursorVisible()
    {
        int visibleLines = VisibleLineCount();
        int characterWidth = Math.Max(1, MeasureStringWidth("W", fontSize));
        int visibleColumns = Math.Max(1, (Width - gutterWidth - 6) / characterWidth);

        if (cursorLine < firstVisibleLine) firstVisibleLine = cursorLine;
        if (cursorLine >= firstVisibleLine + visibleLines) firstVisibleLine = cursorLine - visibleLines + 1;
        if (cursorColumn < firstVisibleColumn) firstVisibleColumn = cursorColumn;
        if (cursorColumn >= firstVisibleColumn + visibleColumns) firstVisibleColumn = cursorColumn - visibleColumns + 1;
        ClampScroll();
    }

    private void ClampScroll()
    {
        firstVisibleLine = Math.Max(0, Math.Min(firstVisibleLine, Math.Max(0, lines.Count - VisibleLineCount())));
        firstVisibleColumn = Math.Max(0, firstVisibleColumn);
    }

    private int VisibleLineCount() => Math.Max(1, (Height - 4) / lineHeight);

    private static string GetIndentation(string line)
    {
        int count = 0;
        while (count < line.Length && (line[count] == ' ' || line[count] == '\t')) count++;
        return line.Substring(0, count);
    }

    public override bool IsOpaqueForCopy() => true;
    public override string GetName() => "CodeEditor";

    private readonly struct SyntaxSpan
    {
        public readonly int start;
        public readonly int length;
        public readonly Color color;
        public readonly string text;

        public SyntaxSpan(int start, int length, Color color, string text)
        {
            this.start = start;
            this.length = length;
            this.color = color;
            this.text = text;
        }
    }
}
