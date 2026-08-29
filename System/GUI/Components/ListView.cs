using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Mono.Cecil;
using Windose.System.Kernel;

public class ListView : Component
{
    public List<ListViewItem> items = new List<ListViewItem>();
    public ListViewItem selectedItem;
    public ListViewMode viewMode = ListViewMode.LargeIcon;

    public int largeCellWidth = 92;
    public int largeCellHeight = 76;
    public int smallRowHeight = 20;
    public int detailsRowHeight = 20;
    public int headerHeight = 20;
    public int iconSize = 32;
    public int smallIconSize = 12;
    public int fontSize = 16;
    public bool useBackground = true;
    public Color backgroundColor = Palette.ControlWhite;
    public Color textColor = Palette.ControlBlack;
    public string[] headers;
    public int[] headerWidths;

    //public string nameHeader = "Name";
    //public string sizeHeader = "Size";
    //public string typeHeader = "Type";
    //public string modifiedHeader = "Modified";

    //public int nameColumnWidth = 180;
    //public int sizeColumnWidth = 80;
    //public int typeColumnWidth = 120;

    public Action<ListViewItem> selectedChanged;
    public Action<ListViewItem> itemDoubleClick;
    public Action<ListViewItem, int, int> itemRightClick;
    public Action<int,int> viewportRightClick;

    private int pressedIndex = -1;
    private int lastClickIndex = -1;
    private int lastClickTick;
    public int doubleClickInterval = 1200;
    private Png itemIcon;

    private Png folderIcon;
    private Rectangle sourceRect;
    private Rectangle destRect;




    public ListView(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
        itemIcon = new Png("/mnt/System/Icons/file_lines.png");
        folderIcon = new Png("/mnt/System/Icons/directory_closed.png");
    }

    public ListViewItem AddItem(string text, Image icon = null, object tag = null)
    {
        ListViewItem item = new ListViewItem(text, icon, tag);
        items.Add(item);
        MarkDirty();
        return item;
    }

    public ListViewItem AddItem(FileEntry fileEntry, Image icon = null)
    {
        ListViewItem item = new ListViewItem(fileEntry, icon);
        items.Add(item);
        MarkDirty();
        return item;
    }

    public ListViewItem AddFolder(string text, Bitmap icon = null, object tag = null)
    {
        ListViewItem item = AddItem(text, icon, tag);
        item.isFolder = true;
        item.type = "File Folder";
        return item;
    }

    public void ClearItems()
    {
        items.Clear();
        selectedItem = null;
        MarkDirty();
    }

    public void SetViewMode(ListViewMode mode)
    {
        if (viewMode == mode) return;

        viewMode = mode;
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

        switch (viewMode)
        {
            case ListViewMode.LargeIcon:
                DrawLargeIcons();
                break;

            case ListViewMode.SmallIcon:
                DrawSmallIcons();
                break;

            case ListViewMode.List:
                DrawList();
                break;

            case ListViewMode.Details:
                DrawDetails();
                break;
        }
    }

    private void DrawLargeIcons()
    {
        int columns = Math.Max(1, Width / Math.Max(1, largeCellWidth));

        for (int i = 0; i < items.Count; i++)
        {
            int column = i % columns;
            int row = i / columns;
            int x = column * largeCellWidth;
            int y = row * largeCellHeight;

            if (y >= Height) continue;

            DrawItemLarge(items[i], x, y, largeCellWidth, largeCellHeight);
        }
    }

    private void DrawItemLarge(ListViewItem item, int x, int y, int width, int height)
    {
        int iconX = x + (width - iconSize) / 2;
        int iconY = y + 6;
        int textWidth = Math.Max(1, width - 6);
        int textX = x + 3;
        int textY = y + iconSize + 12;

        DrawItemIcon(item, iconX, iconY, iconSize);

        if (item == selectedItem || item.selected)
        {
            DrawFilledRectangle(Palette.Highlight, textX, textY - 1, textWidth, fontSize + 2);
            DrawCenteredText(item.text, Palette.HighlightText, textX, textY, textWidth, fontSize);
        }
        else
        {
            DrawCenteredText(item.text, textColor, textX, textY, textWidth, fontSize);
        }
    }

