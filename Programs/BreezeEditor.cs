using System.IO;

public class BreezeEditor : Window
{
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

    private readonly AddressBar pathBar;
    private readonly CodeEditor editor;
    private readonly Panel statusText;
    private readonly Panel cursorText;
    private bool documentDirty;
    private bool diagnosticsPending;
    private long diagnosticsDueAt;
    private const long DiagnosticDelayTicks = 3000000;

    public BreezeEditor(int x = 100, int y = 80, int width = 900, int height = 620)
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
        fileMenu.AddSeparator();
        fileMenu.AddItem("Close", () => WindowManager.PostClose(this));

        MenuPage editMenu = menuBar.AddMenuPage("Edit");
        editMenu.AddItem("Cut", CutSelection);
        editMenu.AddItem("Copy", CopySelection);
        editMenu.AddItem("Paste", PasteClipboard);

        MenuPage runMenu = menuBar.AddMenuPage("Run");
        runMenu.AddItem("Run Script", RunDocument);

        MenuPage helpMenu = menuBar.AddMenuPage("Help");
        helpMenu.AddItem("API Reference", () =>
        {
            WindowManager.Register(new BreezeApiBrowser(X + 40, Y + 40));
        });

        Toolbar toolbar = new Toolbar(0, 0, Width);
        toolbar.AddButton("New", NewDocument, 56);
        toolbar.AddButton("Open", OpenDocument, 64);
        toolbar.AddButton("Save", SaveDocument, 64);
        toolbar.AddSeparator();
        toolbar.AddButton("Run", RunDocument, 56);

        pathBar = new AddressBar(0, 0, Width);
        pathBar.label.text = "File";
        pathBar.Address = @"0:\Apps\main.breeze";

        StatusBar statusBar = new StatusBar(0, 0, Width);
        statusText = statusBar.AddPanel("Ready", 560);
        cursorText = statusBar.AddPanel("Ln 1, Col 1", 140);

        editor = new CodeEditor(0, 0, Width, Height)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0),
        };
        editor.SetSource(DefaultSource);
        editor.changed = OnEditorChanged;
        editor.cursorChanged = UpdateCursorStatus;

        root.AddDockChild(menuBar, Dock.Top);
        root.AddDockChild(toolbar, Dock.Top);
        root.AddDockChild(pathBar, Dock.Top);
        root.AddDockChild(statusBar, Dock.Bottom);
        root.AddDockChild(editor, Dock.Fill);
        AddChild(root);
        ValidateDocument();
    }

    private void CutSelection() => editor.CutSelection();
    private void CopySelection() => editor.CopySelection();
    private void PasteClipboard() => editor.PasteClipboard();

    private void NewDocument()
    {
        FileDialogOptions options = CreateBreezeDialogOptions(FileDialogMode.Save, "New Breeze Script", "New");
        WindowManager.Register(new FileDialog(options, path =>
        {
            pathBar.Address = path;
            editor.SetSource(DefaultSource);
            documentDirty = true;
            ValidateDocument();
            UpdateCursorStatus();
        }, X + 40, Y + 30));
    }

    private void OpenDocument()
    {
        FileDialogOptions options = CreateBreezeDialogOptions(FileDialogMode.Open, "Open Breeze Script", "Open");
        WindowManager.Register(new FileDialog(options, OpenPath, X + 40, Y + 30));
    }

    private FileDialogOptions CreateBreezeDialogOptions(FileDialogMode mode, string title, string buttonText)
    {
        return new FileDialogOptions
        {
            Mode = mode,
            Title = title,
            InitialPath = pathBar.Address,
            FilterExtension = ".breeze",
            FilterDescription = "Breeze scripts (*.breeze)",
            DefaultExtension = ".breeze",
            DefaultFileName = "main.breeze",
            AcceptButtonText = buttonText,
            AllowOverwrite = false,
        };
    }

    private void OpenPath(string path)
    {
        try
        {
            editor.SetSource(File.ReadAllText(path));
            pathBar.Address = path;
            documentDirty = false;
            ValidateDocument();
            UpdateCursorStatus();
        }
        catch (Exception exception)
        {
            SetStatus("Open failed");
            BreezeHost.ShowError("Could not open " + path + ": " + exception.Message);
        }
    }

    private void SaveDocument()
    {
        string path = pathBar.Address;
        try
        {
            string directory = Path.GetDirectoryName(path);
            if (directory != null && directory != "" && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, editor.Source);
            documentDirty = false;
            if (ValidateDocument()) SetStatus("Saved " + path);
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
        SetStatus(documentDirty ? "Running unsaved source" : "Running script");
        BreezeHost.RunSource(editor.Source);
    }

    private void OnEditorChanged()
    {
        documentDirty = true;
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
        BreezeLexer lexer = new BreezeLexer(source);
        List<BreezeToken> tokens = lexer.Tokenize();
        string error = lexer.ErrorMessage;

        if (error == null)
        {
            BreezeParser parser = new BreezeParser(tokens);
            parser.Parse();
            error = parser.ErrorMessage;
        }

        CollectDocumentSymbols(tokens, out List<string> variables, out List<string> functions);
        editor.SetDocumentSymbols(variables, functions);
        if (error != null)
        {
            editor.SetDiagnostic(GetErrorLine(error) - 1, error);
            SetStatus(error);
            return false;
        }

        editor.SetDiagnostic(-1, "");
        SetStatus(documentDirty ? "Modified - no errors" : "No errors");
        return true;
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

    public override string GetName() => "BreezeEditor";
}
