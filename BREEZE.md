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

### Background programs

Choose **Run Background** in Breeze Editor and declare the process with
`scheduledProcess(name)`. It runs cooperatively as a headless background process
and appears in Task Manager without creating a window.

Scheduled Breeze processes use adaptive cooperative pacing. Update handlers run
at most every 100 ms, idle services execute at most every 250 ms, and queued
messages make the process eligible on the next desktop update. This avoids the
Cosmos thread scheduler wake storm while retaining interpreter operation limits.

```text
let worker = scheduledProcess("File Indexer");
let root = "0:\\Documents";

let files = getFiles(root);
let index = 0;
while (index < listCount(files)) {
    print(fileName(listGet(files, index)));
    index = index + 1;
}
```

`getDirectories(path)` and `getFiles(path)` return Breeze lists containing full
paths. `fileName(path)` returns only the final name.

Background programs may use computation, lists, filesystem functions, timers,
and process messages. Window and control functions are rejected at runtime and
stop only the offending background process. Use `send` to pass results to a GUI
application.

### System services

A service is a scheduled Breeze process with a registered system name,
dependency list, protection setting, and restart policy:

```text
let indexer = service("File Indexer", true, true);
serviceDependency("Storage");

on indexer.message {
    if (value(event, "name") == "scan") {
        let files = getFiles(value(event, "data"));
        log("Indexed " + listCount(files) + " files");
    }
}
```

The second argument enables up to three automatic restarts after runtime
failure. The third protects the service from normal stop requests. A startup
program can call `startService(path)`, `stopService(name)`,
`restartService(name)`, and `serviceState(name)`. `dependenciesReady()` reports
whether every name added through `serviceDependency` is running.

Use `tryFindProcess(name)` for optional discovery, `send` for directed IPC, and
`broadcast(name, data)` to publish to every other named Breeze process.
`request(target, name, data)` returns a correlation ID. A receiver calls
`reply(event, data)`; the response has an event name ending in `.reply` and its
`replyTo` property contains that ID.

At boot, Windose runs `0:\System\Services\startup.breeze`. It launches every
other `.breeze` file in that directory as a scheduled service, so adding a
service does not require another native kernel edit.

### Filesystem functions

Services can call `fileExists`, `directoryExists`, `createDirectory`,
`readFile`, `writeFile`, `deleteFile`, `deleteDirectory`, `copyFile`,
`copyDirectory`, `moveFile`, `moveDirectory`, `renamePath`, and `fileInfo`.
Copy, move, delete-directory, rename, and write operations take an explicit
boolean overwrite or recursive argument where applicable. Ordinary operation
failures return `false`.

`fileInfo(path)` exposes `name`, `path`, `isDirectory`, `size`, `childCount`,
`created`, and `modified` through `value`. `clock()`, `processCount()`, and
`log(value)` provide basic system diagnostics.

#### `watchPath(path, recursive) -> boolean`

Subscribes the current Breeze process to filesystem changes. `path` is
normalized before comparison. When `recursive` is `false`, only a change whose
path exactly matches the watched path is delivered. When it is `true`, changes
to that path and every descendant are delivered.

The function returns `true` after registering the subscription, or `false` if
no filesystem backend is active. Registering a watch keeps a windowless process
alive.

Changes arrive through the process `message` event. The message name is
`filesystem.changed`, its sender is `filesystem`, and its `data` value has:

- `type`: `Created`, `Modified`, `Deleted`, or `Moved`.
- `path`: the new or affected normalized path.
- `previousPath`: the old path for `Moved`; otherwise an empty string.

```text
let watcher = process("Document Watcher");
watchPath("0:\\Documents", true);

on watcher.message {
    if (value(event, "name") == "filesystem.changed") {
        let change = value(event, "data");
        log(value(change, "type") + ": " + value(change, "path"));
    }
}
```

#### `clearWatches() -> boolean`

Removes every filesystem watch owned by the current Breeze process. It takes no
arguments, does not affect other processes, and does not delete files. It always
returns `true`. Watches are also removed automatically when their process
terminates or fails.

## Modules and imports

Import another Breeze source file with an absolute or module-relative path:

```text
import "lib/logger.breeze";
import "0:\\System\\Services\\common.breeze";
```

