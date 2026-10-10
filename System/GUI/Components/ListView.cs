using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Windose.System.Features;

public class ListView : Component
{
    public List<ListViewItem> items = new List<ListViewItem>();
    public ListViewItem selectedItem;


    public int detailsRowHeight = 20;
    public int headerHeight = 20;
    public int fontSize = 16;
    public bool useBackground = true;

    public List<ListViewColumn> columns = new List<ListViewColumn>();

    public int iconSize = 16;
    private const int IconSlot = 22;


    public Action<ListViewItem> selectedChanged;
    public Action<ListViewItem> itemDoubleClick;
    public Action<ListViewItem, int, int> itemRightClick;
    public Action<int, int> viewportRightClick;

    private int pressedIndex = -1;
    private int lastClickIndex = -1;
    private int lastClickTick;
    public int doubleClickInterval = 1200;


    public ListView(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
    }

    public ListViewItem AddItem(string[] text, Image? icon = null, object? tag = null)
    {
        var item = new ListViewItem(text, icon, tag);
        items.Add(item);
        MarkDirty();
        return item;
    }

    public void AddColumn(string header, int width)
    {
        columns.Add(new ListViewColumn { Header = header, Width = width });
        MarkDirty();
    }

    public void ClearItems()
    {
        items.Clear();
        selectedItem = null;
        MarkDirty();
    }

    public override void DrawLocal()
    {
        if (useBackground)
            DrawFilledRectangle(Palette.ControlWhite, 0, 0, Width, Height);

        DrawHeader();

        for (int i = 0; i < items.Count; i++)
        {
            int y = headerHeight + i * detailsRowHeight;
            if (y >= Height) continue;

            DrawRow(items[i], y);
        }

    }

    private void DrawHeader()
    {
        DrawFilledRectangle(Palette.ControlFace, 0, 0, Width, headerHeight);

        int x = 0;
        int columnIndex = 0;
        foreach (ListViewColumn c in columns)
        {
            DrawSunkenRectangle(x, 0, c.Width, headerHeight);
            int iconOffset = columnIndex == 0 ? IconSlot : 0;
            int textX = x + 4 + iconOffset;
            DrawAlignedText(c.Header, Palette.ControlBlack, fontSize,
                new Rectangle(textX, 0, Math.Max(0, c.Width - iconOffset - 8), headerHeight),
                HorizontalAlignment.Left, VerticalAlignment.Center);
            x += c.Width;
            columnIndex++;
        }
    }



    private void DrawRow(ListViewItem item, int y)
    {
        bool selected = item == selectedItem;

        if (selected)
            DrawFilledRectangle(Palette.Highlight, 2, y + 1, Width - 4, detailsRowHeight - 2);

        Color color = selected ? Palette.HighlightText : Palette.ControlBlack;

        if (item.Icon != null)
            DrawImageStretch(item.Icon, new Rectangle(4, y + (detailsRowHeight - iconSize) / 2, iconSize, iconSize));

        int x = 0;
        for (int c = 0; c < columns.Count; c++)
        {
            string s = c < item.Text.Length ? item.Text[c] : "";
            int textX = x + 4 + (c == 0 ? IconSlot : 0);
            int avail = columns[c].Width - (textX - x) - 4;

            DrawAlignedText(s, color, fontSize,
                new Rectangle(textX, y, Math.Max(0, avail), detailsRowHeight),
                HorizontalAlignment.Left, VerticalAlignment.Center);
            x += columns[c].Width;
        }
    }

    private string Clip(string s, int pixelWidth)
    {
        int glyph = Math.Max(1, fontSize / 2);   // same 8px-at-16 assumption as before; swap in a real measure if you have one
        int max = Math.Max(0, pixelWidth / glyph);

        return s.Length <= max ? s : s.Substring(0, max);
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInsideAbsolute(mouseX, mouseY))
            return false;

        int index = GetItemIndexAt(mouseX - AbsoluteX, mouseY - AbsoluteY);

        if (mouse.right == MouseEvents.Release)
        {
            if (index >= 0)
            {
                SelectItem(items[index]);
                itemRightClick?.Invoke(items[index], mouseX, mouseY);
                return true;
            }

            viewportRightClick?.Invoke(mouseX, mouseY);
            return true;

        }

        switch (mouse.left)
        {
            case MouseEvents.Press:
                pressedIndex = index;
                return true;

            case MouseEvents.Release:
                if (index >= 0 && index == pressedIndex)
                    ActivateItem(index);

                pressedIndex = -1;
                return true;
        }

        return true;
    }

    private void ActivateItem(int index)
    {
        SelectItem(items[index]);

        int tick = Environment.TickCount;
        int elapsed = unchecked(tick - lastClickTick);
        if (index == lastClickIndex && elapsed >= 0 && elapsed <= doubleClickInterval)
        {
            itemDoubleClick?.Invoke(items[index]);

            lastClickIndex = -1;
            lastClickTick = 0;
            return;
        }

        lastClickIndex = index;
        lastClickTick = tick;
    }

    public void SelectItem(ListViewItem item)
    {
        selectedItem = item;

        selectedChanged?.Invoke(selectedItem);
        MarkDirty();
    }

    public int GetItemIndexAt(int localX, int localY)
    {

        if (localY < headerHeight) return -1;
        int detailsIndex = (localY - headerHeight) / detailsRowHeight;
        return detailsIndex >= 0 && detailsIndex < items.Count ? detailsIndex : -1;

    }

    public int GetContentHeight()
    {
        return Math.Max(headerHeight + detailsRowHeight, headerHeight + items.Count * detailsRowHeight);
    }

    public override bool IsOpaqueForCopy() => useBackground;

    public override string GetComponentName() => "ListView";
}
public class ListViewColumn
{
    public string Header;
    public int Width;
}
