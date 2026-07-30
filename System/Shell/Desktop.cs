using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Mouse;
using System.Drawing;
using System.Globalization;
using Windose;
using Windose.System.Shell;
using static Cosmos.Kernel.System.Graphics.Fonts.PCScreenFont;

public class Desktop : Component
{
    private Color backgroundColor;

    private MenuPopup contextMenu;
    private DesktopIcon ?activeDraggedIcon;
    private List<DesktopIcon> selectedIcons = new List<DesktopIcon>();
    private List<DesktopIcon> gridCollisionIgnoredIcons;

    public static List<DesktopIcon> Icons = new List<DesktopIcon>();
    public static bool SnapIconsToGrid { get; private set; } = true;
    public static bool ShowIconGrid { get; private set; }
    public static int IconGridWidth { get; private set; } = 80;
    public static int IconGridHeight { get; private set; } = 80;
    public static int IconGridOffsetX { get; private set; } = 8;
    public static int IconGridOffsetY { get; private set; } = 8;
    public int contextX { get; private set; }
    public int contextY { get; private set; }
    public Desktop(int x, int y, int width, int height) : base(x, y, width, height)
    {
        zLayer = DrawLayer.Desktop;

        ApplyRegistryBackground();
        ApplyIconGridSettings();

        SystemRegistry.Changed += OnRegistryChanged;

        contextMenu = new MenuPopup(260, 24 * 3)
        {
            itemHeight = 20
        };

        rightClickAction = () =>
        {
            contextX = Math.Min(MouseManager.X, Math.Max(0, Global.screenWidth - contextMenu.Width));
            contextY = Math.Min(MouseManager.Y, Math.Max(0, Global.screenHeight - contextMenu.Height));
            contextMenu.ShowAt(contextX, contextY);

            MarkDirty();
        };


        contextMenu.AddItem("Refresh");

        MenuItem viewItem = contextMenu.AddItem("View");
        viewItem.AddSubmenuItem("Large Icons");
        viewItem.AddSubmenuItem("Medium Icons");
        viewItem.AddSubmenuItem("Small Icons");
        viewItem.AddSubmenuSeparator();
        viewItem.AddSubmenuItem("Show Icon Grid", ToggleIconGrid);
        viewItem.AddSubmenuItem("Snap Icons to Grid", ToggleSnapToGrid);
        viewItem.AddSubmenuItem("Align Icons to Grid", ArrangeIconsOnGrid);
        viewItem.AddSubmenuSeparator();
        viewItem.AddSubmenuItem("Compact Grid", () => SetIconGridPreset(80, 76));
        viewItem.AddSubmenuItem("Comfortable Grid", () => SetIconGridPreset(88, 84));
        viewItem.AddSubmenuItem("Wide Grid", () => SetIconGridPreset(104, 92));
        viewItem.AddSubmenuSeparator();
        viewItem.AddSubmenuItem("Toggle Icons");

        contextMenu.AddSeparator();
        contextMenu.AddItem("Paste");
        contextMenu.AddSeparator();

        MenuItem newItem = contextMenu.AddItem("New");
        newItem.AddSubmenuItem("File", () => DesktopNewFile("txt"));
        newItem.AddSubmenuItem("Folder", DesktopNewDirectory);
        newItem.AddSubmenuSeparator();
        newItem.AddSubmenuItem("Breeze Application", () => DesktopNewFile("breeze"));


        contextMenu.AddSeparator();
        contextMenu.AddItem("Display Settings");
        contextMenu.AddItem("Personalise");
    }

    private void DesktopNewDirectory()
    {
        string path = FileSystemManager.GetUniquePath("/mnt/user/desktop/", "New Directory");

        Directory.CreateDirectory(path);
        AddIcon(new DesktopIcon(contextX, contextY, new FileEntry(Path.GetDirectoryName(path), FileType.Directory, path, 0)));
    }


    private void DesktopNewFile(string extension)
    {
        string path = FileSystemManager.GetUniquePath("/mnt/user/desktop/", "New File", extension);

        File.Create(path);
        AddIcon(new DesktopIcon(contextX, contextY, new FileEntry(Path.GetFileName(path), FileType.File, path, 0)));
    }