Each normalized path executes once per application. Imported functions and
variables enter the current application scope. Circular imports are ignored
after the first visit, and import depth is capped at 32.

## Objects and iteration

Custom objects are case-insensitive property maps:

```text
let settings = object();
set settings.mode = "automatic";
objectSet(settings, "retries", 3);

for (key in objectKeys(settings)) {
    log(key + "=" + value(settings, key));
}
```

Available operations are `objectGet`, `objectSet`, `objectHas`, `objectRemove`,
`objectKeys`, and `objectCount`. `for (item in collection)` accepts lists and
objects; object iteration yields keys. `null` represents an absent value.

`tryReadFile(path)` returns an object containing `ok`, `value`, and `error`, so
expected failures can be handled without exception or kernel-level throw
behavior.

## Capabilities

Privileged native functions are grouped under `ui`, `filesystem.read`,
`filesystem.write`, `ipc`, `logging`, `process.inspect`, `process.control`, and
`service.control`. Use `capability(name)` to request one, `hasCapability(name)`
to inspect it, and `capabilities()` to enumerate granted names.

Scripts under `0:\System\Services` are trusted. Other applications receive a
small default set; native policy can grant or revoke additional access with
`BreezeCapabilityPolicy.Grant(path, capability)` and `Revoke`.

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

## Tutorials

### Create a windowed process

Save this as `0:\Apps\hello.breeze`, open it in Breeze Editor, and choose
**Run Script**:

```text
let app = process("Hello App");
let main = window("Hello", 140, 100, 480, 260);
let root = windowRoot(main);
let body = stackPanel("vertical");
let output = panel("Ready", 28);
let greet = button("Greet", 100, 28);

dock(root, body, "fill");
stack(body, output);
stack(body, greet);

on greet.click {
    set output.text = "Hello from Breeze";
}

show(main);
```

`process` gives the application a Task Manager name and deliberately keeps it
alive without windows. An Exit command should call `stopProcess(app)`. Omit the
`process` declaration when closing the final window should end the application
automatically. `show` registers the finished window.

### Create a headless process

Use a timer instead of doing work on every desktop update:

```text
let worker = process("Clock Logger");
let pulse = timer(1000);

on pulse.tick {
    log("Clock: " + clock());
}
```

Run it with **Run Script**. It creates no window, stays visible in Task Manager,
and logs once per second. Call `stopProcess(worker)` to end itself.

### Create a managed service

Save this as `0:\System\Services\indexer.breeze`, then choose **Run
Background**:

```text
let indexer = service("Document Indexer", true, false);
watchPath("0:\\Documents", true);

on indexer.message {
    if (value(event, "name") == "filesystem.changed") {
        let change = value(event, "data");
        log("Changed: " + value(change, "path"));
    }
}
```

The second `service` argument enables bounded crash restart. The third controls
protection from normal stop requests. Breeze services run cooperatively, so
message and timer handlers must return quickly. With persistent storage, the
boot `startup.breeze` launcher starts files in this directory automatically;
the current in-memory filesystem is cleared at restart.

### Send messages between programs

For a request/reply indexer, use this complete service:

```text
let indexer = service("Document Indexer", true, false);

on indexer.message {
    if (value(event, "name") == "scan") {
        let files = getFiles(value(event, "data"));
        reply(event, listCount(files));
    }
}
```

Create a client application:

```text
let client = process("Indexer Client");
let main = window("Indexer Client", 180, 140, 420, 220);
let root = windowRoot(main);
let scan = button("Scan Documents", 150, 28);
let status = panel("Idle", 28);
dock(root, scan, "top");
dock(root, status, "top");

on scan.click {
    let indexer = tryFindProcess("Document Indexer");
    if (indexer != null) {
        request(indexer, "scan", "0:\\Documents");
    } else {
        set status.text = "Service is not running";
    }
}

on client.message {
    if (value(event, "name") == "scan.reply") {
        set status.text = "Files: " + value(event, "data");
    }
}

show(main);
```

`request` assigns a correlation ID. `reply` sends that ID back through the
message's `replyTo` property. Use `send` when no response is needed and
`broadcast` for every named Breeze process.

### Store settings safely

