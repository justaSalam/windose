public sealed class BreezeApiPage
{
    public string title;
    public string body;

    public BreezeApiPage(string title, string body)
    {
        this.title = title;
        this.body = body;
    }
}

public class BreezeApiBrowser : Window
{
    private readonly Dictionary<string, BreezeApiPage> pages = new Dictionary<string, BreezeApiPage>();
    private readonly List<string> history = new List<string>();
    private readonly ApiDocumentView document;
    private readonly TreeView navigation;
    private readonly Panel status;
    private int historyIndex = -1;

    public BreezeApiBrowser(int x = 120, int y = 70, int width = 940, int height = 620)
        : base(x, y, width, height, "Breeze API Reference", true)
    {
        BuildCatalog();

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
        fileMenu.AddItem("Close", () => WindowManager.PostClose(this));
        MenuPage viewMenu = menuBar.AddMenuPage("View");
        viewMenu.AddItem("Home", () => Navigate("overview", true));

        Toolbar toolbar = new Toolbar(0, 0, Width);
        toolbar.AddButton("Back", Back, 64);
        toolbar.AddButton("Forward", Forward, 80);
        toolbar.AddButton("Home", () => Navigate("overview", true), 64);

        StatusBar statusBar = new StatusBar(0, 0, Width);
        status = statusBar.AddPanel("Breeze API", 520);
        statusBar.AddPanel(pages.Count + " pages", 120);

        DockPanel body = new DockPanel(0, 0, Width, Height)
        {
            clampSize = false,
            Padding = new Thickness(0),
            useBackground = true,
            backgroundColor = Palette.ControlFace,
        };

        navigation = new TreeView(0, 0, 230, Height)
        {
            clampSize = false,
            useBackground = true,
            backgroundColor = Palette.ControlWhite,
        };
        BuildNavigation();
        navigation.selectedChanged = item =>
        {
            if (item?.tag is string pageKey && pages.ContainsKey(pageKey)) Navigate(pageKey, true);
        };

        ScrollView navigationScroll = new ScrollView(0, 0, 240, Height)
        {
            showHorizontalScrollbar = false,
            clampSize = false,
            Margin = new Thickness(0),
        };
        navigationScroll.SetContent(navigation, 230, navigation.GetContentHeight());

        Splitter splitter = new Splitter(0, 0, 4, Height)
        {
            orientation = LayoutOrientation.Vertical,
            clampSize = false,
            Margin = new Thickness(0),
        };

        document = new ApiDocumentView(0, 0, Width, Height)
        {
            clampSize = false,
            Margin = new Thickness(0),
        };

        body.AddDockChild(navigationScroll, Dock.Left);
        body.AddDockChild(splitter, Dock.Left);
        body.AddDockChild(document, Dock.Fill);

        root.AddDockChild(menuBar, Dock.Top);
        root.AddDockChild(toolbar, Dock.Top);
        root.AddDockChild(statusBar, Dock.Bottom);
        root.AddDockChild(body, Dock.Fill);
        AddChild(root);

        Navigate("overview", true);
    }

    private void Navigate(string key, bool addHistory)
    {
        if (!pages.TryGetValue(key, out BreezeApiPage page)) return;

        document.SetDocument(page.title, page.body);
        status.text = page.title;
        status.MarkDirty();

        if (!addHistory) return;
        if (historyIndex >= 0 && historyIndex < history.Count && history[historyIndex] == key) return;

        while (history.Count > historyIndex + 1) history.RemoveAt(history.Count - 1);
        history.Add(key);
        historyIndex = history.Count - 1;
    }

    private void Back()
    {
        if (historyIndex <= 0) return;
        historyIndex--;
        Navigate(history[historyIndex], false);
    }

    private void Forward()
    {
        if (historyIndex >= history.Count - 1) return;
        historyIndex++;
        Navigate(history[historyIndex], false);
    }

    private void BuildNavigation()
    {
        TreeViewItem home = navigation.AddRoot("Breeze API", "overview");
        AddCategory("Language", "language", new[] { "values", "events", "properties" });
        AddCategory("Windows and Layout", "layout", new[] { "window", "dock", "stack" });
        AddCategory("Controls", "controls", new[] { "basic-controls", "menus", "bars" });
        AddCategory("Data Views", "data-views", new[] { "tree", "list", "scroll" });
        AddCategory("Files and Runtime", "runtime", new[] { "files", "application", "debugging" });
        home.expanded = true;
    }

    private void AddCategory(string title, string page, string[] children)
    {
        TreeViewItem category = navigation.AddRoot(title, page);
        category.expanded = true;
        for (int i = 0; i < children.Length; i++)
        {
            string key = children[i];
            category.AddChild(pages[key].title, key);
        }
    }

    private void AddPage(string key, string title, string body) => pages[key] = new BreezeApiPage(title, body);