    private void DrawSmallIcons()
    {
        int rowHeight = smallRowHeight;
        int columns = Math.Max(1, Width / 180);

        for (int i = 0; i < items.Count; i++)
        {
            int column = i % columns;
            int row = i / columns;
            int x = column * 180 + 4;
            int y = row * rowHeight;

            DrawItemRow(items[i], x, y, 176, rowHeight, true);
        }
    }

    private void DrawList()
    {
        for (int i = 0; i < items.Count; i++)
        {
            int y = i * smallRowHeight;
            DrawItemRow(items[i], 4, y, Width - 8, smallRowHeight, true);
        }
    }

    private void DrawDetails()
    {
        DrawDetailsHeader();

        for (int i = 0; i < items.Count; i++)
        {
            int y = headerHeight + i * detailsRowHeight;
            if (y >= Height) continue;

            DrawDetailsRow(items[i], y);
        }
    }

    private void DrawDetailsHeader()
    {
        DrawFilledRectangle(Palette.ControlFace, 0, 0, Width, headerHeight);

        int currentX = 0;
        for (int i = 0; i < headerWidths.Length; i++)
        {

            DrawSunkenRectangle(currentX, 0, headerWidths[i], headerHeight);
            DrawString(headers[i], Palette.ControlBlack, currentX + 4, 2, fontSize);
            currentX += headerWidths[i];
        }

        //int sizeX = nameColumnWidth;
        //int typeX = sizeX + sizeColumnWidth;
        //int modifiedX = typeX + typeColumnWidth;
        //DrawSunkenRectangle(0, 0, nameColumnWidth, headerHeight);
        //DrawSunkenRectangle(sizeX, 0, sizeColumnWidth, headerHeight);
        //DrawSunkenRectangle(typeX, 0, typeColumnWidth, headerHeight);
        //DrawSunkenRectangle(modifiedX, 0, Math.Max(1, Width - modifiedX), headerHeight);

        //DrawString(nameHeader, Palette.ControlBlack, 4, 2, fontSize);
        //DrawString(sizeHeader, Palette.ControlBlack, sizeX + 4, 2, fontSize);
        //DrawString(typeHeader, Palette.ControlBlack, typeX + 4, 2, fontSize);
        //DrawString(modifiedHeader, Palette.ControlBlack, modifiedX + 4, 2, fontSize);
    }



    private void DrawDetailsRow(ListViewItem item, int y)
    {
        bool selected = item == selectedItem || item.selected;

        if (selected)
            DrawFilledRectangle(Palette.Highlight, 2, y + 1, Width - 4, detailsRowHeight - 2);

        Color color = selected ? Palette.HighlightText : textColor;

        //DrawItemIcon(item, 4, y + 2, smallIconSize);


        destRect = new Rectangle(4, y + 2, smallIconSize, smallIconSize);

        string ext = Path.GetExtension(item.fileEntry.AbsoluteLocation);


        //DrawImageStretchAlpha(new Png(IconRegistry.Get(ext)), new Rectangle(4, y + 2, 32, 32), destRect);//TODO Fix image resizing + Add smaller icons ig 
        DrawString(item.text, color, 24, y + 2, fontSize);

        //int sizeX = nameColumnWidth;
        //int typeX = sizeX + sizeColumnWidth;
        //int modifiedX = typeX + typeColumnWidth;

        int currentX = 0;
        for (int i = 0; i < headerWidths.Length; i++)
        {
            DrawString(headers[i], Palette.ControlBlack, currentX + 4, 2, fontSize);
            currentX += headerWidths[i];
        }

        //DrawString(item.size, color, sizeX + 4, y + 2, fontSize);
        //DrawString(item.type, color, typeX + 4, y + 2, fontSize);
        //DrawString(item.modified, color, modifiedX + 4, y + 2, fontSize);
    }

    private void DrawItemRow(ListViewItem item, int x, int y, int width, int height, bool drawIcon)
    {
        if (y >= Height) return;

        bool selected = item == selectedItem || item.selected;

        if (selected)
            DrawFilledRectangle(Palette.Highlight, x, y + 1, width, height - 2);

        if (drawIcon)
            DrawItemIcon(item, x + 2, y + 2, smallIconSize);

        DrawString(item.text, selected ? Palette.HighlightText : textColor, x + 24, y + 2, fontSize);
    }

