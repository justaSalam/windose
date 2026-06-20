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
        };

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
        fileMenu.AddItem("Properties", () => ShowSelectedProperties());
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
        toolbar.AddButton("Properties", () =>
        {
            ShowSelectedProperties();
        });//

        toolbar.AddSeparator();
        toolbar.AddButton("Icons", () => files.SetViewMode(ListViewMode.LargeIcon), 48);
        toolbar.AddButton("List", () => files.SetViewMode(ListViewMode.List), 48);
        toolbar.AddButton("Details", () => files.SetViewMode(ListViewMode.Details), 64);

        objectCountPanel = statusBar.AddPanel("0 object(s)", 120);
        selectedPanel = statusBar.AddPanel("Selected", 180);

        BuildTree();

        tree.selectedChanged = item =>
        {
            OpenLocation(item);
        };

        tree.itemDoubleClick = item =>
        {
            OpenLocation(item);
        };



        files.selectedChanged = item =>
        {
            selectedPanel.text = item.text + " selected";
            statusBar.MarkDirty();
        };

        files.itemDoubleClick = item =>
        {
            if (item.isFolder)
                OpenFolderItem(item);
        };

        OpenLocation(tree.roots[0]);

        AddChild(root);
        files.SetViewMode(ListViewMode.Details);
    }

    private void BuildTree()
    {
        TreeViewItem desktop = tree.AddRoot("Desktop", "desktop");
        TreeViewItem computer = desktop.AddChild("My Computer", "computer");
        TreeViewItem documents = desktop.AddChild("My Documents", "documents");
        TreeViewItem cDrive = computer.AddChild("Windose (C:)", "c");
        TreeViewItem floppyDrive = computer.AddChild("3.5 Floppy (A:)", "a");
        TreeViewItem controlPanel = computer.AddChild("Control Panel", "control");

        TreeViewItem system = cDrive.AddChild("System", "c/system");
        system.AddChild("Config", "c/system/config");
        system.AddChild("Drivers", "c/system/drivers");
        cDrive.AddChild("Programs", "c/programs");
        cDrive.AddChild("Users", "c/users");

        documents.AddChild("Letters", "documents/letters");
        documents.AddChild("Pictures", "documents/pictures");

        floppyDrive.expanded = false;
        controlPanel.expanded = false;
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

    private void NavigateTo(string location, string displayName)
    {
        addressBar.Address = displayName;
        PopulateFiles(location);
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

        switch (location)
        {
            case "desktop":
                AddFolder("My Computer", "computer");
                AddFolder("My Documents", "documents");
                AddFolder("Recycle Bin", "recycle");
                break;

            case "computer":
                AddFolder("Windose (C:)", "c");
                AddFolder("3.5 Floppy (A:)", "a");
                AddFolder("Control Panel", "control");
                break;

            case "c":
                AddFolder("System", "c/system");
                AddFolder("Programs", "c/programs");
                AddFolder("Users", "c/users");
                AddFile("AUTOEXEC.BAT", 1024, "Batch File");
                AddFile("CONFIG.SYS", 1024, "System File");
                break;

            case "c/system":
                AddFolder("Config", "c/system/config");
                AddFolder("Drivers", "c/system/drivers");
                AddFile("kernel.sys", 80 * 1024, "System File");
                AddFile("shell.dll", 32 * 1024, "Application Extension");
                break;

            case "c/system/config":
                AddFile("display.ini", 2 * 1024, "Configuration Settings");
                AddFile("mouse.ini", 1024, "Configuration Settings");
                AddFile("keyboard.ini", 1024, "Configuration Settings");
                break;

            case "c/system/drivers":
                AddFile("vga.drv", 18 * 1024, "Device Driver");
                AddFile("mouse.drv", 12 * 1024, "Device Driver");
                AddFile("keyboard.drv", 10 * 1024, "Device Driver");
                break;

            case "documents":
                AddFolder("Letters", "documents/letters");
                AddFolder("Pictures", "documents/pictures");
                AddFile("notes.txt", 4 * 1024, "Text Document");
                break;

            case "documents/letters":
                AddFile("hello.txt", 2 * 1024, "Text Document");
                AddFile("todo.txt", 1024, "Text Document");
                break;

            case "documents/pictures":
                AddFile("clouds.bmp", 42 * 1024, "Bitmap Image");
                AddFile("setup.bmp", 64 * 1024, "Bitmap Image");
                break;

            case "control":
                AddFolder("Display", "control/display");
                AddFolder("Keyboard", "control/keyboard");
                AddFolder("Mouse", "control/mouse");
                AddFolder("System", "control/system");
                break;

            default:
                AddFile("Empty Folder", 0, "Folder");
                break;
        }

        objectCountPanel.text = files.items.Count + " object(s)";
        statusBar.MarkDirty();
        fileScroll.RefreshContent(true);
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
}
