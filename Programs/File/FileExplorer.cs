using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Windose;

//Control Panel
public class FileExplorer : Window
{
    private DockPanel root;
    private DockPanel explorerBody;
    private MenuBar menuBar;
    private Toolbar toolbar;
    private AddressBar addressBar;
    private StatusBar statusBar;
    private Panel objectCountPanel;
    private Panel selectedPanel;
    private ScrollView treeScroll;
    private ScrollView fileScroll;
    private TreeView tree;
    private ListView files;
    private readonly MenuPopup fileContextMenu;
    private readonly MenuItem openContextItem;
    private readonly MenuItem editContextItem;
    private ListViewItem contextItem;
    private string currentLocation = "desktop";

    public FileExplorer(int x, int y, int width, int height, string title, bool useTitleBar = false) : base(x, y, width, height, title, useTitleBar)
    {
        root = new DockPanel(0, 0, Width, Height)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(28, 2, 2, 2),
            Padding = new Thickness(0),
            useBackground = true,
        };

        menuBar = new MenuBar(0, 0, Width);
        toolbar = new Toolbar(0, 0, Width);
        addressBar = new AddressBar(0, 0, Width);
        statusBar = new StatusBar(0, 0, Width);
        explorerBody = new DockPanel(0, 0, Width, Height)
        {
            clampSize = false,
            useBackground = true,
            backgroundColor = Palette.ControlWhite,
            Padding = new Thickness(0),
        };

        treeScroll = new ScrollView(0, 0, 180, Height)
        {
            showHorizontalScrollbar = false,
            clampSize = false,
            Margin = new Thickness(0),
        };

        tree = new TreeView(0, 0, 180, Height)
        {
            useBackground = true,
            backgroundColor = Palette.ControlWhite,
        };

        Splitter splitter = new Splitter(0, 0, 4, Height)
        {
            orientation = LayoutOrientation.Vertical,
            clampSize = false,
            Margin = new Thickness(0),
        };

        fileScroll = new ScrollView(0, 0, Width, Height)
        {
            showHorizontalScrollbar = true,
            clampSize = false,
            Margin = new Thickness(0),
        };

        files = new ListView(0, 0, Width, Height)
        {
            viewMode = ListViewMode.LargeIcon,
            useBackground = true,
            backgroundColor = Palette.ControlWhite,
            headers = ["Name", "Size", "Type", "Modified"],
            headerWidths = [180, 80, 80, 120]
        };

        //public string nameHeader = "Name";
        //public string sizeHeader = "Size";
        //public string typeHeader = "Type";
        //public string modifiedHeader = "Modified";

        //public int nameColumnWidth = 180;
        //public int sizeColumnWidth = 80;
        //public int typeColumnWidth = 120;
        fileContextMenu = new MenuPopup(160, 24 * 3);
        openContextItem = fileContextMenu.AddItem("Open", OpenContextItem);
        editContextItem = fileContextMenu.AddItem("Edit", EditContextItem);
        fileContextMenu.AddItem("Properties", ShowContextProperties);

        root.AddDockChild(menuBar, Dock.Top);
        root.AddDockChild(toolbar, Dock.Top);
        root.AddDockChild(addressBar, Dock.Top);
        root.AddDockChild(statusBar, Dock.Bottom);
        root.AddDockChild(explorerBody, Dock.Fill);

        treeScroll.SetContent(tree, 180, tree.GetContentHeight());
        fileScroll.SetContent(files, Width, files.GetContentHeight());

        explorerBody.AddDockChild(treeScroll, Dock.Left);
        explorerBody.AddDockChild(splitter, Dock.Left);
        explorerBody.AddDockChild(fileScroll, Dock.Fill);

        MenuPage fileMenu = menuBar.AddMenuPage("File");
        fileMenu.AddItem("Properties", ShowSelectedProperties);
        fileMenu.AddSeparator();
        fileMenu.AddItem("Close", () => WindowManager.PostClose(this));

        MenuPage editMenu = menuBar.AddMenuPage("Edit");
        editMenu.AddItem("Cut").enabled = false;
        editMenu.AddItem("Copy").enabled = false;
        editMenu.AddItem("Paste").enabled = false;
        editMenu.AddSeparator();
        editMenu.AddItem("Delete").enabled = false;