    private Dictionary<string, DesktopLayoutEntry> LoadLayout()
    {
        var layout = new Dictionary<string, DesktopLayoutEntry>();

        const string layoutFile = "/mnt/user/desktop/.desktop.layout";

        if (!File.Exists(layoutFile))
            return layout;

        foreach (string line in File.ReadAllLines(layoutFile))
        {
            string[] parts = line.Split('|');

            if (parts.Length != 3)
                continue;

            layout[parts[0]] = new DesktopLayoutEntry
            {
                Path = parts[0],
                X = int.Parse(parts[1]),
                Y = int.Parse(parts[2])
            };
        }

        return layout;
    }

    private void LoadIcons()
    {
        Dictionary<string, DesktopLayoutEntry> layout = LoadLayout();

        foreach (string path in Directory.GetFileSystemEntries("/mnt/user/desktop"))
        {
            if (Path.GetFileName(path) == ".desktop.layout")
                continue;

            int x = 0;
            int y = 0;

            if (layout.TryGetValue(path, out var entry))
            {
                x = entry.X;
                y = entry.Y;
            }

            //AddIcon(new DesktopIcon(x, y, new FileEntry(path)));
        }
    }

    private void SaveLayout()
    {
        List<string> lines = new();

        foreach (DesktopIcon icon in Icons)
        {
            lines.Add($"{icon.fileEntry.AbsoluteLocation}|{icon.X}|{icon.Y}");
        }

        File.WriteAllLines("/mnt/user/desktop/.desktop.layout", lines);
    }

    public override void Update()
    {
        // The desktop is a background layer; the compositor handles redraw dependencies.
    }

    public void AddIcon(DesktopIcon icon)
    {
        if (!Icons.Contains(icon))
        {
            AddChild(icon);
            Icons.Add(icon);
            PlaceIconOnGrid(icon);
            icon.MarkDirty();

        }
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (activeDraggedIcon != null)
        {
            int previousX = activeDraggedIcon.X;
            int previousY = activeDraggedIcon.Y;

            activeDraggedIcon.HandleInput(mouseX, mouseY, mouse);

            MoveSelectedIconsWith(activeDraggedIcon, activeDraggedIcon.X - previousX, activeDraggedIcon.Y - previousY);

            if (mouse.left == MouseEvents.Release || mouse.left == MouseEvents.None)
            {
                if (IsIconSelected(activeDraggedIcon))
                    PlaceSelectedIconsOnGrid();
                else
                    PlaceIconOnGrid(activeDraggedIcon);

                activeDraggedIcon = null;
            }

            return true;
        }

        if (mouse.left == MouseEvents.Press)
        {
            for (int i = Icons.Count - 1; i >= 0; i--)
            {
                DesktopIcon icon = Icons[i];
                if (!icon.Visible || !icon.IsInsideAbsolute(mouseX, mouseY)) continue;

                bool extendSelection = KeyboardManager.ControlPressed || KeyboardManager.ShiftPressed;

                if (extendSelection)
                {
                    ToggleIconSelection(icon);
                    if (!IsIconSelected(icon))
                        return true;
                }
                else if (!IsIconSelected(icon))
                {
                    SelectOnlyIcon(icon);
                }

                activeDraggedIcon = icon;
                return icon.HandleInput(mouseX, mouseY, mouse);
            }

            ClearIconSelection();
        }

        return base.HandleInput(mouseX, mouseY, mouse);
    }

    public override void DrawLocal()
    {
        DrawImageStretchAlpha(Wallpapers.Lithium, new Rectangle(0, 0, (int)Wallpapers.Lithium.Width, (int)Wallpapers.Lithium.Height), new Rectangle(0, 0, Width, Height));

        if (ShowIconGrid)
            DrawIconGrid();

        foreach (DesktopIcon icon in Icons)
        {
            if (!icon.Visible) continue;
            DrawChild(icon);
        }

    }

    private void OnRegistryChanged(RegistryChange change)
    {
        bool backgroundChanged = change.Key.Equals("System/Desktop/BackgroundColor", StringComparison.OrdinalIgnoreCase) ||
            change.Key.Equals("System/Theme/Name", StringComparison.OrdinalIgnoreCase);
        bool gridChanged = change.Key.StartsWith("System/Desktop/IconGrid", StringComparison.OrdinalIgnoreCase);

        if (!backgroundChanged && !gridChanged) return;

        if (backgroundChanged)
            ApplyRegistryBackground();

        if (gridChanged)
        {
            ApplyIconGridSettings();
            if (SnapIconsToGrid)
                ArrangeIconsOnGrid();
        }

        ForceDirty();
    }

