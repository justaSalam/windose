using Cosmos.Kernel.System.Input;
using Windose.System.Kernel.Subsystem;
using Windose.System.System_Calls;

public enum FileDialogMode
{
    Open,
    Save,
}

public sealed class FileDialogOptions
{
    public FileDialogMode Mode = FileDialogMode.Open;
    public string Title = "";
    public string InitialPath = "/mnt";
    public string FilterExtension = "";
    public string FilterDescription = "All files (*.*)";
    public string DefaultExtension = "";
    public string DefaultFileName = "";
    public string AcceptButtonText = "";
    public bool AllowOverwrite;
}

public sealed class FileDialog : Window
{
    /// <summary>Chosen path after Accept. Null if cancelled or not yet chosen.</summary>
    public string SelectedPath { get; private set; }

    private readonly FileDialogOptions options;
    private readonly Action<string> accepted;
    private readonly AddressBar addressBar;
    private readonly FileListView files;
    private readonly ScrollView fileScroll;
    private readonly TextField fileName;
    private readonly Panel status;
    private string currentDirectory;

    public FileDialog(FileDialogOptions options, Action<string> accepted,
        int x = 180, int y = 120, int width = 660, int height = 460)
        : base(x, y, width, height, GetDialogTitle(options), true)
    {
        this.options = options ?? new FileDialogOptions();
        this.accepted = accepted;
        canMaximize = false;

        DockPanel root = new DockPanel(0, 0, Width, Height)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(28, 2, 2, 2),
            Padding = new Thickness(0),
            useBackground = true,
            backgroundColor = Palette.ControlFace,
        };

        Toolbar toolbar = new Toolbar(0, 0, Width);
        toolbar.AddButton("Up", NavigateUp);
        toolbar.AddButton("Refresh", Refresh);
        toolbar.AddButton("Go", () => NavigateTo(addressBar.Address));

        addressBar = new AddressBar(0, 0, Width);
        addressBar.label.text = "Look in";

        files = new FileListView(0, 0, Width, Height)
        {
            viewMode = ListViewMode.Details,
            useBackground = true,
            backgroundColor = Palette.ControlWhite,
            headers = ["Name", "Size", "Type", "Modified"],
            headerWidths = [240, 80, 140, 140],
        };
        fileScroll = new ScrollView(0, 0, Width, Height)
        {
            clampSize = false,
            Margin = new Thickness(0),
            showHorizontalScrollbar = true,
        };
        fileScroll.SetContent(files, Width, files.GetContentHeight());