    private void DrawItemIcon(ListViewItem item, int x, int y, int size)
    {
        if (item.icon != null)
        {
            buffer.DrawImageAlpha(item.icon, x, y);
            return;
        }

        if (item.isFolder)
            DrawFolderIcon(x, y, size);
        else
            DrawFileIcon(x, y, size);
    }

    private void DrawFolderIcon(int x, int y, int size)
    {
        DrawImage(folderIcon, x, y);


        /*int tabHeight = Math.Max(3, size / 4);
        int bodyY = y + tabHeight;

        DrawFilledRectangle(Color.FromArgb(255, 224, 64), x + 2, y + 2, size / 2, tabHeight);
        DrawRectangle(Palette.ControlShadow, x + 2, y + 2, size / 2, tabHeight);
        DrawFilledRectangle(Color.FromArgb(255, 240, 96), x, bodyY, size, size - tabHeight);
        DrawRectangle(Palette.ControlShadow, x, bodyY, size, size - tabHeight);*/
    }

    public override void MarkDirty(bool invalidate = true)
    {
        base.MarkDirty(invalidate);
    }

    private void DrawFileIcon(int x, int y, int size)
    {
        DrawImage(itemIcon, x, y);
        /*
        DrawFilledRectangle(Palette.ControlWhite, x + 4, y, size - 7, size);
        DrawRectangle(Palette.ControlShadow, x + 4, y, size - 7, size);
        DrawLine(Palette.ControlShadow, x + size - 7, y, x + size - 2, y + 5);
        DrawLine(Palette.ControlShadow, x + size - 2, y + 5, x + size - 2, y + size - 1);
        DrawLine(Palette.ControlShadow, x + 8, y + 9, x + size - 5, y + 9);
        DrawLine(Palette.ControlShadow, x + 8, y + 13, x + size - 5, y + 13);*/
    }

    private void DrawCenteredText(string text, Color color, int x, int y, int width, int size)
    {
        int textWidth = text.Length * 8;
        int textX = x + Math.Max(0, (width - textWidth) / 2);
        DrawString(text, color, textX, y, size);
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
        if (selectedItem != null)
            selectedItem.selected = false;

        selectedItem = item;

        if (selectedItem != null)
            selectedItem.selected = true;

        selectedChanged?.Invoke(selectedItem);
        MarkDirty();
    }

    public int GetItemIndexAt(int localX, int localY)
    {
        switch (viewMode)
        {
            case ListViewMode.LargeIcon:
                int columns = Math.Max(1, Width / Math.Max(1, largeCellWidth));
                int column = localX / largeCellWidth;
                int row = localY / largeCellHeight;
                int index = row * columns + column;
                return index >= 0 && index < items.Count ? index : -1;

            case ListViewMode.SmallIcon:
                int smallColumns = Math.Max(1, Width / 180);
                int smallColumn = localX / 180;
                int smallRow = localY / smallRowHeight;
                int smallIndex = smallRow * smallColumns + smallColumn;
                return smallIndex >= 0 && smallIndex < items.Count ? smallIndex : -1;

            case ListViewMode.List:
                int listIndex = localY / smallRowHeight;
                return listIndex >= 0 && listIndex < items.Count ? listIndex : -1;

            case ListViewMode.Details:
                if (localY < headerHeight) return -1;
                int detailsIndex = (localY - headerHeight) / detailsRowHeight;
                return detailsIndex >= 0 && detailsIndex < items.Count ? detailsIndex : -1;
        }

        return -1;
    }

    public int GetContentHeight()
    {
        switch (viewMode)
        {
            case ListViewMode.LargeIcon:
                int columns = Math.Max(1, Width / Math.Max(1, largeCellWidth));
                int rows = (items.Count + columns - 1) / columns;
                return Math.Max(largeCellHeight, rows * largeCellHeight);

            case ListViewMode.SmallIcon:
                int smallColumns = Math.Max(1, Width / 180);
                int smallRows = (items.Count + smallColumns - 1) / smallColumns;
                return Math.Max(smallRowHeight, smallRows * smallRowHeight);

            case ListViewMode.List:
                return Math.Max(smallRowHeight, items.Count * smallRowHeight);

            case ListViewMode.Details:
                return Math.Max(headerHeight + detailsRowHeight, headerHeight + items.Count * detailsRowHeight);
        }

        return Height;
    }

    public override bool IsOpaqueForCopy() => useBackground;

    public override string GetComponentName() => "ListView";
}
