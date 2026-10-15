using Cosmos.Kernel.System.Input;
using System.Drawing;

/// <summary>
/// Compact editable grid: string columns/rows, header-click sorting, row selection,
/// double-click cell editing via an embedded TextField. Host in a ScrollView for large sets.
/// </summary>
public class DataGrid : Component
{
    public class DataColumn
    {
        public string Header = "";
        public int Width = 120;
    }

    private readonly List<DataColumn> columns = new List<DataColumn>();
    private readonly List<string[]> rows = new List<string[]>();

    public int RowHeight = 22;
    public int HeaderHeight = 24;
    public int FontSize = 14;
    public bool Editable = true;
    public bool Sortable = true;

    public int SelectedRow = -1;
    public int SortedColumn = -1;
    public bool SortAscending = true;

    public event Action<int> SelectedChanged;
    public event Action<int, int, string> CellChanged;

    private TextField editor;
    private int editRow = -1;
    private int editCol = -1;
    private int pressedRow = -1;
    private int lastEditTick;
    private int lastEditRow = -2;
    private const int DoubleClickMs = 600;

    public DataGrid(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
        TabStop = true;
    }

    public int ColumnCount => columns.Count;
    public int RowCount => rows.Count;

    public int AddColumn(string header, int width)
    {
        columns.Add(new DataColumn { Header = header ?? "", Width = Math.Max(20, width) });
        MarkDirty();
        return columns.Count - 1;
    }

    public int AddRow(string[] cells)
    {
        string[] row = new string[columns.Count];
        for (int i = 0; i < row.Length; i++)
            row[i] = cells != null && i < cells.Length ? cells[i] ?? "" : "";
        rows.Add(row);
        MarkDirty();
        return rows.Count - 1;
    }

    public void ClearRows()
    {
        rows.Clear();
        SelectedRow = -1;
        CloseEditor(false);
        MarkDirty();
    }

    public void RemoveRowAt(int index)
    {
        if (index < 0 || index >= rows.Count) return;
        rows.RemoveAt(index);
        if (SelectedRow == index) SelectedRow = -1;
        else if (SelectedRow > index) SelectedRow--;
        if (editRow == index) CloseEditor(false);
        MarkDirty();
    }

    public string GetCell(int row, int col)
    {
        if (row < 0 || row >= rows.Count || col < 0 || col >= columns.Count) return "";
        return rows[row][col];
    }

    public bool SetCell(int row, int col, string value)
    {
        if (row < 0 || row >= rows.Count || col < 0 || col >= columns.Count) return false;
        rows[row][col] = value ?? "";
        MarkDirty();
        return true;
    }

    public void SelectRow(int index)
    {
        if (index < -1 || index >= rows.Count) return;
        if (SelectedRow == index) return;
        SelectedRow = index;
        MarkDirty();
        SelectedChanged?.Invoke(SelectedRow);
    }

    public void SortByColumn(int col, bool ascending)
    {
        if (col < 0 || col >= columns.Count || rows.Count < 2) return;
        SortedColumn = col;
        SortAscending = ascending;
        rows.Sort((a, b) =>
        {
            int cmp = CompareCell(a[col], b[col]);
            return ascending ? cmp : -cmp;
        });
        MarkDirty();
    }