    private void ApplyRegistryBackground()
    {
        string value = SystemRegistry.GetString("System/Desktop/BackgroundColor", "theme");
        backgroundColor = IsThemeBackground(value)
            ? Palette.DesktopBackground
            : TryParseColor(value, out Color color) ? color : Palette.DesktopBackground;
    }

    private static bool IsThemeBackground(string value)
    {
        return string.IsNullOrWhiteSpace(value) ||
            value.Trim().Equals("theme", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseColor(string value, out Color color)
    {
        string hex = (value ?? "").Trim().TrimStart('#');
        if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            color = Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            return true;
        }
        color = Color.Empty;
        return false;
    }

    private void ApplyIconGridSettings()
    {
        SnapIconsToGrid = SystemRegistry.GetBoolean("System/Desktop/IconGridEnabled", true);
        ShowIconGrid = SystemRegistry.GetBoolean("System/Desktop/IconGridVisible", false);
        IconGridWidth = ClampGridValue(SystemRegistry.GetInteger("System/Desktop/IconGridWidth", 80), DesktopIcon.DefaultWidth, 256);
        IconGridHeight = ClampGridValue(SystemRegistry.GetInteger("System/Desktop/IconGridHeight", 80), DesktopIcon.DefaultHeight, 256);
        IconGridOffsetX = ClampGridValue(SystemRegistry.GetInteger("System/Desktop/IconGridOffsetX", 8), 0, Math.Max(0, Width - 1));
        IconGridOffsetY = ClampGridValue(SystemRegistry.GetInteger("System/Desktop/IconGridOffsetY", 8), 0, Math.Max(0, Height - 1));
    }

    private static int ClampGridValue(long value, int minimum, int maximum)
    {
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return (int)value;
    }

    private void ToggleIconGrid()
    {
        SystemRegistry.Set("System/Desktop/IconGridVisible", !ShowIconGrid);
    }

    private void ToggleSnapToGrid()
    {
        SystemRegistry.Set("System/Desktop/IconGridEnabled", !SnapIconsToGrid);
    }

    private void DrawIconGrid()
    {
        int usableHeight = GetUsableHeight();
        Color dotColor = Color.FromArgb(100, 100, 100);

        for (int y = IconGridOffsetY; y < usableHeight; y += IconGridHeight)
        {
            for (int x = IconGridOffsetX; x < Width; x += IconGridWidth)
            {
                DrawFilledRectangle(dotColor, x, y, 2, 2);
            }
        }
    }

    private void SetIconGridPreset(int width, int height)
    {
        SystemRegistry.Set("System/Desktop/IconGridWidth", (long)width);
        SystemRegistry.Set("System/Desktop/IconGridHeight", (long)height);
    }

    private void SelectOnlyIcon(DesktopIcon icon)
    {
        if (selectedIcons.Count == 1 && selectedIcons[0] == icon) return;

        ClearIconSelection();
        AddIconSelection(icon);
    }

    private void ToggleIconSelection(DesktopIcon icon)
    {
        if (IsIconSelected(icon))
            RemoveIconSelection(icon);
        else
            AddIconSelection(icon);
    }

    private void AddIconSelection(DesktopIcon icon)
    {
        if (icon == null || selectedIcons.Contains(icon)) return;

        selectedIcons.Add(icon);
        icon.Set(true);
    }

    private void RemoveIconSelection(DesktopIcon icon)
    {
        if (icon == null || !selectedIcons.Remove(icon)) return;

        icon.Set(false);
    }

    private void ClearIconSelection()
    {
        for (int i = 0; i < selectedIcons.Count; i++)
            selectedIcons[i].Set(false);

        selectedIcons.Clear();
    }

    private bool IsIconSelected(DesktopIcon icon)
    {
        return selectedIcons.Contains(icon);
    }

    private void MoveSelectedIconsWith(DesktopIcon movedIcon, int deltaX, int deltaY)
    {
        if (deltaX == 0 && deltaY == 0) return;
        if (!IsIconSelected(movedIcon)) return;

        for (int i = 0; i < selectedIcons.Count; i++)
        {
            DesktopIcon icon = selectedIcons[i];
            if (icon == movedIcon) continue;

            icon.MoveTo(icon.X + deltaX, icon.Y + deltaY);
        }
    }

    private void PlaceSelectedIconsOnGrid()
    {
        if (!SnapIconsToGrid) return;

        gridCollisionIgnoredIcons = new List<DesktopIcon>(selectedIcons);
        try
        {
            for (int i = 0; i < selectedIcons.Count; i++)
            {
                DesktopIcon icon = selectedIcons[i];
                PlaceIconOnGrid(icon);
                gridCollisionIgnoredIcons.Remove(icon);
            }
        }
        finally
        {
            gridCollisionIgnoredIcons = null;
        }
    }

    private void ArrangeIconsOnGrid()
    {
        for (int i = 0; i < Icons.Count; i++)
            PlaceIconOnGrid(Icons[i]);
    }

    private void PlaceIconOnGrid(DesktopIcon icon)
    {
        if (icon == null || !SnapIconsToGrid) return;

        Point snapped = SnapToGrid(icon.X, icon.Y, icon);
        Point available = FindNearestAvailableGridPosition(icon, snapped);
        icon.MoveTo(available.X, available.Y);
    }

    private Point SnapToGrid(int x, int y, DesktopIcon icon)
    {
        int maxX = Math.Max(IconGridOffsetX, Width - icon.Width);
        int maxY = Math.Max(IconGridOffsetY, GetUsableHeight() - icon.Height);

        int column = (int)Math.Round((x - IconGridOffsetX) / (double)IconGridWidth);
        int row = (int)Math.Round((y - IconGridOffsetY) / (double)IconGridHeight);

        int snappedX = IconGridOffsetX + column * IconGridWidth;
        int snappedY = IconGridOffsetY + row * IconGridHeight;

        return new Point(
            Math.Max(IconGridOffsetX, Math.Min(maxX, snappedX)),
            Math.Max(IconGridOffsetY, Math.Min(maxY, snappedY)));
    }

    private Point FindNearestAvailableGridPosition(DesktopIcon draggedIcon, Point desired)
    {
        int maxColumn = Math.Max(0, (Width - draggedIcon.Width - IconGridOffsetX) / IconGridWidth);
        int maxRow = Math.Max(0, (GetUsableHeight() - draggedIcon.Height - IconGridOffsetY) / IconGridHeight);
        int desiredColumn = Math.Max(0, Math.Min(maxColumn, (desired.X - IconGridOffsetX) / IconGridWidth));
        int desiredRow = Math.Max(0, Math.Min(maxRow, (desired.Y - IconGridOffsetY) / IconGridHeight));
        int maxRadius = Math.Max(maxColumn, maxRow) + 1;

        for (int radius = 0; radius <= maxRadius; radius++)
        {
            for (int row = desiredRow - radius; row <= desiredRow + radius; row++)
            {
                for (int column = desiredColumn - radius; column <= desiredColumn + radius; column++)
                {
                    if (row < 0 || row > maxRow || column < 0 || column > maxColumn) continue;
                    if (Math.Max(Math.Abs(column - desiredColumn), Math.Abs(row - desiredRow)) != radius) continue;
                    if (IsGridCellOccupied(column, row, draggedIcon)) continue;

                    return new Point(IconGridOffsetX + column * IconGridWidth, IconGridOffsetY + row * IconGridHeight);
                }
            }
        }

        return desired;
    }

    private bool IsGridCellOccupied(int column, int row, DesktopIcon ignoredIcon)
    {
        for (int i = 0; i < Icons.Count; i++)
        {
            DesktopIcon icon = Icons[i];
            if (icon == ignoredIcon || !icon.Visible) continue;
            if (gridCollisionIgnoredIcons != null && gridCollisionIgnoredIcons.Contains(icon)) continue;

            Point snapped = SnapToGrid(icon.X, icon.Y, icon);
            int iconColumn = (snapped.X - IconGridOffsetX) / IconGridWidth;
            int iconRow = (snapped.Y - IconGridOffsetY) / IconGridHeight;

            if (iconColumn == column && iconRow == row)
                return true;
        }

        return false;
    }

    private int GetUsableHeight()
    {
        return Explorer.taskbar != null ? Explorer.taskbar.Y : Height;
    }

    public override void Dispose()
    {
        SystemRegistry.Changed -= OnRegistryChanged;
        base.Dispose();
    }

}
public class DesktopLayoutEntry
{
    public string Path { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
}
