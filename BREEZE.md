# Breeze

Breeze is Windose's interpreted GUI application language. Windose only
needs to be rebuilt when the runtime or native GUI controls change. Script
applications can be edited and relaunched without recompiling the kernel.

Place the main script at `0:\Apps\main.breeze`, then select **Run main.breeze** in
the Start menu. The Cosmos VFS must be mounted before loading a script file.

## Complete example

```text
let main = window("My App", 120, 100, 600, 400);
let root = windowRoot(main);

let tools = toolbar();
dock(root, tools, "top");
let toolbarAction = toolbarButton(tools, "Greet", 80);

let status = statusBar();
dock(root, status, "bottom");
let statusText = statusPanel(status, "Ready", 400);

let body = stackPanel("vertical");
dock(root, body, "fill");
let input = textField("World", 26);
let action = button("Greet", 100, 28);
stack(body, input);
stack(body, action);

on action.click {
    set statusText.text = "Hello, " + value(input, "text");
}

on toolbarAction.click {
    set statusText.text = "Toolbar clicked";
}

show(main);
```

Controls are created first, placed into a layout, connected to events, and
finally displayed with `show(main)`.

## Language statements

### `let`

```text
let name = expression;
```

Creates a named variable.

```text
let title = "Notes";
let width = 500;
let main = window(title, 100, 100, width, 300);
```

Reassign a variable by writing its name followed by `=`:

```text
let count = 0;
count = count + 1;
```

Use `set` to change a property on a GUI object.

### `set`

```text
set target.property = expression;
```

Changes a supported property and marks the control for redraw.

```text
set statusText.text = "Saved";
set main.canResize = false;
set action.visible = false;
```

Supported component properties:

| Property | Value | Meaning |
| --- | --- | --- |
| `text` | string | Visible text on the control. |
| `visible` | boolean | Shows or hides the control. |
| `width` | number | Resizes the control width. |
| `height` | number | Resizes the control height. |
| `fontSize` | number | Font height for panels, buttons, and text fields. Use `16` for the native bitmap font. |
| `canResize` | boolean | Enables manual resizing on a window. |
| `canMaximize` | boolean | Enables maximizing a window. |
| `canMinimize` | boolean | Enables minimizing a window. |

### `on`

```text
on target.eventName {
    statements
}
```

Registers statements that execute later when the event occurs.

```text
on saveButton.click {
    set statusText.text = "Saved";
}
```

Supported events:

| Target | Events |
| --- | --- |
| `Button` | `click` |
| `MenuItem` | `click` |
| `TreeView` | `select`, `doubleClick` |
| `ListView` | `select`, `doubleClick` |

For tree and list events, the selected item is temporarily available through
the variable `event`:

```text
on files.select {
    set statusText.text = value(event, "text") + " selected";
}
```

### Values and expressions

Breeze supports strings, numbers, booleans, variables, function calls,
parentheses, and the following operators:

| Operators | Purpose |
| --- | --- |
| `+`, `-`, `*`, `/` | Arithmetic. `+` also joins text. |
| `==`, `!=` | Equality. |
| `<`, `<=`, `>`, `>=` | Numeric comparison. |
| `!`, `&&`, `||` | Boolean logic. `&&` and `||` short-circuit. |

```text
let message = "Width: " + 640;
let enabled = true;
```

If either side of `+` is text, the values are joined as text. Otherwise the
numbers are added. Comments begin with `//` and continue to the end of the line.

### Control flow

```text
if (count > 10) {
    print("large");
} else if (count > 0) {
    print("small");
} else {
    print("empty");
}

while (count < 10) {
    count = count + 1;
}
```

Loops stop with an error after 10,000 iterations. A script or event handler is
also stopped after 100,000 executed operations.

### Functions

```text
function formatCount(count) {
    if (count == 1) {
        return "1 item";
    }
    return count + " items";
}

let label = formatCount(3);
```

Function parameters and variables declared inside a function are local to that
call. Functions may call other functions, up to 64 nested calls.

### Script lists

Script lists store arbitrary values and are separate from the visual
`ListView` control.

```text
let names = list();
listAdd(names, "Desktop");
listAdd(names, "Documents");
let first = listGet(names, 0);
listSet(names, 1, "Files");
let count = listCount(names);
let removed = listRemove(names, 0);
```

List indexes start at zero. Reading, changing, or removing an index outside the
list produces a Breeze error.

## Windows and layout

### `window(title, x, y, width, height)`