        MenuPage viewMenu = menuBar.AddMenuPage("View");
        viewMenu.AddItem("Large Icons", () => files.SetViewMode(ListViewMode.LargeIcon));
        viewMenu.AddItem("List", () => files.SetViewMode(ListViewMode.List));
        viewMenu.AddItem("Details", () => files.SetViewMode(ListViewMode.Details));
        viewMenu.AddSeparator();
        viewMenu.AddItem("Refresh", () => PopulateFiles(currentLocation));

        MenuPage goMenu = menuBar.AddMenuPage("Go");
        goMenu.AddItem("Desktop", () => NavigateTo("desktop", "Desktop"));
        goMenu.AddItem("My Computer", () => NavigateTo("computer", "My Computer"));
        goMenu.AddItem("My Documents", () => NavigateTo("documents", "My Documents"));

        MenuPage helpMenu = menuBar.AddMenuPage("Help");
        helpMenu.AddItem("Windose File Explorer").enabled = false;

        toolbar.AddButton("Back");
        toolbar.AddButton("Forward");
        toolbar.AddButton("Up");

        toolbar.AddSeparator();
        toolbar.AddButton("Cut");
        toolbar.AddButton("Copy");
        toolbar.AddButton("Paste");

        toolbar.AddSeparator();
        toolbar.AddButton("Undo");

        toolbar.AddSeparator();
        toolbar.AddButton("Delete");
        toolbar.AddButton("Properties", ShowSelectedProperties);

        toolbar.AddSeparator();
        toolbar.AddButton("Icons", () => files.SetViewMode(ListViewMode.LargeIcon), 48);
        toolbar.AddButton("List", () => files.SetViewMode(ListViewMode.List), 48);
        toolbar.AddButton("Details", () => files.SetViewMode(ListViewMode.Details), 64);

        objectCountPanel = statusBar.AddPanel("0 object(s)", 120);
        selectedPanel = statusBar.AddPanel("Selected", 180);

        BuildTree();

        tree.selectedChanged = OpenLocation;

        tree.itemDoubleClick = OpenLocation;



        files.selectedChanged = item =>
        {
            selectedPanel.text = item.text + " selected";
            statusBar.MarkDirty();
            RefreshExplorerVisuals();
        };

        files.itemDoubleClick = item =>
        {
            if (item.isFolder)
                OpenFolderItem(item);
            else
                OpenFileItem(item);
        };

        files.itemRightClick = ShowFileContextMenu;

        OpenLocation(tree.roots[0]);

