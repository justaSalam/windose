using Cosmos.Kernel.System.Graphics;
using System.Globalization;

public sealed class RegistryEditor : Window
{
    private readonly TreeView keyTree;
    private readonly ListView valueList;
    private readonly ScrollView treeScroll;
    private readonly ScrollView valueScroll;
    private readonly AddressBar addressBar;
    private readonly Panel status;
    private string selectedPath = "";

    public RegistryEditor(int x = 150, int y = 100, int width = 850, int height = 540)
        : base(x, y, width, height, "Registry Editor", true)
    {
        DockPanel root = new DockPanel(0, 0, Width, Height)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(28, 2, 2, 2),
            Padding = new Thickness(0),
            useBackground = true,
            backgroundColor = Palette.ControlFace,
        };

        MenuBar menuBar = new MenuBar(0, 0, Width);
        MenuPage fileMenu = menuBar.AddMenuPage("File");
        fileMenu.AddItem("Exit", () => WindowManager.PostClose(this));

        MenuPage editMenu = menuBar.AddMenuPage("Edit");
        editMenu.AddItem("New String", () => CreateValue(RegistryValueKind.String));
        editMenu.AddItem("New Integer", () => CreateValue(RegistryValueKind.Integer));
        editMenu.AddItem("New Number", () => CreateValue(RegistryValueKind.Number));
        editMenu.AddItem("New Boolean", () => CreateValue(RegistryValueKind.Boolean));
        editMenu.AddSeparator();
        editMenu.AddItem("Modify", EditSelected);
        editMenu.AddItem("Delete", DeleteSelected);

        MenuPage viewMenu = menuBar.AddMenuPage("View");
        viewMenu.AddItem("Refresh", RefreshAll);

        Toolbar toolbar = new Toolbar(0, 0, Width);
        toolbar.AddButton("New String", () => CreateValue(RegistryValueKind.String));
        toolbar.AddButton("New Integer", () => CreateValue(RegistryValueKind.Integer));
        toolbar.AddButton("New Boolean", () => CreateValue(RegistryValueKind.Boolean));
        toolbar.AddSeparator();
        toolbar.AddButton("Modify", EditSelected);
        toolbar.AddButton("Delete", DeleteSelected);
        toolbar.AddButton("Refresh", RefreshAll);

        addressBar = new AddressBar(0, 0, Width);
        addressBar.label.text = "Key";

        StatusBar statusBar = new StatusBar(0, 0, Width);
        status = statusBar.AddPanel("Ready", Math.Max(180, width - 20));

        DockPanel body = new DockPanel(0, 0, Width, Height)
        {
            clampSize = false,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            useBackground = true,
            backgroundColor = Palette.ControlWhite,
        };

        keyTree = new TreeView(0, 0, 250, Height)
        {
            useBackground = true,
            backgroundColor = Palette.ControlWhite,
        };
        treeScroll = new ScrollView(0, 0, 250, Height)
        {
            showHorizontalScrollbar = true,
            clampSize = false,
            Margin = new Thickness(0),
        };
        treeScroll.SetContent(keyTree, 250, keyTree.GetContentHeight());

        Splitter splitter = new Splitter(0, 0, 4, Height)
        {
            orientation = LayoutOrientation.Vertical,
            clampSize = false,
            Margin = new Thickness(0),
        };

        valueList = new ListView(0, 0, Width, Height)
        {
            viewMode = ListViewMode.Details,
            useBackground = true,
            backgroundColor = Palette.ControlWhite,

            headers = ["Name", "Type", "Data", "Restart"],
            headerWidths = [180, 100, 100, 360]
            //nameHeader = "Name",
            //sizeHeader = "Type",
            //typeHeader = "Data",
            //modifiedHeader = "Restart",
            //nameColumnWidth = 180,
            //sizeColumnWidth = 100,
            //typeColumnWidth = 360,
        };
        valueScroll = new ScrollView(0, 0, Width, Height)
        {
            showHorizontalScrollbar = true,
            clampSize = false,
            Margin = new Thickness(0),
        };
        valueScroll.SetContent(valueList, Math.Max(600, Width), valueList.GetContentHeight());

        body.AddDockChild(treeScroll, Dock.Left);
        body.AddDockChild(splitter, Dock.Left);
        body.AddDockChild(valueScroll, Dock.Fill);

        root.AddDockChild(menuBar, Dock.Top);
        root.AddDockChild(toolbar, Dock.Top);
        root.AddDockChild(addressBar, Dock.Top);
        root.AddDockChild(statusBar, Dock.Bottom);
        root.AddDockChild(body, Dock.Fill);
        AddChild(root);

        keyTree.selectedChanged = SelectKey;
        valueList.selectedChanged = SelectValue;
        valueList.itemDoubleClick = item => EditSelected();
        Registry.Changed += OnRegistryChanged;