        Panel footer = new Panel(Palette.ControlFace, 0, 0, Width, 62)
        {
            clampSize = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0),
            useBackground = true,
        };
        Panel fileLabel = new Panel(Palette.ControlFace, 8, 5, 78, 24)
        {
            text = "File name:",
            fontSize = 16,
            useBackground = false,
            clampSize = false,
            Margin = new Thickness(0),
        };
        fileName = new TextField(88, 4, Math.Max(120, width - 270), 26)
        {
            text = GetInitialFileName(this.options.InitialPath, this.options.DefaultFileName),
            clampSize = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(4, 32, 174, 88),
        };
        Button acceptButton = new Button(GetAcceptButtonText(this.options), width - 170, 4, 76, 26)
        {
            useBorders = true,
            clampSize = false,
            horizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 90, 32),
            leftClickAction = Accept,
        };
        Button cancelButton = new Button("Cancel", width - 88, 4, 76, 26)
        {
            useBorders = true,
            clampSize = false,
            horizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 8, 32),
            leftClickAction = () => WindowManager.PostClose(this),
        };
        status = new Panel(Palette.ControlFace, 8, 34, width - 16, 22)
        {
            text = this.options.FilterDescription,
            fontSize = 14,
            useBackground = false,
            clampSize = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(34, 6, 8, 8),
        };

        footer.AddChild(fileLabel);
        footer.AddChild(fileName);
        footer.AddChild(acceptButton);
        footer.AddChild(cancelButton);
        footer.AddChild(status);

        root.AddDockChild(toolbar, Dock.Top);
        root.AddDockChild(addressBar, Dock.Top);
        root.AddDockChild(footer, Dock.Bottom);
        root.AddDockChild(fileScroll, Dock.Fill);
        AddChild(root);

        files.selectedChanged = SelectItem;
        files.itemDoubleClick = OpenItem;

        string initialDirectory = GetInitialDirectory(this.options.InitialPath);
        NavigateTo(initialDirectory);
    }

    private void SelectItem(FileListViewItem item)
    {
        if (item == null) return;
        if (!item.isFolder) fileName.text = item.text;
        status.text = item.isFolder ? "File Folder" : item.type;
        fileName.MarkDirty();
        status.MarkDirty();
    }

    private void OpenItem(FileListViewItem item)
    {
        if (item == null || item.tag == null) return;
        if (item.isFolder)
        {
            NavigateTo((string)item.tag);
            return;
        }

        fileName.text = item.text;
        fileName.MarkDirty();
        if (options.Mode == FileDialogMode.Open) Accept();
    }

    private void NavigateUp()
    {
        string parent = FileSystemManager.GetParent(currentDirectory);
        if (string.IsNullOrEmpty(parent)) return;
        NavigateTo(parent);
    }

    private void Refresh() => NavigateTo(currentDirectory);

    private void NavigateTo(string path)
    {
        path = FileSystemManager.NormalizePath(path);
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            SetStatus("Folder not found");
            return;
        }

        currentDirectory = path;
        addressBar.Address = path;
        files.ClearItems();

        try
        {
            string[] directories = Directory.GetDirectories(path);
            for (int i = 0; i < directories.Length; i++)
            {
                string directory = directories[i];
                string dirName = Path.GetFileName(directory);
                if (string.IsNullOrEmpty(dirName)) dirName = directory;
                if (dirName == ".trash") continue;

                FileListViewItem item = files.AddFolder(dirName, tag: directory);
                item.type = "File Folder";
                item.size = "";
                item.modified = "";
            }

            string[] paths = Directory.GetFiles(path);
            for (int i = 0; i < paths.Length; i++)
            {
                string file = paths[i];
                if (!MatchesFilter(file)) continue;
                string fName = Path.GetFileName(file);
                if (fName.EndsWith(".origpath", StringComparison.OrdinalIgnoreCase)) continue;

                FileListViewItem item = files.AddItem(fName, tag: file);
                item.isFolder = false;
                item.type = FileAssociations.GetFileTypeDescription(file);
                try
                {
                    FileInfo fi = new FileInfo(file);
                    item.size = ByteSize.Format(fi.Length);
                    item.modified = fi.CreationTime.ToString("g");
                }
                catch
                {
                    item.size = "";
                    item.modified = "";
                }
            }
        }
        catch (Exception ex)
        {
            SetStatus("Error reading folder: " + ex.Message);
            SystemLogger.WriteLine("FileDialog", $"Error reading directory {path}: {ex.Message}", ConsoleMessageType.Error);
        }

        SetStatus(files.items.Count + " item(s)");
        fileScroll.RefreshContent(true);
    }

    private void Accept()
    {
        if (string.IsNullOrEmpty(currentDirectory))
        {
            SetStatus("Choose an available folder");
            return;
        }

        string name = fileName.text == null ? "" : fileName.text.Trim();
        if (name == "")
        {
            SetStatus("Enter a file name");
            return;
        }
        string defaultExtension = NormalizeExtension(options.DefaultExtension);
        if (defaultExtension != "" && !name.EndsWith(defaultExtension, StringComparison.OrdinalIgnoreCase))
            name += defaultExtension;

        string path = FileSystemManager.Combine(currentDirectory, name);
        if (options.Mode == FileDialogMode.Open && !File.Exists(path))
        {
            SetStatus("File not found");
            return;
        }
        if (options.Mode == FileDialogMode.Save && !options.AllowOverwrite && File.Exists(path))
        {
            SetStatus("A file with that name already exists");
            return;
        }

        SelectedPath = path;
        try
        {
            accepted?.Invoke(path);
        }
        catch (Exception ex)
        {
            SystemLogger.WriteLine("FileDialog", $"Error in dialog callback: {ex.Message}", ConsoleMessageType.Error);
        }
        WindowManager.PostClose(this);
    }

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        if (keyEvent.Key == Key.Enter) { Accept(); return; }
        if (keyEvent.Key == Key.Escape) { WindowManager.PostClose(this); return; }
        base.HandleKeyboard(keyEvent);
    }

    private void SetStatus(string value)
    {
        status.text = value;
        status.MarkDirty();
    }

    private static string GetInitialDirectory(string path)
    {
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) return FileSystemManager.NormalizePath(path);
        string directory = FileSystemManager.GetParent(path ?? "");
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)) return directory;
        if (Directory.Exists("/mnt")) return "/mnt";
        return "/";
    }

    private static string GetInitialFileName(string path, string defaultName)
    {
        if (path != null && path != "" && Directory.Exists(path)) return defaultName ?? "";
        string name = FileSystemManager.GetName(path ?? "");
        return name == "" ? defaultName ?? "" : name;
    }

    private bool MatchesFilter(string path)
    {
        if (options == null || string.IsNullOrEmpty(options.FilterExtension)) return true;
        string[] exts = options.FilterExtension.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
        if (exts.Length == 0) return true;
        for (int i = 0; i < exts.Length; i++)
        {
            string ext = NormalizeExtension(exts[i]);
            if (ext == "" || ext == ".*" || path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return "";
        extension = extension.Trim().TrimStart('*');
        if (string.IsNullOrEmpty(extension)) return "";
        return extension.StartsWith('.') ? extension : "." + extension;
    }

    private static string GetDialogTitle(FileDialogOptions options)
    {
        if (options != null && !string.IsNullOrEmpty(options.Title)) return options.Title;
        return (options != null && options.Mode == FileDialogMode.Save) ? "Save File" : "Open File";
    }

    private static string GetAcceptButtonText(FileDialogOptions options)
    {
        if (options != null && !string.IsNullOrEmpty(options.AcceptButtonText)) return options.AcceptButtonText;
        return (options != null && options.Mode == FileDialogMode.Save) ? "Save" : "Open";
    }

    public override string GetComponentName() => "FileDialog";
}