```text
let path = "0:\\Documents\\settings.txt";
if (!fileExists(path)) {
    writeFile(path, "automatic", false);
}

let result = tryReadFile(path);
if (value(result, "ok")) {
    log("Mode: " + value(result, "value"));
} else {
    log(value(result, "error"));
}
```

Use `tryReadFile` for expected failures. Mutating filesystem calls return a
boolean, allowing a service to report failure without crashing.

### Create and import a module

Save `0:\Apps\lib\format.breeze`:

```text
function formatCount(value) {
    return "Items: " + value;
}
```

Import it from an application beside the `lib` directory:

```text
import "lib/format.breeze";
log(formatCount(12));
```

Relative imports resolve from the importing file. Modules execute once per
application and share its scope and capabilities.

### Grant a privileged capability

Normal applications cannot control system services. Native code can grant that
permission to a specific script before it runs:

```csharp
BreezeCapabilityPolicy.Grant(
    @"0:\Apps\service-manager.breeze",
    "service.control");
```

The script can then verify access with `hasCapability("service.control")` and
call `startService`, `stopService`, or `restartService`.

## Complete system API reference

### Service lifecycle

```text
service(name, restartOnFailure, protected) -> service
serviceDependency(name)                    -> boolean
dependenciesReady()                       -> boolean
startService(path)                         -> boolean
stopService(name)                          -> boolean
restartService(name)                       -> boolean
serviceState(name)                         -> string
```

`service` is valid only in a scheduled background program. A protected service
rejects `stopService`, while `restartService` remains available to the service
manager. `serviceState` returns `running`, `stopped`, or `missing`. Runtime
failures restart an opted-in service at most three times; the counter resets
after it has run for 30 seconds. `startService` queues work onto the process
manager and does not mutate process lists from the service thread.

At boot, `0:\System\Services\startup.breeze` starts every other Breeze file in
that directory. A service should use `dependenciesReady()` before beginning
work that depends on another registered service.

Service properties available through `value` are `name`, `state`, `running`,
`protected`, `restartOnFailure`, and `dependenciesReady`. Service events are
`update` and `message`.

### Process communication

```text
findProcess(name)             -> process or runtime error
tryFindProcess(name)          -> process or null
send(process, name, data)     -> boolean
broadcast(name, data)         -> delivered count
request(process, name, data)  -> correlation ID, or 0
reply(requestEvent, data)     -> boolean
```

Message queues hold at most 128 messages and deliver at most 32 events per
update. Message properties are `name`, `data`, `sender`, `id`, and `replyTo`.
A reply is named `<request-name>.reply`; `replyTo` contains the request ID.

### Files and directories

```text
fileExists(path)                                      -> boolean
directoryExists(path)                                 -> boolean
createDirectory(path)                                 -> boolean
readFile(path)                                        -> string or runtime error
tryReadFile(path)                                     -> result object
writeFile(path, content, overwrite)                   -> boolean
deleteFile(path)                                      -> boolean
deleteDirectory(path, recursive)                      -> boolean
copyFile(source, destination, overwrite)              -> boolean
copyDirectory(source, destination, overwrite)         -> boolean
moveFile(source, destination, overwrite)              -> boolean
moveDirectory(source, destination, overwrite)         -> boolean
renamePath(path, newName, overwrite)                  -> boolean
getFiles(path)                                        -> list
getDirectories(path)                                  -> list
fileName(path)                                        -> string
fileInfo(path)                                        -> metadata object
watchPath(path, recursive)                            -> boolean
clearWatches()                                        -> boolean
```

`tryReadFile` properties are `ok`, `value`, and `error`. `fileInfo` properties
are `name`, `path`, `isDirectory`, `size`, `childCount`, `created`, and
`modified`. Watches arrive through the process `message` event with the name
`filesystem.changed`; the data properties are `type`, `path`, and
`previousPath`. Watch subscriptions are removed automatically when the process
terminates.

Directory copying, moving, and recursive deletion include all descendants.
Root deletion and moving a directory into itself are rejected. These calls use
the same `IWindoseFileSystem` contract as Explorer and the editor.

### Objects, modules, and iteration

