using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.System.FileSystem;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using Cosmos.Kernel.System.Storage;
using System.Drawing;
using Windose;
using Windose.Programs;
using Windose.System.Kernel;
using Windose.System.Kernel.FileSystem;
using Windose.System.Kernel.Subsystem;
using Windose.System.System_Calls;

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
    private FileListView files;
    private readonly MenuPopup fileContextMenu;
    private readonly MenuPopup viewportContextMenu;
    private readonly MenuItem openContextItem;
    private readonly MenuItem openWithContextItem;
    private readonly MenuItem editContextItem;
    private FileListViewItem? contextItem;
    private string currentLocation = "/mnt";
    private readonly List<string> backStack = new List<string>();
    private readonly List<string> forwardStack = new List<string>();

    public FileExplorer(int x, int y, int width, int height, string title, string rootPath = "/mnt") : base(x, y, width, height, title, true)
    {
        root = new DockPanel(0, 0, ClientWidth, ClientHeight)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            useBackground = true,
        };
        menuBar = new MenuBar(0, 0, Width);
        toolbar = new Toolbar(0, 0, Width);
        addressBar = new AddressBar(0, 0, Width);
        addressBar.dropDownButton.leftClickAction = ShowHistoryPopup;
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

        //TODO Tree item right click (proper version)
        tree.itemRightClick += ctx => viewportContextMenu.ShowAt(MouseManager.X, MouseManager.Y);

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

        files = new FileListView(0, 0, Width, Height)
        {
            viewMode = ListViewMode.LargeIcon,
            useBackground = true,
            backgroundColor = Palette.ControlWhite,
            headers = ["Name", "Size", "Type", "Modified"],
            headerWidths = [180, 80, 80, 120],
            useRightClick = true
        };

        fileContextMenu = new MenuPopup(160, 24 * 3);
        viewportContextMenu = new MenuPopup(160, 24 * 3);

        MenuItem viewContext = viewportContextMenu.AddItem("View");
        viewContext.AddSubmenuItem("Icons", () => files.SetViewMode(ListViewMode.LargeIcon));
        viewContext.AddSubmenuItem("List", () => files.SetViewMode(ListViewMode.List));
        viewContext.AddSubmenuItem("Details", () => files.SetViewMode(ListViewMode.Details));

        MenuItem sortContext = viewportContextMenu.AddItem("Sort");
        sortContext.AddSubmenuItem("Name");
        sortContext.AddSubmenuItem("Date");
        sortContext.AddSubmenuItem("Type");
        sortContext.AddSubmenuItem("Size");
        sortContext.AddSubmenuSeparator();
        sortContext.AddSubmenuItem("Ascending");
        sortContext.AddSubmenuItem("Descending");

        viewportContextMenu.AddSeparator();

        viewportContextMenu.AddItem("Paste", PasteClipboard);
        viewportContextMenu.AddSeparator();

        viewportContextMenu.AddItem("Refresh", Refresh);
        viewportContextMenu.AddSeparator();

        MenuItem newFileContext = viewportContextMenu.AddItem("New");
        newFileContext.AddSubmenuItem("File", NewFileFromContext);
        newFileContext.AddSubmenuItem("Folder", NewDirectoryFromContext);
        newFileContext.AddSubmenuSeparator();
        newFileContext.AddSubmenuItem("Shortcut", NewShortcutFromContext);

        viewportContextMenu.AddSeparator();
        viewportContextMenu.AddItem("Properties", ShowContextProperties);

        openContextItem = fileContextMenu.AddItem("Open", OpenContextItem);
        openWithContextItem = fileContextMenu.AddItem("Open With...", OpenWithContextItem);
        editContextItem = fileContextMenu.AddItem("Edit", EditContextItem);

        fileContextMenu.AddSeparator();
        fileContextMenu.AddItem("Cut", CutSelectedFile);
        fileContextMenu.AddItem("Copy", CopySelectedFile);

        fileContextMenu.AddSeparator();
        fileContextMenu.AddItem("Create Shortcut", CreateShortcutForSelectedItem);
        fileContextMenu.AddItem("Delete", DeleteFile);
        fileContextMenu.AddItem("Rename", RenameSelectedItem);

        fileContextMenu.AddSeparator();
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
        editMenu.AddItem("Cut", CutSelectedFile);
        editMenu.AddItem("Copy", CopySelectedFile);
        editMenu.AddItem("Paste", PasteClipboard);
        editMenu.AddSeparator();
        editMenu.AddItem("Delete", DeleteFileForSelected);
        editMenu.AddItem("Select All", () => files.SelectAll());

        MenuPage viewMenu = menuBar.AddMenuPage("View");
        viewMenu.AddItem("Large Icons", () => files.SetViewMode(ListViewMode.LargeIcon));
        viewMenu.AddItem("List", () => files.SetViewMode(ListViewMode.List));
        viewMenu.AddItem("Details", () => files.SetViewMode(ListViewMode.Details));
        viewMenu.AddSeparator();
        viewMenu.AddItem("Refresh", Refresh);

        MenuPage goMenu = menuBar.AddMenuPage("Go");
        goMenu.AddItem("Desktop", () => NavigateTo("desktop", "Desktop"));
        goMenu.AddItem("My Computer", () => NavigateTo("computer", "My Computer"));
        goMenu.AddItem("My Documents", () => NavigateTo("documents", "My Documents"));

        MenuPage helpMenu = menuBar.AddMenuPage("Help");
        helpMenu.AddItem("Windose File Explorer").enabled = false;

        toolbar.AddButton("Back", GoBack, "Back");
        toolbar.AddButton("Forward", GoForward, "Forward");
        toolbar.AddButton("Up", GoUp, "Up one level");

        toolbar.AddSeparator();
        toolbar.AddButton("Cut", CutSelectedFile, "Cut");
        toolbar.AddButton("Copy", CopySelectedFile, "Copy");
        toolbar.AddButton("Paste", PasteClipboard, "Paste");

        toolbar.AddSeparator();
        toolbar.AddButton("Undo", null, "Undo");

        toolbar.AddSeparator();
        toolbar.AddButton("Delete", () => DeleteFileForSelected(), "Delete");
        toolbar.AddButton("Properties", ShowSelectedProperties, "Properties");

        toolbar.AddSeparator();
        toolbar.AddButton("Icons", () => files.SetViewMode(ListViewMode.LargeIcon));
        toolbar.AddButton("List", () => files.SetViewMode(ListViewMode.List));
        toolbar.AddButton("Details", () => files.SetViewMode(ListViewMode.Details));

        objectCountPanel = statusBar.AddPanel("0 object(s)", 120);
        selectedPanel = statusBar.AddPanel("Selected", 180);

        BuildTree(rootPath);

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
        files.viewportRightClick = viewportContextMenu.ShowAt;
        files.itemDelete = (item) => DeleteFileForSelected();
        files.itemCopy = (item) => CopySelectedFile();
        files.itemCut = (item) => CutSelectedFile();
        files.itemPaste = () => PasteClipboard();
        files.itemRename = (item) => RenameSelectedItem();

        Content = root;
        files.SetViewMode(ListViewMode.LargeIcon);
    }

    private void CutSelectedFile()
    {
        if (files.selectedItem != null && files.selectedItem.hasFileEntry)
        {
            FileClipboard.SetCut(new[] { files.selectedItem.fileEntry.AbsoluteLocation });
        }
    }

    private void CopySelectedFile()
    {
        if (files.selectedItem != null && files.selectedItem.hasFileEntry)
        {
            FileClipboard.SetCopy(new[] { files.selectedItem.fileEntry.AbsoluteLocation });
        }
    }

    private void PasteClipboard()
    {
        if (FileClipboard.HasContent && Directory.Exists(currentLocation))
        {
            FileClipboard.PasteTo(currentLocation);
            Refresh();
        }
    }

    private void RenameSelectedItem()
    {
        if (files.selectedItem != null && files.selectedItem.hasFileEntry)
        {
            string path = files.selectedItem.fileEntry.AbsoluteLocation;
            LaunchTracker.Start(() => new RenameDialog(path, Refresh));
        }
    }

    private void NewFileFromContext()
    {
        File.Create(FileSystemManager.GetUniquePath(currentLocation, "New File", ".txt"));
        Refresh();
    }

    private void NewShortcutFromContext()
    {
        LaunchTracker.Start(() => new CreateShortcutDialog(currentLocation));
    }

    private void CreateShortcutForSelectedItem()
    {
        if (IsItemValid(out FileListViewItem item) && item.hasFileEntry)
        {
            string targetPath = item.fileEntry.AbsoluteLocation;
            LaunchTracker.Start(() => new CreateShortcutDialog(currentLocation, targetPath));
        }
    }

    private void NewDirectoryFromContext()
    {
        Directory.CreateDirectory(FileSystemManager.GetUniquePath(currentLocation, "New Directory"));

        Refresh();
    }

    private void DeleteFileForSelected()
    {
        if (files.selectedItem != null && files.selectedItem.hasFileEntry)
        {
            TrashManager.MoveToTrash(files.selectedItem.fileEntry.AbsoluteLocation);
            Refresh();
        }
    }

    private void DeleteFile()
    {
        if (!IsItemValid(out FileListViewItem item))
        {
            return;
        }

        FileEntry fileEntry = item.fileEntry;
        TrashManager.MoveToTrash(fileEntry.AbsoluteLocation);

        Refresh();

    }

    private const string DriveImage = "imageres.dll,-32";

    private void BuildTree(in string path)
    {
        tree.ClearItems();

        PopulateFilesystemLocation(path);

        for (int i = 0; i < StorageManager.DeviceCount; i++)
        {
            IBlockDevice? device = StorageManager.GetDevice(i);
            if (device == null)
            {
                continue;
            }

            IReadOnlyList<Partition> partitions = StorageManager.GetPartitions(device);

            foreach (Partition partition in partitions)
            {

                VfsMount? mount = DiskManagement.GetMount(partition);
                if (mount == null)
                {
                    continue;
                }

                string mPoint = mount.MountPoint;

                TreeViewItem root = tree.AddRoot(mPoint, DriveImage, mPoint);
                PopulateTreeItem(root);
            }


        }
    }

    private void PopulateTreeItem(TreeViewItem item)
    {
        if (item == null) return;

        string path = (string)item.tag;

        if (string.IsNullOrEmpty(path)) return;
        item.children.Clear();

        try
        {
            foreach (string dir in Directory.GetDirectories(path))
            {
                if (Path.GetFileName(dir) == ".trash") continue;
                TreeViewItem childItem = item.AddChild(Path.GetFileName(dir), "imageres.dll,-3", dir);
                PopulateTreeItem(childItem);
            }
        }
        catch (Exception ex)
        {
            SystemLogger.WriteLine("FileExplorer", $"Access denied reading {path}: {ex.Message}", ConsoleMessageType.Error);
            SystemLogger.Dump();
        }
    }

    private void OpenLocation(TreeViewItem item)
    {
        if (item == null) return;

        string path = (string)item.tag;

        PushHistory(path);
        addressBar.Address = path;
        PopulateFiles(path);
        Refresh();
    }

    private void OpenFolderItem(FileListViewItem item)
    {
        string path = item.hasFileEntry ? item.fileEntry.AbsoluteLocation : (string)item.tag;
        if (path == null) return;

        PushHistory(path);
        addressBar.Address = item.text;
        PopulateFiles(path);
        Refresh();

    }



    private void OpenFileItem(FileListViewItem item)
    {
        string path = item.hasFileEntry ? item.fileEntry.AbsoluteLocation : (string)item.tag;
        OpenFilePath(path);
    }

    public static void OpenFilePath(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".elf")
        {
            try
            {
                Windose.System.Kernel.LaunchTracker.StartElf(path, new string[] { path });
            }
            catch (Exception ex)
            {
                SystemLogger.WriteLine("ELF", $" Error launching {path}: {ex.Message}", ConsoleMessageType.Error);
            }
            return;
        }

        if (FileAssociations.Launch(path))
        {
            return;
        }

        SystemLogger.WriteLine("FileExplorer", $"No associated application for {ext}", ConsoleMessageType.Warning);
    }

    private void ShowFileContextMenu(FileListViewItem item, int mouseX, int mouseY)
    {
        contextItem = item;
        openContextItem.enabled = item != null;
        openWithContextItem.enabled = item != null && !item.isFolder && item.hasFileEntry;
        editContextItem.enabled = item != null && !item.isFolder && item.hasFileEntry;

        int x = Math.Min(mouseX, Math.Max(0, Global.screenWidth - fileContextMenu.Width));
        int y = Math.Min(mouseY, Math.Max(0, Global.screenHeight - fileContextMenu.Height));
        fileContextMenu.ShowAt(x, y);
        RefreshExplorerVisuals();
    }



    private void OpenContextItem()
    {
        FileListViewItem item = contextItem;
        contextItem = null;
        if (item == null) return;

        if (item.isFolder)
            OpenFolderItem(item);
        else
            OpenFileItem(item);
    }

    private void OpenWithContextItem()
    {
        FileListViewItem item = contextItem;
        contextItem = null;
        if (item == null || item.isFolder || !item.hasFileEntry) return;

        string path = item.fileEntry.AbsoluteLocation;
        if (string.IsNullOrEmpty(path)) return;
        LaunchTracker.Start(() => new OpenWithDialog(X + 60, Y + 60, path));
    }

    private void EditContextItem()
    {
        FileListViewItem item = contextItem;
        contextItem = null;
        if (item == null || item.isFolder || !item.hasFileEntry) return;

        string path = item.fileEntry.AbsoluteLocation;
        if (string.IsNullOrEmpty(path)) return;
        LaunchTracker.Start(() => new BreezeEditor(X + 40, Y + 40, 900, 620, path));
    }

    private void ShowContextProperties()
    {
        if (!IsItemValid(out FileListViewItem item)) return;
        LaunchTracker.Start(() => new FileProperties(X + 40, Y + 40, item.fileEntry));
    }
    private bool IsItemValid(out FileListViewItem item)
    {
        item = contextItem;
        contextItem = null;

        return item != null && item.hasFileEntry;
    }
    private void NavigateTo(string location, string displayName)
    {
        PushHistory(location);
        addressBar.Address = displayName;
        PopulateFiles(location);
    }

    private void PushHistory(string location)
    {
        if (location == currentLocation) return;
        backStack.Add(currentLocation);
        if (backStack.Count > 50) backStack.RemoveAt(0);
        forwardStack.Clear();
    }

    private void GoBack()
    {
        if (backStack.Count == 0) return;
        forwardStack.Add(currentLocation);
        string location = backStack[backStack.Count - 1];
        backStack.RemoveAt(backStack.Count - 1);
        addressBar.Address = location;
        PopulateFiles(location);
    }

    private void GoForward()
    {
        if (forwardStack.Count == 0) return;
        backStack.Add(currentLocation);
        string location = forwardStack[forwardStack.Count - 1];
        forwardStack.RemoveAt(forwardStack.Count - 1);
        addressBar.Address = location;
        PopulateFiles(location);
    }

    private void GoUp()
    {
        string parent = FileSystemManager.GetParent(currentLocation);
        if (string.IsNullOrEmpty(parent)) return;
        PushHistory(parent);
        addressBar.Address = parent;
        PopulateFiles(parent);
    }

    private void ShowHistoryPopup()
    {
        MenuPopup history = new MenuPopup(280, 24);
        for (int i = backStack.Count - 1; i >= 0; i--)
        {
            string location = backStack[i];
            history.AddItem(location, () =>
            {
                forwardStack.Add(currentLocation);
                addressBar.Address = location;
                PopulateFiles(location);
            });
        }
        history.ShowAt(MouseManager.X, MouseManager.Y);
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
            LaunchTracker.Start(() => new FileProperties(X + 40, Y + 40, files.selectedItem.fileEntry));
    }

    private void Refresh()
    {
        BuildTree("/mnt");
        PopulateFiles(currentLocation);

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
        addressBar.Address = currentLocation;
        // Explorer contains several cached layout buffers. Redraw the explorer after interaction so those updated buffers reach the screen.
        ForceDirty();
    }

    private void PopulateFilesystemLocation(string location)
    {
        if (!Directory.Exists(location))
        {
            AddFile("Empty Folder", 0, "Folder");
            return;
        }

        try
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(location);

            foreach (DirectoryInfo dir in directoryInfo.GetDirectories())
            {
                if (dir.Name == ".trash") continue;
                AddFolder(dir.Name, dir.FullName);
            }

            FileInfo[] fileInfo = directoryInfo.GetFiles();

            foreach (FileInfo file in fileInfo)
            {
                FileEntry entry = new FileEntry(file.Name, FileType.File, file.FullName, file.Length);

                if (!entry.displayInExplorer)
                {
                    continue;
                }
                if (file.Name.EndsWith(".origpath", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                FileListViewItem item = files.AddItem(entry);

                item.type = FileAssociations.GetFileTypeDescription(file.FullName);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            files.ClearItems();
            FileListViewItem item = files.AddItem("Access denied");
            item.type = "Folder";
            SystemLogger.WriteLine("FileExplorer", $"Access denied reading {location}: {ex.Message}", ConsoleMessageType.Warning);
        }
    }

    private void PopulateControlPanel()
    {
        const string appletDirectory = "/mnt/System/ControlPanel";
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
            FileListViewItem item = files.AddItem(entry);
            item.type = "Control Panel Applet";
        }
    }

    private FileListViewItem AddFolder(string name, string path)
    {
        FileEntry entry = new FileEntry(name, FileType.Directory, path, 0, "");
        FileListViewItem item = files.AddItem(entry);
        item.type = "File Folder";
        return item;
    }

    private FileListViewItem AddFile(string name, long sizeBytes, string type)
    {
        FileEntry entry = new FileEntry(name, FileType.File, GetChildLocation(currentLocation, name), sizeBytes);
        FileListViewItem item = files.AddItem(entry);
        item.type = type;
        return item;
    }
    private string GetChildLocation(string parent, string name)
    {
        if (parent == null || parent == "")
            return name;

        return parent + "/" + name;
    }

    private void OnFileSystemChanged(FileSystemChange change)
    {
        WindowManager.RunOnUI(() =>
        {
            if (currentLocation != null
                && (currentLocation.StartsWith("/mnt", StringComparison.OrdinalIgnoreCase)
                || string.Equals(currentLocation, "control", StringComparison.OrdinalIgnoreCase)))
            {
                PopulateFiles(currentLocation);
            }
        });
    }

    public override void HandleKeyboard(Cosmos.Kernel.System.Input.KeyEvent keyEvent)
    {
        if (keyEvent.Key == Cosmos.Kernel.System.Input.Key.F5)
        {
            Refresh();
            return;
        }

        if (IsControlPressed(keyEvent))
        {
            if (keyEvent.Key == Cosmos.Kernel.System.Input.Key.C) { CopySelectedFile(); return; }
            if (keyEvent.Key == Cosmos.Kernel.System.Input.Key.X) { CutSelectedFile(); return; }
            if (keyEvent.Key == Cosmos.Kernel.System.Input.Key.V) { PasteClipboard(); return; }
            if (keyEvent.Key == Cosmos.Kernel.System.Input.Key.A) { files.SelectAll(); return; }
            if (IsShiftPressed(keyEvent) && keyEvent.Key == Cosmos.Kernel.System.Input.Key.N)
            {
                NewDirectoryFromContext();
                return;
            }
            if (keyEvent.Key == Cosmos.Kernel.System.Input.Key.L)
            {
                focusedComponent = addressBar;
                addressBar.RequestFocus();
                return;
            }
        }

        if (IsAltPressed(keyEvent))
        {
            if (keyEvent.Key == Cosmos.Kernel.System.Input.Key.LeftArrow)
            {
                GoBack();
                return;
            }
            if (keyEvent.Key == Cosmos.Kernel.System.Input.Key.RightArrow)
            {
                GoForward();
                return;
            }
            if (keyEvent.Key == Cosmos.Kernel.System.Input.Key.UpArrow)
            {
                GoUp();
                return;
            }
            if (keyEvent.Key == Cosmos.Kernel.System.Input.Key.Enter)
            {
                ShowSelectedProperties();
                return;
            }
            if (keyEvent.Key == Cosmos.Kernel.System.Input.Key.D)
            {
                focusedComponent = addressBar;
                addressBar.RequestFocus();
                return;
            }
        }

        if (keyEvent.Key == Cosmos.Kernel.System.Input.Key.F2 && focusedComponent != addressBar)
        {
            RenameSelectedItem();
            return;
        }

        if (keyEvent.Key == Cosmos.Kernel.System.Input.Key.Backspace && focusedComponent != addressBar)
        {
            GoUp();
            return;
        }

        if (keyEvent.Key == Cosmos.Kernel.System.Input.Key.Delete && focusedComponent != addressBar)
        {
            DeleteFileForSelected();
            return;
        }

        if (focusedComponent != addressBar)
        {
            if (focusedComponent == files)
            {
                files.HandleKeyboard(keyEvent);
                return;
            }
            else if (focusedComponent == tree)
            {
                tree.HandleKeyboard(keyEvent);
                return;
            }
            else
            {
                files.HandleKeyboard(keyEvent);
                return;
            }
        }

        base.HandleKeyboard(keyEvent);
    }

    public override void Dispose()
    {
        fileContextMenu.Hide();
        fileContextMenu.Dispose();
        base.Dispose();
    }
}
