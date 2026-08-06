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
    private readonly FileDialogOptions options;
    private readonly Action<string> accepted;
    private readonly AddressBar addressBar;
    private readonly ListView files;
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
        toolbar.AddButton("Up", NavigateUp, 56);
        toolbar.AddButton("Refresh", Refresh, 72);
        toolbar.AddButton("Go", () => NavigateTo(addressBar.Address), 48);

        addressBar = new AddressBar(0, 0, Width);
        addressBar.label.text = "Look in";

        files = new ListView(0, 0, Width, Height)
        {
            viewMode = ListViewMode.Details,
            useBackground = true,
            backgroundColor = Palette.ControlWhite,
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
            fontSize = 16,
            clampSize = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(4, 32, 174, 88),
        };
        Button acceptButton = new Button(width - 170, 4, 76, 26)
        {
            text = GetAcceptButtonText(this.options),
            fontSize = 16,
            useBorders = true,
            clampSize = false,
            horizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 90, 32),
            leftMouseRelease = Accept,
        };
        Button cancelButton = new Button(width - 88, 4, 76, 26)
        {
            text = "Cancel",
            fontSize = 16,
            useBorders = true,
            clampSize = false,
            horizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 8, 32),
            leftMouseRelease = () => WindowManager.PostClose(this),
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

    private void SelectItem(ListViewItem item)
    {
        if (item == null) return;
        if (!item.isFolder) fileName.text = item.text;
        status.text = item.isFolder ? "File Folder" : item.type;
        fileName.MarkDirty();
        status.MarkDirty();
    }

    private void OpenItem(ListViewItem item)
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
        if (parent == null || parent == "") return;
        NavigateTo(parent);
    }

    private void Refresh() => NavigateTo(currentDirectory);

    private void NavigateTo(string path)
    {
        path = FileSystemManager.NormalizePath(path);
        if (!Directory.Exists(path))
        {
            SetStatus("Folder not found");
            return;
        }

        currentDirectory = path;
        addressBar.Address = path;
        files.ClearItems();

        string[] directories = Directory.GetDirectories(path);
        for (int i = 0; i < directories.Length; i++)
        {
            string directory = directories[i];
            ListViewItem item = files.AddFolder(directory, tag: directory);
            item.type = "File Folder";
        }

        string[] paths = Directory.GetFiles(path);
        for (int i = 0; i < paths.Length; i++)
        {
            string file = paths[i];
            if (!MatchesFilter(file)) continue;
            ListViewItem item = files.AddItem(Path.GetFileName(file), tag: file);
            item.type = options.FilterDescription;
        }

        SetStatus(files.items.Count + " item(s)");
        fileScroll.RefreshContent(true);
    }

    private void Accept()
    {
        if (currentDirectory == null || currentDirectory == "")
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

        accepted?.Invoke(path);
        WindowManager.PostClose(this);
    }

    private void SetStatus(string value)
    {
        status.text = value;
        status.MarkDirty();
    }

    private static string GetInitialDirectory(string path)
    {
        if (path != null && path != "" && Directory.Exists(path)) return FileSystemManager.NormalizePath(path);
        string directory = FileSystemManager.GetParent(path ?? "");
        if (directory != null && directory != "" && Directory.Exists(directory)) return directory;
        return "/mnt";
    }

    private static string GetInitialFileName(string path, string defaultName)
    {
        if (path != null && path != "" && Directory.Exists(path)) return defaultName ?? "";
        string name = FileSystemManager.GetName(path ?? "");
        return name == "" ? defaultName ?? "" : name;
    }

    private bool MatchesFilter(string path)
    {
        string extension = NormalizeExtension(options.FilterExtension);
        return extension == "" || path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeExtension(string extension)
    {
        if (extension == null || extension == "") return "";
        return extension[0] == '.' ? extension : "." + extension;
    }

    private static string GetDialogTitle(FileDialogOptions options)
    {
        if (options != null && options.Title != null && options.Title != "") return options.Title;
        return options != null && options.Mode == FileDialogMode.Save ? "Save File" : "Open File";
    }

    private static string GetAcceptButtonText(FileDialogOptions options)
    {
        if (options.AcceptButtonText != null && options.AcceptButtonText != "") return options.AcceptButtonText;
        return options.Mode == FileDialogMode.Save ? "Save" : "Open";
    }

    public override string GetName() => "FileDialog";
}