```text
import "module.breeze";
let value = null;
let data = object();
objectGet(data, key)          -> value or null
objectSet(data, key, value)   -> value
objectHas(data, key)          -> boolean
objectRemove(data, key)       -> boolean
objectKeys(data)              -> list
objectCount(data)             -> number

for (item in collection) {
    // collection may be a list or object
}
```

Object keys are case-insensitive. `set data.property = value` writes an object
property, and `value(data, "property")` reads one. For-in over an object yields
its keys. Imports execute once, resolve relative to the importing module, share
the application scope, and have a maximum nesting depth of 32.

### System registry

The registry stores named system and application settings in a case-insensitive
hierarchy. Use `/` between key segments. Applications should keep their own
settings below `Apps/<application name>`:

```text
registryDefine("Apps/Notes/Autosave", true,
    "Save the current document automatically.", false);
registrySet("Apps/Notes/Autosave", false);

let enabled = registryGet("Apps/Notes/Autosave");
let exists = registryExists("Apps/Notes/Autosave");
let keys = registryKeys("Apps/Notes");
let details = registryInfo("Apps/Notes/Autosave");
```

`registryDefine(key, defaultValue, description, requiresRestart)` creates a
documented setting if it does not exist. Calling it again updates the setting's
metadata without replacing the user's current value. `registrySet` creates an
undocumented custom setting when necessary and saves it immediately.
`registryDelete` removes a setting, `registrySave` explicitly flushes all
settings, and `registryRestartRequired` reports whether a restart-required
value has changed since boot.

`registryInfo` returns an object with `key`, `value`, `defaultValue`,
`description`, `requiresRestart`, and `builtIn`. `registryKeys(prefix)` returns
every matching key below a prefix, so a settings program can discover custom
entries created while the system is running.

Normal applications receive `registry.read` and `registry.custom.write`, which
allow reads and writes outside `System/`. Changing a protected `System/...`
setting requires `registry.write`. System services are trusted, or native code
may grant it to a settings application:

```csharp
BreezeCapabilityPolicy.Grant(@"0:\Apps\settings.breeze", "registry.write");
```

The desktop background color updates immediately:

```text
registrySet("System/Desktop/BackgroundColor", "#202840");
```

`System/Desktop/Wallpaper` and `System/Desktop/WallpaperMode` are reserved for
bitmap wallpaper support. Display keys record the desired boot mode:

```text
registrySet("System/Display/Width", 1920);
registrySet("System/Display/Height", 1080);
registrySet("System/Display/BitsPerPixel", 32);
```

GOP selects the framebuffer before Windose runs, so these display values cannot
resize the current framebuffer. They set `registryRestartRequired()` to true and
must later be consumed by the boot configuration or display bootstrap during a
restart. `System/Display/CurrentWidth` and `CurrentHeight` are read-only runtime
observations and are not persisted.

Registry storage uses `IWindoseFileSystem` at `0:\System\registry.db`. With the
current temporary filesystem, values survive application restarts but not a
machine reboot. They become durable automatically when a persistent filesystem
backend replaces the temporary one.

### Capabilities and system information

```text
capability(name)       -> boolean
hasCapability(name)    -> boolean
capabilities()         -> list
clock()                -> milliseconds
processCount()         -> number
log(value)             -> true
```

Capability names are `ui`, `filesystem.read`, `filesystem.write`,
`registry.read`, `registry.custom.write`, `registry.write`, `ipc`, `logging`,
`process.inspect`, `process.control`, and `service.control`.
Applications receive UI, filesystem, IPC, logging, and inspection access by
default. Process and service control require a native grant. Scripts under
`0:\System\Services` are trusted. Native hosts grant or revoke access through
`BreezeCapabilityPolicy.Grant(path, capability)` and `Revoke`.

## Temporary filesystem

Windose currently mounts an in-memory disk at `0:\`. It contains `Apps`,
`Documents`, and `System` directories. Files saved there remain available to
the editor, File Explorer, file dialogs, and Breeze processes until reboot.

Native file access goes through `IWindoseFileSystem` and
`FileSystemManager.Current`. When persistent filesystem support is ready,
implement that interface and replace the boot initialization:

```csharp
FileSystemManager.Initialize(new CosmosFileSystem());
```

No Breeze, editor, dialog, or Explorer code needs to change.

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
