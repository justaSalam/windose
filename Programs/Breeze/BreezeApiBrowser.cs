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
        AddCategory("Tutorials", "tutorials", new[]
        {
            "tutorial-window", "tutorial-headless", "tutorial-service", "tutorial-ipc",
            "tutorial-storage", "tutorial-module", "tutorial-capability"
        });
        AddCategory("Language", "language", new[] { "values", "events", "properties" });
        AddCategory("Windows and Layout", "layout", new[] { "window", "dock", "stack" });
        AddCategory("Controls", "controls", new[] { "basic-controls", "menus", "bars" });
        AddCategory("Data Views", "data-views", new[] { "tree", "list", "scroll" });
        AddCategory("Files and Runtime", "runtime", new[] { "files", "system-reference", "application", "debugging" });
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

        AddPage("tutorials", "Tutorials", @"
## Build complete Breeze programs
These tutorials are small programs you can run and modify. They cover windowed applications, background work, services, messages, storage, modules, and privileged APIs.

- Run Script starts a normal windowed or headless process.
- Run Background starts a managed service.
- Save applications under 0:\Apps and services under 0:\System\Services.

The current in-memory filesystem is cleared when Windose restarts.");

        AddPage("tutorial-window", "Tutorial: Windowed Process", @"
## Create a small application
Save as 0:\Apps\hello.breeze and choose Run Script.

$ let app = process(""Hello App"");
$ let main = window(""Hello"", 140, 100, 480, 260);
$ let root = windowRoot(main);
$ let body = stackPanel(""vertical"");
$ let output = panel(""Ready"", 28);
$ let greet = button(""Greet"", 100, 28);
$ dock(root, body, ""fill"");
$ stack(body, output);
$ stack(body, greet);
$ on greet.click {
$     set output.text = ""Hello from Breeze"";
$ }
$ show(main);

process supplies the Task Manager name and keeps the program alive without windows. Call stopProcess(app) from its Exit command, or omit process when final-window closure should end it automatically. Create and arrange controls before calling show.");

        AddPage("tutorial-headless", "Tutorial: Headless Process", @"
## Run work without a window
Save the script and choose Run Script.

$ let worker = process(""Clock Logger"");
$ let pulse = timer(1000);
$ on pulse.tick {
$     log(""Clock: "" + clock());
$ }

The process remains alive without a window, while the timer schedules one log entry per second. Timers are preferable to expensive work during every desktop update.");

        AddPage("tutorial-service", "Tutorial: Managed Service", @"
## Watch a directory
Save as 0:\System\Services\indexer.breeze and choose Run Background.

$ let indexer = service(""Document Indexer"", true, false);
$ watchPath(""0:\\Documents"", true);
$ on indexer.message {
$     if (value(event, ""name"") == ""filesystem.changed"") {
$         let change = value(event, ""data"");
$         log(""Changed: "" + value(change, ""path""));
$     }
$ }

The second service argument enables bounded crash restart. The third controls whether the service is protected; false leaves this example stoppable. Keep handlers short because Breeze services run cooperatively.");

        AddPage("tutorial-ipc", "Tutorial: Process Messages", @"
## Service receiver
$ let indexer = service(""Document Indexer"", true, false);
$ on indexer.message {
$     if (value(event, ""name"") == ""scan"") {
$         let files = getFiles(value(event, ""data""));
$         reply(event, listCount(files));
$     }
$ }

## Windowed client
$ let client = process(""Indexer Client"");
$ let main = window(""Indexer Client"", 180, 140, 420, 220);
$ let root = windowRoot(main);
$ let scan = button(""Scan Documents"", 150, 28);
$ let status = panel(""Idle"", 28);
$ dock(root, scan, ""top"");
$ dock(root, status, ""top"");
$ on scan.click {
$     let indexer = tryFindProcess(""Document Indexer"");
$     if (indexer != null) {
$         request(indexer, ""scan"", ""0:\\Documents"");
$     } else {
$         set status.text = ""Service is not running"";
$     }
$ }
$ on client.message {
$     if (value(event, ""name"") == ""scan.reply"") {
$         set status.text = ""Files: "" + value(event, ""data"");
$     }
$ }
$ show(main);

Use send when no response is needed. request and reply preserve a correlation ID so the client can match responses.");

        AddPage("tutorial-storage", "Tutorial: Store Settings", @"
## Read a setting without crashing
$ let path = ""0:\\Documents\\settings.txt"";
$ if (!fileExists(path)) {
$     writeFile(path, ""automatic"", false);
$ }
$ let result = tryReadFile(path);
$ if (value(result, ""ok"")) {
$     log(""Mode: "" + value(result, ""value""));
$ } else {
$     log(value(result, ""error""));
$ }

tryReadFile returns ok, value, and error. Filesystem mutations return booleans for failures that the program can handle.");

        AddPage("tutorial-module", "Tutorial: Import a Module", @"
## Reuse a function
Save this as 0:\Apps\lib\format.breeze:

$ function formatCount(value) {
$     return ""Items: "" + value;
$ }

Import it from 0:\Apps\main.breeze:

$ import ""lib/format.breeze"";
$ log(formatCount(12));

Relative imports resolve beside the importing file. Each normalized module executes once per application.");

        AddPage("tutorial-capability", "Tutorial: Grant a Capability", @"
## Permit a privileged operation
Capabilities are granted by native Windose code before the script starts:

$ BreezeCapabilityPolicy.Grant(
$     @""0:\Apps\service-manager.breeze"",
$     ""service.control"");

The Breeze script can verify access with hasCapability(""service.control"") and then use startService, stopService, or restartService. Grant only the narrow capability the program needs.");

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

Runtime validation errors are returned and displayed in a Breeze Error window rather than escaping into WindowManager.

## Modules
$ import ""lib/logger.breeze"";
Imports execute once per application. Relative paths resolve beside the importing file. Imported functions and variables join the application scope; cycles are bounded safely.

## Objects and for-in
$ let settings = object();
$ set settings.mode = ""automatic"";
$ for (key in objectKeys(settings)) {
$     log(key + ""="" + value(settings, key));
$ }
Objects are case-insensitive property maps. objectGet, objectSet, objectHas, objectRemove, objectKeys, and objectCount are available. for-in accepts lists and objects. null represents no value.

## Capabilities
$ capability(""filesystem.write"");
$ hasCapability(""filesystem.write"");
$ capabilities();
Native APIs are grouped into ui, filesystem.read, filesystem.write, ipc, logging, process.inspect, process.control, and service.control. System service scripts are trusted; native BreezeCapabilityPolicy grants additional access to applications.");

        AddPage("files", "Filesystem", @"
## loadDirectory
$ loadDirectory(list, path)
Clears a ListView and fills it with directories and files from Windose storage.

$ loadDirectory(files, ""0:\\"");

The path must exist. Directory items expose isFolder as true and their full path through value(event, ""path"").

## File operations
$ writeFile(""0:\\Documents\\status.txt"", ""ready"", true);
$ let text = readFile(""0:\\Documents\\status.txt"");
$ copyFile(""0:\\Documents\\status.txt"", ""0:\\Documents\\status.bak"", true);
$ moveFile(""0:\\Documents\\status.bak"", ""0:\\Apps\\status.bak"", true);
$ deleteFile(""0:\\Apps\\status.bak"");

Also available: fileExists, directoryExists, createDirectory, deleteDirectory, copyDirectory, moveDirectory, renamePath, and fileInfo. Mutating calls return true or false. fileInfo exposes name, path, isDirectory, size, childCount, created, and modified through value.

tryReadFile(path) returns an object with ok, value, and error instead of stopping the service when the file is absent.

## watchPath
$ watchPath(path, recursive) -> boolean
Subscribes the current process to filesystem changes. path is normalized under 0:\. false matches only that exact path; true also matches every descendant. It returns false only when no filesystem backend is active.

Matching changes arrive in the process message event with the name filesystem.changed and sender filesystem. event.data exposes:
- type: Created, Modified, Deleted, or Moved
- path: new or affected normalized path
- previousPath: old path for a move, otherwise empty

$ let watcher = process(""Document Watcher"");
$ watchPath(""0:\\Documents"", true);
$ on watcher.message {
$     if (value(event, ""name"") == ""filesystem.changed"") {
$         let change = value(event, ""data"");
$         log(value(change, ""type"") + "": "" + value(change, ""path""));
$     }
$ }

## clearWatches
$ clearWatches() -> boolean
Removes every watch owned by the current process. It does not affect other processes or delete files. It always returns true. Windose also clears watches automatically when the process terminates.

## Loading script files
$ BreezeHost.RunFile(@""0:\Apps\main.breeze"");
RunFile is called from native C# and executes an edited script without rebuilding Windose.

Windose currently uses a temporary in-memory disk at 0:\. Its files survive while the system is running but are cleared at reboot. Native code talks to IWindoseFileSystem through FileSystemManager.Current, so a persistent Cosmos adapter can replace the temporary backend without changing Breeze or its programs.");

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

tryFindProcess returns an empty value instead of stopping the caller when a process is absent. broadcast(name, data) sends to every other named Breeze process and returns the delivery count.

request(target, name, data) returns a correlation ID. reply(event, data) sends a response whose replyTo property contains that ID.

## System services
$ let indexer = service(""File Indexer"", true, true);
$ serviceDependency(""Storage"");
$ on indexer.message {
$     log(value(event, ""data""));
$ }

service(name, restartOnFailure, protected) declares the current scheduled program as a service. A startup program uses startService(path), stopService(name), restartService(name), and serviceState(name). dependenciesReady() checks registered dependencies. Failed services restart at most three times, preventing an endless crash loop.

Windose automatically runs 0:\System\Services\startup.breeze during boot. It launches every other Breeze file in that directory.

## Background programs
$ let worker = scheduledProcess(""File Indexer"");
$ let files = getFiles(""0:\\Documents"");
$ let index = 0;
$ while (index < listCount(files)) {
$     print(fileName(listGet(files, index)));
$     index = index + 1;
$ }

Start this source with Run Background in Breeze Editor. Breeze background processes run cooperatively rather than creating Cosmos threads. Update handlers run at most every 100 ms, idle services execute at most every 250 ms, and messages make them eligible on the next desktop update. getDirectories and getFiles return lists of full paths. GUI functions remain unavailable. Send results to a normal Breeze GUI process with send.

## show
$ show(window)
Queues registration with WindowManager, focuses the window, and creates its taskbar button.

## close
$ close(window)
Posts a close message. WindowManager removes the window and taskbar button safely.

Call show after constructing controls and event handlers. Windowed and headless Breeze applications appear under Applications and Processes in Task Manager.");

        AddPage("system-reference", "Complete System API", @"
## Services
$ service(name, restartOnFailure, protected) -> service
$ serviceDependency(name) -> boolean
$ dependenciesReady() -> boolean
$ startService(path) -> boolean
$ stopService(name) -> boolean
$ restartService(name) -> boolean
$ serviceState(name) -> running, stopped, or missing

service is valid in scheduled programs. Failed opted-in services restart at most three times. Protected services reject normal stop requests. At boot, startup.breeze launches every other script under 0:\System\Services. Service properties: name, state, running, protected, restartOnFailure, dependenciesReady. Events: update, message.

## IPC
$ findProcess(name) -> process or runtime error
$ tryFindProcess(name) -> process or null
$ send(process, name, data) -> boolean
$ broadcast(name, data) -> delivered count
$ request(process, name, data) -> correlation ID or 0
$ reply(requestEvent, data) -> boolean

Messages expose name, data, sender, id, and replyTo. Replies are named request-name.reply. Queues hold 128 messages and deliver at most 32 per update.

## Filesystem
$ fileExists(path) / directoryExists(path)
$ createDirectory(path)
$ readFile(path) / tryReadFile(path)
$ writeFile(path, content, overwrite)
$ deleteFile(path) / deleteDirectory(path, recursive)
$ copyFile(source, destination, overwrite)
$ copyDirectory(source, destination, overwrite)
$ moveFile(source, destination, overwrite)
$ moveDirectory(source, destination, overwrite)
$ renamePath(path, newName, overwrite)
$ getFiles(path) / getDirectories(path) / fileName(path)
$ fileInfo(path)
$ watchPath(path, recursive) / clearWatches()

Mutations return booleans. tryReadFile exposes ok, value, error. fileInfo exposes name, path, isDirectory, size, childCount, created, modified. Watches arrive as filesystem.changed messages; data exposes type, path, previousPath. Watches are removed automatically at process termination.

## Objects and modules
$ import ""module.breeze"";
$ let data = object();
$ objectGet(data, key)
$ objectSet(data, key, value)
$ objectHas(data, key)
$ objectRemove(data, key)
$ objectKeys(data)
$ objectCount(data)
$ for (item in collection) { }
$ null

Objects are case-insensitive maps. For-in accepts lists and objects. Imports are relative, execute once, share application scope, and are limited to 32 nested imports.

## Capabilities and system information
$ capability(name) / hasCapability(name) / capabilities()
$ clock() / processCount() / log(value)

Capabilities: ui, filesystem.read, filesystem.write, ipc, logging, process.inspect, process.control, service.control. Normal applications receive UI, filesystem, IPC, logging, and inspection access. Process and service control need native grants. System service scripts are trusted.");

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
