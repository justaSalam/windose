[README.md](https://github.com/user-attachments/files/33079638/README.md)
# Windose

A hobby desktop operating system written in C# on [Cosmos](https://github.com/CosmosOS/Cosmos) (NativeAOT). Windose boots to a windowed desktop with a taskbar and Start menu, a file explorer, a command prompt, a text editor, and its own scripting language for writing GUI applications and background services.

## Features

- **Desktop shell:** window manager with a taskbar, Start menu, desktop icons, draggable and resizable windows, focus handling and per-window threading
- **Compositor:** double-buffered rendering through a `DirectBitmap` pipeline with dirty-flag redraw
- **GUI toolkit:** `Component` / `Window` hierarchy with docking and stack layouts, buttons, text fields, list and tree views, menus, toolbars, status bars and scroll views
- **Breeze:** an interpreted GUI application language, so apps can be written and relaunched without rebuilding the kernel (Obsolete, replace with Lua) (see [BREEZE.md](BREEZE.md))
- **Command prompt:** extensible shell with a command registry, including filesystem commands and a diskpart-style disk tool (see [COMMANDS.md](COMMANDS.md))
- **Filesystem:** Cosmos VFS with disk-backed storage, GPT/MBR partition handling and FAT32 formatting
- **System registry:** hierarchical, case-insensitive key/value store with documented defaults, restart-required flags and on-disk persistence
- **Process and service model:** Task Manager, cooperative processes, timers, IPC messaging, and managed background services with dependencies and restart policies

## Architecture in short

Every program in Windose is a `Window`. The desktop window manager draws windows and the process manager updates them on the main thread. Heavier work, such as running shell commands, runs on its own `System.Threading.Thread`.

```
Kernel.cs                  entry point (Windose.Kernel)
 ├─ Compositor / DWM       window drawing, input, focus, taskbar
 ├─ GUI toolkit            Component, Panel, Window, DockPanel, ListView, ...
 ├─ Process manager        window updates, processes, services, Task Manager
 ├─ Breeze runtime         lexer, parser, interpreter, capability policy
 ├─ Virtual CPU            bytecode VM, .kexe loader, INT 0x80 syscalls
 ├─ VFS / storage          Cosmos VFS, partitions, GPT/MBR, FAT32
 └─ System services        registry, accounts, file associations
```

## Repository layout

| Path | Contents |
| --- | --- |
| `Kernel.cs` | Kernel entry point |
| `System/` | Core system code |
| `Programs/` | Built-in programs |
| `Resources/` | Embedded fonts, icons, cursors and wallpapers | `INSTALLED BY THE [WINDOSE INSTALLER](https://github.com/justaSalam/Windose-Installer)`
| `BREEZE.md`, `BREEZE_API.html` | Breeze language guide and API reference |

## Building

Requirements:

- .NET 10 SDK
- Cosmos SDK 3.0.89 (restored through NuGet, see `NuGet.Config`)

```sh
cosmos build
```

## Writing apps with Breeze

Put a script at `/mnt/Apps/main.breeze` and choose **Run main.breeze** in the Start menu:

```breeze
let main = window("Hello", 120, 100, 400, 200);
let root = windowRoot(main);

let body = stackPanel("vertical");
dock(root, body, "fill");

let output = panel("Ready", 28);
let greet = button("Greet", 100, 28);
stack(body, output);
stack(body, greet);

on greet.click {
    set output.text = "Hello from Breeze";
}

show(main);
```

Breeze also covers processes, timers, IPC (`send`, `request`, `reply`, `broadcast`), file I/O with change watching, imports, objects, registry access, and capability-based permissions for privileged calls. Full reference: [BREEZE.md](BREEZE.md).

## Adding a shell command

```csharp
CommandRegistry.Register(
    "hello",
    "Greets a user.",
    "hello [name]",
    (context, arguments) =>
    {
        string name = arguments.Length == 0 ? "World" : arguments[0];
        context.WriteLine("Hello, " + name);
    });
```

## Status

Windose is a personal, work-in-progress project. Expect rough edges, missing features and breaking changes between commits.

## Credits

- Built on [Cosmos](https://github.com/CosmosOS/Cosmos)
- Architectural inspiration from [AuraOS](https://github.com/valentinbreiz) by valentinbreiz

## Third-party assets and licensing

The `Resources/` folder embeds fonts, icons and wallpapers. Some of these are derived from Microsoft Windows (for example Arial, MS Sans Serif and classic Windows icons) and are not covered by this project's license.