    private void BuildCatalog()
    {
        AddPage("overview", "Breeze API Reference", @"
## Build applications without rebuilding Windose
Breeze is an interpreted language connected directly to the Windose GUI framework. Scripts can create windows, compose layouts, read files, and react to user input.

$ let main = window(""My App"", 100, 100, 600, 400);
$ let root = windowRoot(main);
$ show(main);

## Typical application order
- Create a window and its root layout.
- Create menus, toolbars, status bars, and content controls.
- Add controls with dock, stack, or add.
- Register event handlers with on.
- Call show after the application is fully constructed.

Use the navigation tree to browse every language feature and native function.");

        AddPage("language", "Language", @"
## Statements
$ let name = expression;
Creates a variable. Variables hold values or native Windose objects.

$ name = expression;
Changes an existing variable.

$ set control.property = expression;
Changes a supported property and redraws the component.

$ on control.click { statements }
Registers an event handler that runs later.

$ if (condition) { statements } else { statements }
Runs one branch based on a condition. else if is also supported.

$ while (condition) { statements }
Repeats a block. A loop is limited to 10,000 iterations.

$ function name(parameter) { return value; }
Creates a function with local parameters and variables.

## Comments
$ // Everything after this marker is ignored.");

        AddPage("values", "Values and Expressions", @"
## Supported values
- Strings: ""Hello""
- Numbers: 42 or 16.5
- Booleans: true and false
- Variables created with let
- Objects returned by native functions

## Operators
$ let total = 20 + 22;
$ let message = ""Count: "" + total;
If either operand is text, + joins the values as text. Otherwise it adds numbers.

- Arithmetic: + - * /
- Comparison: == != < <= > >=
- Logic: ! && ||

## Script lists
$ let items = list();
$ listAdd(items, ""Desktop"");
$ let first = listGet(items, 0);
$ listSet(items, 0, ""Documents"");
$ let count = listCount(items);
$ let removed = listRemove(items, 0);
Script lists hold arbitrary values and are separate from the visual ListView control.");

        AddPage("events", "Events", @"
## Click events
$ on saveButton.click { set status.text = ""Saved""; }
Button and MenuItem support click.

## Selection events
$ on files.select { set status.text = value(event, ""text""); }
TreeView and ListView support select and doubleClick.

Inside tree and list handlers, event refers to the selected item. Read its properties with value(event, property).");

        AddPage("properties", "Properties", @"
## Read properties
$ value(object, ""property"")
Returns a property for use in an expression.

## Write properties
$ set object.property = value;

- text: visible control text
- visible: true or false
- width and height: pixel dimensions
- fontSize: panel, button, or text field font height
- canResize, canMaximize, canMinimize: window capabilities

List items expose text, path, isFolder, type, and size. Tree items expose text, path, and expanded.");

        AddPage("layout", "Windows and Layout", @"
## Layout containers
Breeze exposes the same layout model used by native Windose applications.

- DockPanel reserves edges and fills remaining space.
- StackPanel places controls sequentially.
- add performs direct parent-child composition without layout.

Prefer windowRoot for the main window layout. It reserves the title bar automatically.");

        AddPage("window", "window and windowRoot", @"
## window
$ window(title, x, y, width, height)
Creates and returns a titled Window. Coordinates and dimensions are pixels. The window remains hidden from WindowManager until show is called.

$ let main = window(""Explorer"", 100, 80, 700, 500);

## windowRoot
$ windowRoot(window)
Creates a stretching DockPanel, reserves title-bar space, adds it to the supplied window, and returns the panel.

$ let root = windowRoot(main);");

        AddPage("dock", "dockPanel and dock", @"
## dockPanel
$ dockPanel()
Creates an empty DockPanel.

## dock
$ dock(parent, child, position)
Adds a child using top, bottom, left, right, or fill.

$ dock(root, menus, ""top"");
$ dock(root, status, ""bottom"");
$ dock(root, content, ""fill"");

Order matters. Add edge controls first and the fill control last.");

        AddPage("stack", "stackPanel, stack, and add", @"
## stackPanel
$ stackPanel(""vertical"")
$ stackPanel(""horizontal"")
Creates a sequential layout container.

## stack
$ stack(parent, child)
Adds a child and recalculates stack positions.

## add
$ add(parent, child)
Adds a child directly without dock or stack layout. Use it for manually positioned components.");

        AddPage("controls", "Controls", @"
## Native controls
Breeze constructors return real Windose components. Store the result with let, place it in a layout, and attach events when needed.

- panel for headings and messages
- button for commands
- textField for editable single-line input
- menus, toolbars, and status bars for application chrome
- tree and list views for structured data");

        AddPage("basic-controls", "panel, button, and textField", @"
## panel
$ panel(text, height)
Creates a fixed-height text panel.

## button
$ button(text, width, height)
Creates a clickable button. Bind on button.click to execute commands.

## textField
$ textField(initialText, height)
Creates editable single-line input. Read its contents with value(field, ""text"").

$ let name = textField(""Untitled"", 26);
$ let save = button(""Save"", 80, 28);");

        AddPage("menus", "Menus", @"
## menuBar
$ menuBar()
Creates an empty menu bar.

## menu
$ menu(menuBar, text)
Adds a drop-down page and returns it.

## menuItem
$ menuItem(menuPage, text)
Adds and returns a command item.

$ let fileMenu = menu(menus, ""File"");
$ let closeItem = menuItem(fileMenu, ""Close"");
$ on closeItem.click { close(main); }");

        AddPage("bars", "Toolbars and Status Bars", @"
## toolbar and toolbarButton
$ toolbar()
$ toolbarButton(toolbar, text, width)
toolbarButton returns a normal Button with a click event.

## statusBar and statusPanel
$ statusBar()
$ statusPanel(statusBar, text, width)
statusPanel returns a Panel. Update it with set panel.text.

Dock toolbars at the top and status bars at the bottom.");

        AddPage("data-views", "Tree, List, and Scroll Views", @"
## Structured application data
TreeView represents hierarchy. ListView represents files or repeated items. ScrollView supplies a viewport when either control grows beyond its available area.

Both tree and list controls support select and doubleClick events.");

        AddPage("tree", "TreeView", @"
## treeView
$ treeView()
Creates an empty tree.

## treeRoot
$ treeRoot(tree, text, tag)
Adds and returns a root item.

## treeChild
$ treeChild(parentItem, text, tag)
Adds and returns a nested item.

$ let drive = treeRoot(tree, ""Drive C"", ""0:\\"");
$ treeChild(drive, ""System"", ""0:\\System"");

The tag is returned as the path property during events.");

        AddPage("list", "ListView", @"
## listView
$ listView(mode)
Creates a list. Modes are icons, smallicons, list, and details.

## listItem
$ listItem(list, text, tag, isFolder)
Adds and returns an item. tag normally stores its path.

## listClear and listMode
$ listClear(list)
$ listMode(list, ""details"")
Clear all items or change the current presentation mode.");

        AddPage("scroll", "ScrollView", @"
## scrollView
$ scrollView(content)
Wraps content and returns a scrollable viewport.

$ let files = listView(""details"");
$ let fileScroll = scrollView(files);
$ dock(body, fileScroll, ""fill"");

Dock the returned ScrollView, not the original content. Tree and list content heights refresh automatically.");

        AddPage("runtime", "Files and Runtime", @"
## Native services
Scripts can use the mounted Cosmos VFS, register windows, close windows, and write diagnostics to serial output.

Runtime validation errors are returned and displayed in a Breeze Error window rather than escaping into WindowManager.");

        AddPage("files", "Filesystem", @"
## loadDirectory
$ loadDirectory(list, path)
Clears a ListView and fills it with directories and files from System.IO.

$ loadDirectory(files, ""0:\\"");

The Cosmos VFS must be mounted and the path must exist. Directory items expose isFolder as true and their full path through value(event, ""path"").

## Loading script files
$ BreezeHost.RunFile(@""0:\Apps\main.breeze"");
RunFile is called from native C# and executes an edited script without rebuilding Windose.");

        AddPage("application", "Application Lifecycle", @"
## Headless process
$ let worker = process(""File Indexer"");
$ on worker.update {
$     // Runs once per scheduler update.
$ }
$ stopProcess(worker);

process names the current Breeze application and keeps it alive without a window. The update handler uses the normal operation limits. stopProcess closes its windows and removes it from Task Manager.

## Timers
$ let clock = timer(1000);
$ on clock.tick {
$     print(""tick"");
$ }
$ stopTimer(clock);
$ startTimer(clock);

Timers repeat at the requested millisecond interval. They run cooperatively during the owning process update. Properties: interval, active.

## Process messages
$ let app = process(""File Indexer"");
$ on app.message {
$     print(value(event, ""sender""));
$     print(value(event, ""name""));
$     print(value(event, ""data""));
$ }

Another application can send to it without keeping a reference:
$ let target = findProcess(""File Indexer"");
$ send(target, ""scan"", ""0:\\Documents"");

Messages are queued in order. Delivery is bounded per scheduler update so one sender cannot consume an entire frame.

## show
$ show(window)
Queues registration with WindowManager, focuses the window, and creates its taskbar button.

## close
$ close(window)
Posts a close message. WindowManager removes the window and taskbar button safely.

Call show after constructing controls and event handlers. Windowed and headless Breeze applications appear under Applications and Processes in Task Manager.");

        AddPage("debugging", "Errors and Debugging", @"
## print
$ print(value)
Writes a value and newline to serial output.

## Error windows
Lexer, parser, native-binding, and event errors are returned to BreezeHost without using .NET exceptions. The visible message appears in a Breeze Error window.

Errors include line numbers for invalid syntax. Unknown functions, variables, properties, and incorrect argument counts produce direct messages.");
    }

    public override string GetName() => "BreezeApiBrowser";
}
