public class BreezeEditor : Window
{
    private sealed class EditorDocument
    {
        public string Path;
        public string Source;
        public bool Dirty;
    }

    private sealed class ProjectEntry
    {
        public string Path;
        public bool IsDirectory;
    }

    private const string DefaultSource = @"// Breeze application
let main = window(""My Application"", 160, 120, 600, 400);
let root = windowRoot(main);
let status = statusBar();
dock(root, status, ""bottom"");
let statusText = statusPanel(status, ""Ready"", 400);
let body = stackPanel(""vertical"");
dock(root, body, ""fill"");
let action = button(""Click me"", 100, 28);
stack(body, action);

on action.click {
    set statusText.text = ""Button clicked"";
}

show(main);
";

    private readonly List<EditorDocument> documents = new List<EditorDocument>();
    private readonly AddressBar pathBar;
    private readonly CodeEditor editor;
    private readonly Panel statusText;
    private readonly Panel cursorText;
    private readonly DocumentTabStrip tabStrip;
    private readonly TreeView projectTree;
    private readonly ScrollView projectScroll;
    private readonly IWindoseFileSystem subscribedFileSystem;
    private int activeDocumentIndex = -1;
    private string projectRoot = @"/mnt/Apps";
    private bool diagnosticsPending;
    private long diagnosticsDueAt;
    private const long DiagnosticDelayTicks = 1500000;

    private EditorDocument ActiveDocument => activeDocumentIndex >= 0 && activeDocumentIndex < documents.Count
        ? documents[activeDocumentIndex]
        : null;

    public BreezeEditor(int x = 100, int y = 80, int width = 900, int height = 620,
        string initialPath = "")
        : base(x, y, width, height, "Breeze Editor", true)
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
        fileMenu.AddItem("New", NewDocument);
        fileMenu.AddItem("Open", OpenDocument);
        fileMenu.AddItem("Save", SaveDocument);
        fileMenu.AddItem("Close Tab", CloseActiveTab);
        fileMenu.AddSeparator();
        fileMenu.AddItem("Close", () => WindowManager.PostClose(this));

        MenuPage editMenu = menuBar.AddMenuPage("Edit");
        editMenu.AddItem("Undo", () => editor.Undo());
        editMenu.AddItem("Redo", () => editor.Redo());
        editMenu.AddSeparator();
        editMenu.AddItem("Cut", () => editor.CutSelection());
        editMenu.AddItem("Copy", () => editor.CopySelection());
        editMenu.AddItem("Paste", () => editor.PasteClipboard());

        MenuPage projectMenu = menuBar.AddMenuPage("Project");
        projectMenu.AddItem("Refresh", RefreshProjectExplorer);

        MenuPage runMenu = menuBar.AddMenuPage("Run");
        runMenu.AddItem("Run Script", RunDocument);
        runMenu.AddItem("Run Background", RunBackgroundDocument);

        MenuPage helpMenu = menuBar.AddMenuPage("Help");
        helpMenu.AddItem("API Reference", () => WindowManager.Register(new BreezeApiBrowser(X + 40, Y + 40)));

        Toolbar toolbar = new Toolbar(0, 0, Width);
        toolbar.AddButton("New", NewDocument);
        toolbar.AddButton("Open", OpenDocument);
        toolbar.AddButton("Save", SaveDocument);
        toolbar.AddSeparator();
        toolbar.AddButton("Undo", () => editor.Undo());
        toolbar.AddButton("Redo", () => editor.Redo());
        toolbar.AddSeparator();
        toolbar.AddButton("Run", RunDocument);
        toolbar.AddButton("Background", RunBackgroundDocument);

        pathBar = new AddressBar(0, 0, Width);
        pathBar.label.text = "File";

        StatusBar statusBar = new StatusBar(0, 0, Width);
        statusText = statusBar.AddPanel("Ready", 560);
        cursorText = statusBar.AddPanel("Ln 1, Col 1", 140);

        DockPanel workspace = new DockPanel(0, 0, Width, Height)
        {
            clampSize = false,
            Padding = new Thickness(0),
            useBackground = true,
        };

        projectTree = new TreeView(0, 0, 210, Height)
        {
            useBackground = true,
            backgroundColor = Palette.ControlWhite,
        };
        projectScroll = new ScrollView(0, 0, 210, Height)
        {
            showHorizontalScrollbar = false,
            clampSize = false,
            Margin = new Thickness(0),
        };
        projectScroll.SetContent(projectTree, 210, projectTree.GetContentHeight());

        Splitter splitter = new Splitter(0, 0, 4, Height)
        {
            orientation = LayoutOrientation.Vertical,
            clampSize = false,
            Margin = new Thickness(0),
        };

