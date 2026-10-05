[README.md](https://github.com/user-attachments/files/33079638/README.md)
# Windose

A hobby desktop operating system written in C# on [Cosmos](https://github.com/CosmosOS/Cosmos) (NativeAOT). Windose boots to a windowed desktop with a taskbar and Start menu, a file explorer, a command prompt, a text editor, and its own scripting language for writing GUI applications and background services.

<!-- TODO: add a screenshot here, e.g. ![Windose desktop](docs/screenshot.png) -->

## Features

- **Desktop shell:** window manager with a taskbar, Start menu, desktop icons (persisted between sessions), draggable and resizable windows, focus handling and per-window threading
- **Compositor:** double-buffered rendering through a `DirectBitmap`/`DrawArray` pipeline with dirty-flag redraw, protected by a `SpinLock`-guarded message queue
- **GUI toolkit:** `Component` / `Panel` / `Window` hierarchy with docking and stack layouts, buttons, text fields, list and tree views, menus, toolbars, status bars and scroll views
- **Breeze:** an interpreted GUI application language, so apps can be written and relaunched without rebuilding the kernel (see [BREEZE.md](BREEZE.md))
- **Command prompt:** extensible shell with a command registry, including filesystem commands and a diskpart-style disk tool (see [COMMANDS.md](COMMANDS.md))
- **Virtual CPU:** a bytecode VM with a register architecture and an `INT 0x80` syscall mechanism, used to run `.kexe` executables
- **Filesystem:** Cosmos VFS with disk-backed storage, GPT/MBR partition handling and FAT32 formatting
- **Disk Management:** GUI utility showing disks, partitions and a partition bar
- **System registry:** hierarchical, case-insensitive key/value store with documented defaults, restart-required flags and on-disk persistence
- **File associations:** icon and handler lookup per extension, stored in the registry
- **Accounts and security:** user account system with salted password hashing and session management, using a from-scratch SHA-256 (the BCL crypto stack is not available under Cosmos)
- **Process and service model:** Task Manager, cooperative processes, timers, IPC messaging, and managed background services with dependencies and restart policies

## Architecture in short

Every program in Windose is a `Window`. The desktop window manager draws windows and the process manager updates them on the main thread. Heavier work, such as running shell commands, runs on its own `System.Threading.Thread` (Cosmos is single core with preemptive scheduling, so keep these short and avoid `lock`; use `SpinLock`).

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
| `Windose.csproj`, `Windose.slnx` | Project and solution (Cosmos SDK) |
| `System/` | Core system code |
| `Programs/` | Built-in programs |
| `Bootloader/` | Boot code <!-- TODO: describe --> |
| `Installer/` | Installer <!-- TODO: describe --> |
| `Resources/` | Embedded fonts, icons, cursors and wallpapers |
| `src/C/` | Native C sources <!-- TODO: describe --> |
| `BREEZE.md`, `BREEZE_API.html` | Breeze language guide and API reference |
| `COMMANDS.md` | Command prompt guide and how to add commands |

## Building

Requirements:

- .NET 10 SDK
- Cosmos SDK 3.0.89 (restored through NuGet, see `NuGet.Config`)

<!-- TODO: exact build and run steps. Fill in what you actually do: build command, output ISO location, which emulator (QEMU?) and launch flags, UEFI/OVMF requirement, disk image setup. -->

```sh
dotnet build Windose.csproj
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

See [COMMANDS.md](COMMANDS.md) for aliases, path resolution and quoting.

## Status

Windose is a personal, work-in-progress project. Expect rough edges, missing features and breaking changes between commits.

<!-- TODO: add a short roadmap, e.g. dirty-rectangle compositing, file/program associations for executables, more system programs. -->

## Credits

- Built on [Cosmos](https://github.com/CosmosOS/Cosmos)
- Architectural inspiration from [AuraOS](https://github.com/valentinbreiz) by valentinbreiz

## Third-party assets and licensing

The `Resources/` folder embeds fonts, icons and wallpapers. Some of these appear to be derived from Microsoft Windows (for example Arial, MS Sans Serif and classic Windows icons) and are not covered by this project's license.

<!-- TODO before making the repo widely public: confirm the source and license of every asset in Resources/, replace or remove anything you cannot redistribute, and list attributions here. -->

## License

<!-- TODO: choose a license (MIT is the usual pick for a hobby OS) and add a LICENSE file. A repo with no license is "all rights reserved" by default. -->