Creates a titled window and returns it. The window is not displayed until it is
passed to `show`.

```text
let main = window("File Browser", 100, 80, 700, 500);
```

`x` and `y` are screen coordinates. `width` and `height` are measured in pixels.

### `windowRoot(window)`

Creates the main `DockPanel` for a window, adds it to the window, and reserves
space for the title bar. Most applications should call this once.

```text
let root = windowRoot(main);
```

### `dockPanel()`

Creates an empty `DockPanel`. A dock panel places children against its top,
bottom, left, or right edge, or fills the remaining area.

```text
let body = dockPanel();
dock(root, body, "fill");
```

### `stackPanel(orientation)`

Creates a panel that places children one after another. `orientation` must be
`"vertical"` or `"horizontal"`.

```text
let buttons = stackPanel("horizontal");
stack(buttons, okButton);
stack(buttons, cancelButton);
```

### `dock(parent, child, position)`

Adds `child` to a `DockPanel`. Valid positions are `"top"`, `"bottom"`,
`"left"`, `"right"`, and `"fill"`.

```text
dock(root, menuBar, "top");
dock(root, statusBar, "bottom");
dock(root, content, "fill");
```

Dock order matters. Edge controls consume space in the order they are added.
Add the `"fill"` control last.

### `stack(parent, child)`

Adds a control to a `StackPanel`. The panel automatically chooses the child's
position based on its orientation.

```text
stack(form, nameField);
stack(form, saveButton);
```

### `add(parent, child)`

Adds a child directly to any component without performing dock or stack layout.
The child keeps its own coordinates and alignment settings. Prefer `dock` or
`stack` for normal application layouts.

## Basic controls

### `panel(text, height)`

Creates a fixed-height panel containing text. Panels are useful for headings,
messages, and simple status information.

```text
let heading = panel("System Settings", 28);
```

The initial width is replaced when the panel is docked or stretched.

### `button(text, width, height)`

Creates a clickable button and returns it.

```text
let apply = button("Apply", 80, 28);
on apply.click {
    set statusText.text = "Applied";
}
```

### `textField(text, height)`

Creates an editable text field with initial text. Read its current contents with
`value(field, "text")`.

```text
let name = textField("Untitled", 26);
let currentName = value(name, "text");
```

## Menus

### `menuBar()`

Creates an empty menu bar. Normally it is docked to the top of the root panel.

```text
let menus = menuBar();
dock(root, menus, "top");
```

### `menu(bar, text)`

Adds a drop-down page to a `MenuBar` and returns the new menu page.

```text
let fileMenu = menu(menus, "File");
```

### `menuItem(menu, text)`

Adds a clickable command to a menu page and returns the `MenuItem` so a click
handler can be attached.

```text
let closeItem = menuItem(fileMenu, "Close");
on closeItem.click {
    close(main);
}
```

## Toolbars and status bars

### `toolbar()`

Creates an empty toolbar. Dock it to the top of a `DockPanel`.

### `toolbarButton(toolbar, text, width)`

Adds a button to a toolbar and returns the button. It supports the normal
`click` event.

```text
let tools = toolbar();
dock(root, tools, "top");
let refresh = toolbarButton(tools, "Refresh", 80);
```

### `statusBar()`

Creates an empty status bar. Dock it to the bottom of the root panel.

### `statusPanel(statusBar, text, width)`

Adds a fixed-width text panel to a status bar and returns that panel. Change its
contents with `set panel.text = ...`.

```text
let status = statusBar();
dock(root, status, "bottom");
let objectCount = statusPanel(status, "0 objects", 140);
```

## Tree views

### `treeView()`

Creates an empty hierarchical tree control.

### `treeRoot(tree, text, tag)`

Adds a root item to a tree and returns the item. `tag` is application data,
usually a filesystem path.

```text
let folders = treeView();
let computer = treeRoot(folders, "My Computer", "0:\\");
```

### `treeChild(parentItem, text, tag)`

Adds a child below an existing tree item and returns the new child.

```text
let system = treeChild(computer, "System", "0:\\System");
treeChild(system, "Drivers", "0:\\System\\Drivers");
```

Tree events expose these item properties through `value(event, property)`:

| Property | Meaning |
| --- | --- |
| `text` | Displayed item name. |
| `path` | The value supplied as `tag`. |
| `expanded` | Whether the branch is expanded. |

## List views

### `listView(mode)`