        RefreshAll();
    }

    private void RefreshAll()
    {
        string pathToRestore = selectedPath;
        keyTree.ClearItems();
        TreeViewItem computer = keyTree.AddRoot("Computer", "");
        List<string> keys = Registry.GetKeys();

        for (int i = 0; i < keys.Count; i++)
        {
            string[] segments = keys[i].Split('/');
            TreeViewItem parent = computer;
            string path = "";
            for (int segment = 0; segment < segments.Length - 1; segment++)
            {
                path = path == "" ? segments[segment] : path + "/" + segments[segment];
                parent = FindOrAddChild(parent, segments[segment], path);
            }
        }

        TreeViewItem selection = FindTreeItem(computer, pathToRestore) ?? computer;
        keyTree.SelectItem(selection);
        treeScroll.MarkDirty();
        ForceDirty();
    }

    private void SelectKey(TreeViewItem item)
    {
        selectedPath = item?.tag as string ?? "";
        addressBar.Address = selectedPath == "" ? "Computer" : "Computer/" + selectedPath;
        PopulateValues();
    }

    private void PopulateValues()
    {
        valueList.ClearItems();
        List<string> keys = Registry.GetKeys(selectedPath);
        for (int i = 0; i < keys.Count; i++)
        {
            if (!string.Equals(GetParentKey(keys[i]), selectedPath, StringComparison.OrdinalIgnoreCase)) continue;
            RegistryEntry entry = Registry.GetEntry(keys[i]);
            if (entry == null) continue;

            ListViewItem item = valueList.AddItem(GetLeafName(entry.Key), tag: entry);
            item.icon = new Png("/mnt/System/Icons/regedit_binary.png");
            item.size = GetValueType(entry.Value);
            item.type = FormatValue(entry.Value);
            item.modified = entry.RequiresRestart ? "Yes" : "No";
        }

        status.text = valueList.items.Count + " value(s)";
        status.MarkDirty();
        valueScroll.MarkDirty();
        ForceDirty();
    }

    private void SelectValue(ListViewItem item)
    {
        if (item?.tag is not RegistryEntry entry) return;
        status.text = entry.Key + (entry.IsBuiltIn ? " (system)" : " (custom)");
        status.MarkDirty();
        ForceDirty();
    }

    private void CreateValue(RegistryValueKind kind)
    {
        WindowManager.Register(new RegistryValueDialog(selectedPath, kind, () => RefreshAll(), X + 80, Y + 80));
    }

    private void EditSelected()
    {
        if (valueList.selectedItem?.tag is not RegistryEntry entry)
        {
            status.text = "Select a value to modify.";
            status.MarkDirty();
            return;
        }
        WindowManager.Register(new RegistryValueDialog(entry, () => RefreshAll(), X + 80, Y + 80));
    }

    private void DeleteSelected()
    {
        if (valueList.selectedItem?.tag is not RegistryEntry entry)
        {
            status.text = "Select a value to delete.";
            status.MarkDirty();
            return;
        }
        WindowManager.Register(new RegistryDeleteDialog(entry, () => RefreshAll(), X + 100, Y + 100));
    }

    private void OnRegistryChanged(RegistryChange change)
    {
        RefreshAll();
    }

    public override void Dispose()
    {
        Registry.Changed -= OnRegistryChanged;
        base.Dispose();
    }

    private static TreeViewItem FindOrAddChild(TreeViewItem parent, string text, string path)
    {
        for (int i = 0; i < parent.children.Count; i++)
            if (string.Equals(parent.children[i].text, text, StringComparison.OrdinalIgnoreCase)) return parent.children[i];
        return parent.AddChild(text, path);
    }

    private static TreeViewItem FindTreeItem(TreeViewItem item, string path)
    {
        if (string.Equals(item.tag as string ?? "", path ?? "", StringComparison.OrdinalIgnoreCase)) return item;
        for (int i = 0; i < item.children.Count; i++)
        {
            TreeViewItem found = FindTreeItem(item.children[i], path);
            if (found != null) return found;
        }
        return null;
    }

    internal static string GetParentKey(string key)
    {
        int separator = key.LastIndexOf('/');
        return separator < 0 ? "" : key.Substring(0, separator);
    }

    internal static string GetLeafName(string key)
    {
        int separator = key.LastIndexOf('/');
        return separator < 0 ? key : key.Substring(separator + 1);
    }

    internal static string GetValueType(object value)
    {
        if (value == null) return "Null";
        if (value is bool) return "Boolean";
        if (value is long || value is int) return "Integer";
        if (value is double || value is float) return "Number";
        return "String";
    }

    internal static string FormatValue(object value)
    {
        if (value == null) return "(null)";
        if (value is bool boolean) return boolean ? "true" : "false";
        if (value is double number) return number.ToString(CultureInfo.InvariantCulture);
        return value.ToString();
    }
}

public enum RegistryValueKind
{
    String,
    Integer,
    Number,
    Boolean,
}