        AddChild(root);
        files.SetViewMode(ListViewMode.Details);


    }

    private void BuildTree()///TODO
    {
        PopulateFilesystemLocation("/mnt");
    }

    private void OpenLocation(TreeViewItem item)
    {
        if (item == null) return;

        addressBar.Address = item.text;
        PopulateFiles(item.tag as string);
    }

    private void OpenFolderItem(ListViewItem item)
    {
        string path = item.hasFileEntry ? item.fileEntry.AbsoluteLocation : item.tag as string;
        if (path == null) return;

        addressBar.Address = item.text;
        PopulateFiles(path);
    }

    private void OpenFileItem(ListViewItem item)
    {
        string path = item.hasFileEntry ? item.fileEntry.AbsoluteLocation : item.tag as string;
        if (string.IsNullOrEmpty(path)) return;

        BreezeHost.RunFile(path);

    }

    private void ShowFileContextMenu(ListViewItem item, int mouseX, int mouseY)
    {
        contextItem = item;
        openContextItem.enabled = item != null;
        editContextItem.enabled = item != null && !item.isFolder && item.hasFileEntry;

        int x = Math.Min(mouseX, Math.Max(0, Global.screenWidth - fileContextMenu.Width));
        int y = Math.Min(mouseY, Math.Max(0, Global.screenHeight - fileContextMenu.Height));
        fileContextMenu.ShowAt(x, y);
        RefreshExplorerVisuals();
    }

    private void OpenContextItem()
    {
        ListViewItem item = contextItem;
        contextItem = null;
        if (item == null) return;

        if (item.isFolder)
            OpenFolderItem(item);
        else
            OpenFileItem(item);
    }

    private void EditContextItem()
    {
        ListViewItem item = contextItem;
        contextItem = null;
        if (item == null || item.isFolder || !item.hasFileEntry) return;

        string path = item.fileEntry.AbsoluteLocation;
        if (string.IsNullOrEmpty(path)) return;
        WindowManager.Register(new BreezeEditor(X + 40, Y + 40, 900, 620, path));
    }

    private void ShowContextProperties()
    {
        ListViewItem item = contextItem;
        contextItem = null;
        if (item != null && item.hasFileEntry)
            WindowManager.Register(new FileProperties(X + 40, Y + 40, item.fileEntry));
    }

    private void NavigateTo(string location, string displayName)
    {
        addressBar.Address = displayName;
        PopulateFiles(location);
    }

    public void NavigateToPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        addressBar.Address = path;
        PopulateFiles(path);
    }

    private void ShowSelectedProperties()
    {
        if (files.selectedItem != null && files.selectedItem.hasFileEntry)
            WindowManager.Register(new FileProperties(X + 40, Y + 40, files.selectedItem.fileEntry));
    }

    private void PopulateFiles(string location)
    {
        currentLocation = location;
        files.ClearItems();
        selectedPanel.text = "";

        PopulateFilesystemLocation(location);


        objectCountPanel.text = files.items.Count + " object(s)";
        statusBar.MarkDirty();
        fileScroll.RefreshContent(true);
        RefreshExplorerVisuals();
    }

    private void RefreshExplorerVisuals()
    {
        files.MarkDirty(false);
        fileScroll.ForceDirty();
        statusBar.ForceDirty();

        // Explorer contains several cached layout buffers. Redraw the owning
        // window after interaction so those updated buffers reach the screen.
        ForceDirty();
    }

    private void PopulateFilesystemLocation(string location)
    {
        if (!Directory.Exists(location))
        {
            AddFile("Empty Folder", 0, "Folder");
            return;
        }

        string[] directories = Directory.GetDirectories(location);
        foreach (string dir in directories)
        {
            AddFolder(Path.GetDirectoryName(dir), dir);
        }

        string[] filePaths = Directory.GetFiles(location);

        foreach (string filePath in filePaths)
        {
            long size = 0;
            FileEntry entry = new FileEntry(Path.GetFileName(filePath), FileType.File, filePath, size);
            ListViewItem item = files.AddItem(entry);
            item.type = string.Equals(FileSystemManager.GetExtension(filePath), ".breeze", StringComparison.OrdinalIgnoreCase)
                ? "Breeze Script"
                : "File";
        }
    }

    private void PopulateControlPanel()
    {
        const string appletDirectory = @"/mnt/System/ControlPanel";
        if (!Directory.Exists(appletDirectory)) return;

        string[] appletPaths = Directory.GetFiles(appletDirectory);
        for (int i = 0; i < appletPaths.Length; i++)
        {
            string path = appletPaths[i];
            const string extension = ".breeze";
            if (!string.Equals(FileSystemManager.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
                continue;

            string fileName = FileSystemManager.GetName(path);
            string displayName = fileName.Substring(0, fileName.Length - extension.Length);
            FileEntry entry = new FileEntry(displayName, FileType.File, path, 0);
            ListViewItem item = files.AddItem(entry);
            item.type = "Control Panel Applet";
        }
    }

    private ListViewItem AddFolder(string name, string path)
    {
        FileEntry entry = new FileEntry(name, FileType.Directory, path, 0, "");
        ListViewItem item = files.AddItem(entry);
        item.type = "File Folder";
        return item;
    }

    private ListViewItem AddFile(string name, long sizeBytes, string type)
    {
        FileEntry entry = new FileEntry(name, FileType.File, GetChildLocation(currentLocation, name), sizeBytes);
        ListViewItem item = files.AddItem(entry);
        item.type = type;
        return item;
    }
    private string GetChildLocation(string parent, string name)
    {
        if (parent == null || parent == "")
            return name;

        return parent + "/" + name;
    }

    public override void HandleMessage(UiMessage message)
    {
        if (message.Command == "filesystem.changed" &&
            currentLocation != null && (currentLocation.StartsWith("0:", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(currentLocation, "control", StringComparison.OrdinalIgnoreCase)))
            PopulateFiles(currentLocation);
    }

    private void OnFileSystemChanged(FileSystemChange change)
    {
        WindowManager.PostCommand("filesystem.changed", target: this, data: change);
    }

    public override void Dispose()
    {
        fileContextMenu.Hide();
        fileContextMenu.Dispose();
        base.Dispose();
    }
}