Creates an empty file-style list. Valid modes are `"icons"`, `"smallicons"`,
`"list"`, and `"details"`.

```text
let files = listView("details");
```

### `listItem(list, text, tag, isFolder)`

Adds an item and returns it. `tag` normally contains its path. `isFolder`
controls whether the item uses folder behavior and appearance.

```text
listItem(files, "System", "0:\\System", true);
listItem(files, "notes.txt", "0:\\notes.txt", false);
```

### `listClear(list)`

Removes every item and clears the current selection.

### `listMode(list, mode)`

Changes an existing list's display mode. It accepts the same mode strings as
`listView`.

```text
listMode(files, "icons");
```

List events expose these item properties:

| Property | Meaning |
| --- | --- |
| `text` | Displayed item name. |
| `path` | The value supplied as `tag`. |
| `isFolder` | Whether the item represents a directory. |
| `type` | Displayed file type. |
| `size` | Displayed file size. |

## Scrolling and files

### `scrollView(content)`

Wraps a component in a scrollable viewport and returns the `ScrollView`. Dock
the returned scroll view, not the original content control.

```text
let files = listView("details");
let fileScroll = scrollView(files);
dock(body, fileScroll, "fill");
```

The scroll view automatically refreshes its content height for `TreeView` and
`ListView` controls.

### `loadDirectory(list, path)`

Clears a `ListView`, reads the specified directory through `System.IO`, and adds
its directories and files. The Cosmos VFS must be mounted and `path` must exist.

```text
loadDirectory(files, "0:\\");
```

An invalid path produces a Breeze error window instead of stopping the
window manager.

## Application and utility functions

### Headless processes

Use `process(name)` when an application needs to keep running without owning a
window. The returned process appears in Task Manager and can receive an update
event once per scheduler update.

```text
let worker = process("File Indexer");

on worker.update {
    // Perform a small amount of background work here.
}

stopProcess(worker);
```

`process(name)` also sets the current Breeze application's process name.
`stopProcess(process)` stops it and closes any windows owned by it. The readable
process properties are `name` and `running`.

Process updates are cooperative: the handler runs on the main scheduler and
must return quickly. It is not a separate thread.

Native processes are protected from Task Manager termination by default. A
native C# process can explicitly control this through `canTerminate`:

```csharp
process.canTerminate = false; // Protected from End Task.
process.canTerminate = true;  // User may end it from Task Manager.
```

Breeze application processes are terminable by default. Breeze scripts cannot
change `canTerminate` yet; protection is currently a native C# setting.

### Timers

`timer(milliseconds)` creates and starts a repeating timer. Bind its `tick`
event, and use `stopTimer` or `startTimer` to control it.

```text
let app = process("Clock");
let clock = timer(1000);

on clock.tick {
    print("tick");
}

stopTimer(clock);
startTimer(clock);
```

Timer properties are `interval` and `active`. Timer handlers are cooperative
and execute during their owning process update; they do not run on a separate
thread.

### Process messages

A receiving application binds the `message` event on its own process:

```text
let app = process("File Indexer");

on app.message {
    print(value(event, "sender"));
    print(value(event, "name"));
    print(value(event, "data"));
}
```

Another application can find it by name and enqueue a message:

```text
let indexer = findProcess("File Indexer");
send(indexer, "scan", "0:\\Documents");
```

`send` returns `false` if the target queue is full. Messages are delivered in
order with a bounded number processed per scheduler update so a busy sender
cannot monopolize a frame.

### `show(window)`

Registers the window with `WindowManager`, gives it focus, and adds its taskbar
button. Call it after constructing the controls and event handlers.

### `close(window)`

Posts a close message for the window. The window and its taskbar button are
removed by `WindowManager`.

### `value(object, property)`

Reads a supported property and returns it for use in another expression.

```text
set output.text = value(input, "text");
set selectedName.text = value(event, "text");
```

Common component properties are `text`, `width`, `height`, and `visible`.

### `print(value)`

Writes a value followed by a newline to the serial output. It is intended for
debugging scripts.

```text
print("Application started");
```

## Loading scripts from C #

Run source already held in memory:

```csharp
BreezeHost.RunSource(sourceText);
```

Run a file from the mounted VFS:

```csharp
BreezeHost.RunFile(@"0:\Apps\main.breeze");
```

Lexer, parser, runtime-validation, and event-handler errors are returned as
normal error values and shown in a `Breeze Error` window. Breeze does not use
.NET exceptions for script errors.
