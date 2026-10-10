using Cosmos.Kernel.System.Graphics;
using System.Drawing;

public class TreeView : Component
{
    private const int ExpandBoxSize = 9;
    private const int IconSize = 16;
    private const int IconOffset = 12;

    public List<TreeViewItem> roots = new List<TreeViewItem>();
    public TreeViewItem selectedItem;

    public int rowHeight = 18;
    public int indentWidth = 16;
    public int fontSize = 16;
    public bool useBackground = true;
    public Color backgroundColor = Palette.ControlWhite;
    public Color textColor = Palette.ControlBlack;

    public Action<TreeViewItem> selectedChanged;
    public Action<TreeViewItem> itemDoubleClick;
    public Action<TreeViewItem> itemRightClick;

    private int pressedRow = -1;
    private TreeViewItem pressedItem;
    private int lastClickRow = -1;
    private int lastClickTick;
    public int doubleClickInterval = 1200;

    public TreeView(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
    }

    public TreeViewItem AddRoot(string text, object tag = null)
    {
        TreeViewItem item = new TreeViewItem(text, tag);
        roots.Add(item);
        MarkDirty();
        return item;
    }

    public TreeViewItem AddRoot(string text, string image, object tag = null)
    {
        TreeViewItem item = new TreeViewItem(text, tag)
        {
            image = new Png(image)
        };
        roots.Add(item);
        MarkDirty();
        return item;
    }

    public void ClearItems()
    {
        roots.Clear();
        selectedItem = null;
        MarkDirty();
    }

    public override void Draw()
    {
        base.Draw();
    }

    public override void DrawLocal()
    {
        if (useBackground)
            DrawFilledRectangle(backgroundColor, 0, 0, Width, Height);

        int row = 0;

        for (int i = 0; i < roots.Count; i++)
            DrawItem(roots[i], 0, ref row);
    }

    private void DrawItem(TreeViewItem item, int depth, ref int row)
    {
        int y = row * rowHeight;
        if (y >= Height) return;

        int x = 4 + depth * indentWidth;
        bool selected = item == selectedItem || item.selected;

        if (item.HasChildren())
            DrawExpandBox(item, x, y);

        if (selected)
        {
            DrawFilledRectangle(Palette.Highlight, x + IconOffset, y + 1, Math.Max(1, Width - x - IconOffset - 2), rowHeight - 2);
        }

        int textX = x + 18;
        if (item.image != null)
        {
            DrawImageStretch(item.image, new Rectangle(x + IconOffset, y + Math.Max(0, (rowHeight - IconSize) / 2), IconSize, IconSize));
            textX += IconOffset + IconSize + 4 - 18;
        }

        DrawAlignedText(item.text, selected ? Palette.HighlightText : textColor, fontSize,
            new Rectangle(textX, y, Math.Max(0, Width - textX - Padding.right), rowHeight),
            HorizontalAlignment.Left, VerticalAlignment.Center);

        row++;

        if (!item.expanded)
            return;

        for (int i = 0; i < item.children.Count; i++)
            DrawItem(item.children[i], depth + 1, ref row);
    }

    private void DrawExpandBox(TreeViewItem item, int x, int y)
    {
        int boxX = x;
        int boxY = y + Math.Max(0, (rowHeight - ExpandBoxSize) / 2);

        DrawFilledRectangle(Palette.ControlWhite, boxX, boxY, ExpandBoxSize, ExpandBoxSize);
        DrawRectangle(Palette.ControlShadow, boxX, boxY, ExpandBoxSize, ExpandBoxSize);

        DrawLine(Palette.ControlBlack, boxX + 2, boxY + 4, boxX + 6, boxY + 4);

        if (!item.expanded)
            DrawLine(Palette.ControlBlack, boxX + 4, boxY + 2, boxX + 4, boxY + 6);
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInsideAbsolute(mouseX, mouseY))
            return false;

        int localY = mouseY - AbsoluteY;
        int row = localY / Math.Max(1, rowHeight);
        TreeViewItem item = GetVisibleItem(row);

        switch (mouse.left)
        {
            case MouseEvents.Press:
                pressedRow = row;
                pressedItem = item;
                return true;

            case MouseEvents.Release:
                if (item != null && item == pressedItem && row == pressedRow)
                {
                    ActivateItem(item, row, mouseX - AbsoluteX);
                }

                pressedRow = -1;
                pressedItem = null;
                return true;
        }

        if (mouse.right == MouseEvents.Release)
        {
            if (item != null && item == pressedItem && row == pressedRow)
            {
                ActivateItem(item, row, mouseX - AbsoluteX);
            }
            itemRightClick?.Invoke(item);
        }

        return true;
    }

    private void ActivateItem(TreeViewItem item, int row, int localX)
    {
        int depth = GetDepth(item);
        int expandX = 4 + depth * indentWidth;

        if (item.HasChildren() && localX >= expandX && localX < expandX + ExpandBoxSize)
        {
            item.expanded = !item.expanded;
            MarkDirty();
            return;
        }

        SelectItem(item);

        int tick = Environment.TickCount;
        int elapsed = unchecked(tick - lastClickTick);
        if (row == lastClickRow && elapsed >= 0 && elapsed <= doubleClickInterval)
        {
            if (item.HasChildren())
                item.expanded = !item.expanded;

            itemDoubleClick?.Invoke(item);
            MarkDirty();
            lastClickRow = -1;
            lastClickTick = 0;
            return;
        }

        lastClickRow = row;
        lastClickTick = tick;
    }

    public void SelectItem(TreeViewItem item)
    {
        if (selectedItem != null)
            selectedItem.selected = false;

        selectedItem = item;

        if (selectedItem != null)
            selectedItem.selected = true;

        selectedChanged?.Invoke(selectedItem);
        MarkDirty();
    }

    public TreeViewItem GetVisibleItem(int targetRow)
    {
        int row = 0;

        for (int i = 0; i < roots.Count; i++)
        {
            TreeViewItem item = GetVisibleItem(roots[i], targetRow, ref row);
            if (item != null)
                return item;
        }

        return null;
    }

    public int GetVisibleItemCount()
    {
        int count = 0;

        for (int i = 0; i < roots.Count; i++)
            CountVisibleItems(roots[i], ref count);

        return count;
    }

    public int GetContentHeight()
    {
        return Math.Max(rowHeight, GetVisibleItemCount() * rowHeight);
    }

    private void CountVisibleItems(TreeViewItem item, ref int count)
    {
        count++;

        if (!item.expanded)
            return;

        for (int i = 0; i < item.children.Count; i++)
            CountVisibleItems(item.children[i], ref count);
    }

    private TreeViewItem GetVisibleItem(TreeViewItem item, int targetRow, ref int row)
    {
        if (row == targetRow)
            return item;

        row++;

        if (!item.expanded)
            return null;

        for (int i = 0; i < item.children.Count; i++)
        {
            TreeViewItem match = GetVisibleItem(item.children[i], targetRow, ref row);
            if (match != null)
                return match;
        }

        return null;
    }

    private int GetDepth(TreeViewItem item)
    {
        int depth = 0;
        TreeViewItem current = item.parent;

        while (current != null)
        {
            depth++;
            current = current.parent;
        }

        return depth;
    }

    public override bool IsOpaqueForCopy() => useBackground;

    public override string GetComponentName() => "TreeView";
}