        DockPanel editorArea = new DockPanel(0, 0, Width, Height)
        {
            clampSize = false,
            Padding = new Thickness(0),
            useBackground = true,
        };
        tabStrip = new DocumentTabStrip(0, 0, Width, 26)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
        };
        editor = new CodeEditor(0, 0, Width, Height)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0),
        };

        editorArea.AddDockChild(tabStrip, Dock.Top);
        editorArea.AddDockChild(editor, Dock.Fill);
        workspace.AddDockChild(projectScroll, Dock.Left);
        workspace.AddDockChild(splitter, Dock.Left);
        workspace.AddDockChild(editorArea, Dock.Fill);

        root.AddDockChild(menuBar, Dock.Top);
        root.AddDockChild(toolbar, Dock.Top);
        root.AddDockChild(pathBar, Dock.Top);
        root.AddDockChild(statusBar, Dock.Bottom);
        root.AddDockChild(workspace, Dock.Fill);
        AddChild(root);

        editor.changed = OnEditorChanged;
        editor.cursorChanged = UpdateCursorStatus;
        tabStrip.tabSelected = SwitchDocument;
        projectTree.itemDoubleClick = OpenProjectItem;

        if (!string.IsNullOrEmpty(initialPath)) OpenPath(initialPath, true);
        else CreateDocument(@"/mnt/Apps/main.breeze", DefaultSource, false, true);
        RefreshProjectExplorer();
        ValidateDocument();
    }

    private void NewDocument()
    {
        FileDialogOptions options = CreateBreezeDialogOptions(FileDialogMode.Save, "New Breeze Script", "New");
        WindowManager.Register(new FileDialog(options, path =>
        {
            projectRoot = FileSystemManager.GetParent(path);
            CreateDocument(path, DefaultSource, true, true);
            RefreshProjectExplorer();
        }, X + 40, Y + 30));
    }

    private void OpenDocument()
    {
        FileDialogOptions options = CreateBreezeDialogOptions(FileDialogMode.Open, "Open Breeze Script", "Open");
        WindowManager.Register(new FileDialog(options, path => OpenPath(path, true), X + 40, Y + 30));
    }

    private FileDialogOptions CreateBreezeDialogOptions(FileDialogMode mode, string title, string buttonText)
    {
        return new FileDialogOptions
        {
            Mode = mode,
            Title = title,
            InitialPath = ActiveDocument?.Path ?? @"/mnt/Apps/main.breeze",
            FilterExtension = ".breeze",
            FilterDescription = "Breeze scripts (*.breeze)",
            DefaultExtension = ".breeze",
            DefaultFileName = "main.breeze",
            AcceptButtonText = buttonText,
            AllowOverwrite = false,
        };
    }

    private void OpenPath(string path, bool updateProjectRoot = false)
    {
        string normalized = FileSystemManager.NormalizePath(path);
        for (int i = 0; i < documents.Count; i++)
        {
            if (!string.Equals(documents[i].Path, normalized, StringComparison.OrdinalIgnoreCase)) continue;
            SwitchDocument(i);
            return;
        }

        try
        {
            if (updateProjectRoot) projectRoot = FileSystemManager.GetParent(normalized);
            CreateDocument(normalized, File.ReadAllText(normalized), false, true);
            if (updateProjectRoot) RefreshProjectExplorer();
        }
        catch (Exception exception)
        {
            SetStatus("Open failed");
            BreezeHost.ShowError("Could not open " + normalized + ": " + exception.Message);
        }
    }

    private void CreateDocument(string path, string source, bool dirty, bool activate)
    {
        SaveActiveEditorState();
        documents.Add(new EditorDocument
        {
            Path = FileSystemManager.NormalizePath(path),
            Source = source ?? "",
            Dirty = dirty,
        });
        RebuildTabs();
        if (activate) SwitchDocument(documents.Count - 1);
    }

    private void SwitchDocument(int index)
    {
        if (index < 0 || index >= documents.Count) return;
        if (index == activeDocumentIndex)
        {
            RebuildTabs();
            return;
        }

        SaveActiveEditorState();
        activeDocumentIndex = index;
        EditorDocument document = documents[index];
        pathBar.Address = document.Path;
        editor.SetSource(document.Source);
        RebuildTabs();
        UpdateCursorStatus();
        ValidateDocument();
        ForceDirty();
    }

    private void SaveActiveEditorState()
    {
        EditorDocument document = ActiveDocument;
        if (document == null) return;
        document.Source = editor.Source;
        document.Path = FileSystemManager.NormalizePath(pathBar.Address);
    }

    private void CloseActiveTab()
    {
        if (ActiveDocument == null) return;
        int closing = activeDocumentIndex;
        documents.RemoveAt(closing);
        activeDocumentIndex = -1;
        if (documents.Count == 0)
            CreateDocument(@"/mnt/Apps/untitled.breeze", DefaultSource, true, true);
        else
            SwitchDocument(Math.Min(closing, documents.Count - 1));
        RebuildTabs();
    }

    private void RebuildTabs()
    {
        List<string> labels = new List<string>();
        for (int i = 0; i < documents.Count; i++)
        {
            EditorDocument document = documents[i];
            labels.Add(FileSystemManager.GetName(document.Path) + (document.Dirty ? " *" : ""));
        }
        tabStrip.SetTabs(labels, activeDocumentIndex);
    }

    private void SaveDocument()
    {
        EditorDocument document = ActiveDocument;
        if (document == null) return;
        SaveActiveEditorState();
        string path = document.Path;
        try
        {
            string directory = FileSystemManager.GetParent(path);
            if (directory != "" && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, document.Source);

            document.Dirty = false;
            RebuildTabs();
            if (ValidateDocument()) SetStatus("Saved " + path);
            RefreshProjectExplorer();
        }
        catch (Exception exception)
        {
            SetStatus("Save failed");
            BreezeHost.ShowError("Could not save " + path + ": " + exception.Message);
        }
    }

    private void RunDocument()
    {
        if (!ValidateDocument()) return;
        EditorDocument document = ActiveDocument;
        SetStatus(document != null && document.Dirty ? "Running unsaved source" : "Running script");
        BreezeHost.RunSource(editor.Source, document?.Path ?? pathBar.Address);
    }

    private void RunBackgroundDocument()
    {
        if (!ValidateDocument()) return;
        EditorDocument document = ActiveDocument;
        SetStatus(document != null && document.Dirty ? "Running unsaved source in background" : "Running in background");
        BreezeHost.RunScheduledSource(editor.Source, document?.Path ?? pathBar.Address);
    }

    private void OnEditorChanged()
    {
        EditorDocument document = ActiveDocument;
        if (document != null) document.Dirty = true;
        RebuildTabs();
        SetStatus("Modified");
        diagnosticsPending = true;
        diagnosticsDueAt = DateTime.UtcNow.Ticks + DiagnosticDelayTicks;
    }

    public override void Update()
    {
        base.Update();
        if (!diagnosticsPending || DateTime.UtcNow.Ticks < diagnosticsDueAt) return;
        ValidateDocument();
    }

    private bool ValidateDocument()
    {
        diagnosticsPending = false;
        string source = editor.Source;
        List<CodeEditor.Diagnostic> diagnostics = CollectDelimiterDiagnostics(source);
        BreezeLexer lexer = new BreezeLexer(source);
        List<BreezeToken> tokens = lexer.Tokenize();
        string error = lexer.ErrorMessage;

        if (error == null)
        {
            BreezeParser parser = new BreezeParser(tokens);
            parser.Parse();
            error = parser.ErrorMessage;
        }

        if (error != null)
            AddDiagnostic(diagnostics, GetErrorLine(error) - 1, error);

        CollectDocumentSymbols(tokens, out List<string> variables, out List<string> functions);
        editor.SetDocumentSymbols(variables, functions);
        editor.SetDiagnostics(diagnostics);

        if (diagnostics.Count > 0)
        {
            SetStatus(diagnostics.Count + " error(s): " + diagnostics[0].Message);
            return false;
        }

        SetStatus(ActiveDocument?.Dirty == true ? "Modified - no errors" : "No errors");
        return true;
    }

    private static List<CodeEditor.Diagnostic> CollectDelimiterDiagnostics(string source)
    {
        List<CodeEditor.Diagnostic> result = new List<CodeEditor.Diagnostic>();
        List<int> braces = new List<int>();
        List<int> parentheses = new List<int>();
        int line = 0;
        bool inString = false;

        for (int i = 0; i < source.Length; i++)
        {
            char value = source[i];
            if (value == '\n') { line++; continue; }
            if (!inString && value == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                if (i < source.Length) line++;
                continue;
            }
            if (value == '"' && (i == 0 || source[i - 1] != '\\')) { inString = !inString; continue; }
            if (inString) continue;

            if (value == '{') braces.Add(line);
            else if (value == '}')
            {
                if (braces.Count == 0) AddDiagnostic(result, line, "Unexpected '}'");
                else braces.RemoveAt(braces.Count - 1);
            }
            else if (value == '(') parentheses.Add(line);
            else if (value == ')')
            {
                if (parentheses.Count == 0) AddDiagnostic(result, line, "Unexpected ')'");
                else parentheses.RemoveAt(parentheses.Count - 1);
            }
        }

        for (int i = 0; i < braces.Count; i++) AddDiagnostic(result, braces[i], "Missing '}'");
        for (int i = 0; i < parentheses.Count; i++) AddDiagnostic(result, parentheses[i], "Missing ')'");
        return result;
    }

    private static void AddDiagnostic(List<CodeEditor.Diagnostic> diagnostics, int line, string message)
    {
        for (int i = 0; i < diagnostics.Count; i++)
            if (diagnostics[i].Line == line && diagnostics[i].Message == message) return;
        diagnostics.Add(new CodeEditor.Diagnostic(line, message));
    }

    private void RefreshProjectExplorer()
    {
        projectTree.ClearItems();
        string rootPath = FileSystemManager.NormalizePath(projectRoot);
        string rootName = FileSystemManager.GetName(rootPath);
        TreeViewItem root = projectTree.AddRoot(rootName == "" ? rootPath : rootName,
            new ProjectEntry { Path = rootPath, IsDirectory = true });
        if (Directory.Exists(rootPath))
            AddProjectChildren(root, rootPath, 0);
        projectScroll.RefreshContent(true);
        projectScroll.ForceDirty();
    }

    private void AddProjectChildren(TreeViewItem parent, string path, int depth)
    {
        if (depth >= 8) return;
        string[] directories = Directory.GetDirectories(path);
        for (int i = 0; i < directories.Length; i++)
        {
            string directory = directories[i];
            TreeViewItem child = parent.AddChild(FileSystemManager.GetName(directory),
                new ProjectEntry { Path = directory, IsDirectory = true });
            AddProjectChildren(child, directory, depth + 1);
        }

        string[] files = Directory.GetFiles(path);
        for (int i = 0; i < files.Length; i++)
        {
            string file = files[i];
            parent.AddChild(FileSystemManager.GetName(file),
                new ProjectEntry { Path = file, IsDirectory = false });
        }
    }

    private void OpenProjectItem(TreeViewItem item)
    {
        if (!(item?.tag is ProjectEntry entry) || entry.IsDirectory) return;
        if (!string.Equals(FileSystemManager.GetExtension(entry.Path), ".breeze", StringComparison.OrdinalIgnoreCase)) return;
        OpenPath(entry.Path);
    }

    private void OnFileSystemChanged(FileSystemChange change)
    {
        WindowManager.PostCommand("editor.project.changed", target: this, data: change);
    }

    public override void HandleMessage(UiMessage message)
    {
        if (message.Command == "editor.project.changed") RefreshProjectExplorer();
    }

    private static void CollectDocumentSymbols(List<BreezeToken> tokens,
        out List<string> variables, out List<string> functions)
    {
        variables = new List<string>();
        functions = new List<string>();
        for (int i = 0; i + 1 < tokens.Count; i++)
        {
            BreezeToken token = tokens[i];
            BreezeToken next = tokens[i + 1];
            if (token.Type == BreezeTokenType.Let && next.Type == BreezeTokenType.Identifier)
                AddUnique(variables, next.Text);

            if (token.Type != BreezeTokenType.Function || next.Type != BreezeTokenType.Identifier) continue;
            AddUnique(functions, next.Text);
            int parameter = i + 2;
            while (parameter < tokens.Count && tokens[parameter].Type != BreezeTokenType.LeftParenthesis) parameter++;
            parameter++;
            while (parameter < tokens.Count && tokens[parameter].Type != BreezeTokenType.RightParenthesis)
            {
                if (tokens[parameter].Type == BreezeTokenType.Identifier)
                    AddUnique(variables, tokens[parameter].Text);
                parameter++;
            }
        }
    }

    private static void AddUnique(List<string> values, string value)
    {
        if (!string.IsNullOrEmpty(value) && !values.Contains(value)) values.Add(value);
    }

    private static int GetErrorLine(string error)
    {
        if (string.IsNullOrEmpty(error) || !error.StartsWith("Line ")) return 1;
        int colon = error.IndexOf(':');
        if (colon <= 5) return 1;
        return int.TryParse(error.Substring(5, colon - 5), out int line) ? Math.Max(1, line) : 1;
    }

    private void UpdateCursorStatus()
    {
        string value = "Ln " + editor.CursorLine + ", Col " + editor.CursorColumn;
        if (cursorText.text == value) return;
        cursorText.text = value;
        cursorText.MarkDirty();
    }

    private void SetStatus(string value)
    {
        if (statusText.text == value) return;
        statusText.text = value;
        statusText.MarkDirty();
    }

    public override void Dispose()
    {
        if (subscribedFileSystem != null) subscribedFileSystem.Changed -= OnFileSystemChanged;
        base.Dispose();
    }

    public override string GetComponentName() => "BreezeEditor";
}