    private static int CompareCell(string a, string b)
    {
        bool na = TryParseLong(a, out long la);
        bool nb = TryParseLong(b, out long lb);
        if (na && nb) return la.CompareTo(lb);
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseLong(string s, out long value)
    {
        value = 0;
        if (string.IsNullOrEmpty(s)) return false;
        int i = 0;
        bool neg = false;
        if (s[0] == '+' || s[0] == '-') { neg = s[0] == '-'; i = 1; }
        if (i >= s.Length) return false;
        long v = 0;
        for (; i < s.Length; i++)
        {
            if (s[i] < '0' || s[i] > '9') return false;
            v = v * 10 + (s[i] - '0');
        }
        value = neg ? -v : v;
        return true;
    }

    public int GetContentHeight() => HeaderHeight + rows.Count * RowHeight;

    public override void DrawLocal()
    {
        DrawFilledRectangle(Palette.ControlFace, 0, 0, Width, Height);

        int x = 0;
        for (int c = 0; c < columns.Count; c++)
        {
            if (c == SortedColumn)
                DrawFilledRectangle(Palette.Highlight, x, 0, columns[c].Width, HeaderHeight);
            string title = columns[c].Header + (c == SortedColumn ? (SortAscending ? " ^" : " v") : "");
            DrawAlignedText(title, c == SortedColumn ? Palette.HighlightText : Palette.ControlBlack, FontSize,
                new Rectangle(x + 4, 0, Math.Max(0, columns[c].Width - 8), HeaderHeight),
                HorizontalAlignment.Left, VerticalAlignment.Center);
            x += columns[c].Width;
        }
        DrawLine(Palette.ControlShadow, 0, HeaderHeight - 1, Math.Max(0, Width - 1), HeaderHeight - 1);

        for (int r = 0; r < rows.Count; r++)
        {
            int y = HeaderHeight + r * RowHeight;
            if (y >= Height) break;
            if (r == SelectedRow)
                DrawFilledRectangle(Palette.Highlight, 0, y, Width, RowHeight);
            x = 0;
            for (int c = 0; c < columns.Count; c++)
            {
                Color color = r == SelectedRow ? Palette.HighlightText : Palette.ControlBlack;
                DrawAlignedText(rows[r][c], color, FontSize,
                    new Rectangle(x + 4, y, Math.Max(0, columns[c].Width - 8), RowHeight),
                    HorizontalAlignment.Left, VerticalAlignment.Center);
                x += columns[c].Width;
            }
        }
    }

    private int ColumnAt(int localX)
    {
        int x = 0;
        for (int c = 0; c < columns.Count; c++)
        {
            if (localX >= x && localX < x + columns[c].Width) return c;
            x += columns[c].Width;
        }
        return -1;
    }

    private int RowAt(int localY)
    {
        if (localY < HeaderHeight) return -1;
        int r = (localY - HeaderHeight) / Math.Max(1, RowHeight);
        return r >= 0 && r < rows.Count ? r : -1;
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInputEnabled()) return false;
        if (!IsInsideAbsolute(mouseX, mouseY))
        {
            if (mouse.left == MouseEvents.Press) CloseEditor(true);
            return false;
        }

        int lx = mouseX - AbsoluteX;
        int ly = mouseY - AbsoluteY;

        if (mouse.left == MouseEvents.Press)
        {
            pressedRow = RowAt(ly);
            CloseEditor(true);
            return true;
        }

        if (mouse.left == MouseEvents.Release)
        {
            if (ly < HeaderHeight)
            {
                int col = ColumnAt(lx);
                if (Sortable && col >= 0)
                    SortByColumn(col, SortedColumn == col ? !SortAscending : true);
                pressedRow = -1;
                return true;
            }
            int row = RowAt(ly);
            if (row >= 0 && row == pressedRow)
            {
                SelectRow(row);
                int tick = Environment.TickCount;
                if (row == lastEditRow && unchecked(tick - lastEditTick) <= DoubleClickMs && Editable)
                    OpenEditor(row, ColumnAt(lx));
                lastEditRow = row;
                lastEditTick = tick;
            }
            pressedRow = -1;
            return true;
        }

        return true;
    }

    private void OpenEditor(int row, int col)
    {
        if (col < 0) return;
        CloseEditor(true);
        int x = 0;
        for (int c = 0; c < col; c++) x += columns[c].Width;
        int y = HeaderHeight + row * RowHeight;
        editRow = row;
        editCol = col;
        editor = new TextField(x + 1, y + 1, columns[col].Width - 2, RowHeight - 2)
        {
            clampSize = false,
            Margin = new Thickness(0),
            text = rows[row][col],
        };
        editor.TextChanged += OnEditorChanged;
        AddChild(editor);
        MarkDirty();
    }

    private void OnEditorChanged(string text)
    {
        if (editor == null || editRow < 0) return;
        rows[editRow][editCol] = text;
    }

    private void CloseEditor(bool commit)
    {
        if (editor == null) return;
        string text = editor.text;
        int row = editRow, col = editCol;
        RemoveChild(editor);
        editor.Dispose();
        editor = null;
        editRow = -1;
        editCol = -1;
        if (commit && row >= 0 && row < rows.Count && col >= 0 && col < columns.Count)
        {
            rows[row][col] = text;
            CellChanged?.Invoke(row, col, text);
        }
        MarkDirty();
    }

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        if (!IsInputEnabled()) return;
        if (editor != null)
        {
            if (keyEvent.Key == Key.Enter) { CloseEditor(true); return; }
            if (keyEvent.Key == Key.Escape) { CloseEditor(false); return; }
            editor.HandleKeyboard(keyEvent);
            return;
        }
        if (rows.Count == 0) return;
        switch (keyEvent.Key)
        {
            case Key.UpArrow: SelectRow(Math.Max(0, SelectedRow <= 0 ? 0 : SelectedRow - 1)); break;
            case Key.DownArrow: SelectRow(Math.Min(rows.Count - 1, SelectedRow + 1)); break;
            case Key.Home: SelectRow(0); break;
            case Key.End: SelectRow(rows.Count - 1); break;
            case Key.PageUp: SelectRow(Math.Max(0, (SelectedRow < 0 ? rows.Count : SelectedRow) - Math.Max(1, (Height - HeaderHeight) / Math.Max(1, RowHeight)))); break;
            case Key.PageDown: SelectRow(Math.Min(rows.Count - 1, SelectedRow + Math.Max(1, (Height - HeaderHeight) / Math.Max(1, RowHeight)))); break;
        }
    }

    public override string GetComponentName() => "DataGrid";
}
