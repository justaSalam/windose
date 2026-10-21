# Windose

A hobby desktop operating system written in C# on [Cosmos](https://github.com/CosmosOS/Cosmos) (NativeAOT). Windose boots to a windowed desktop with a taskbar and Start menu, a file explorer, a command prompt, a text editor, and an ELF loader that lets you run native x86-64 programs written in C — without rebuilding the kernel.

## Features

- **Install to disk using the [Windose Installer](https://github.com/justaSalam/Windose-Installer)**
- **Desktop shell:** window manager with a taskbar, Start menu, desktop icons, draggable and resizable windows, focus handling and per-window threading
- **Compositor:** double-buffered rendering through a `DirectBitmap` pipeline with dirty-flag redraw
- **GUI toolkit:** `Component` / `Window` hierarchy with docking and stack layouts, buttons, text fields, list and tree views, menus, toolbars, status bars and scroll views
- **ELF loader:** runs x86-64 ELF programs written in C — each with its own thread, handle table, event queue and memory quota — without a kernel rebuild (see [ELF Loader](#elf-loader))
- **Lua scripting:** embedded Lua runtime (`Cosmos.Executable.Lua`) for shell automation and desktop macros
- **Command prompt:** extensible shell with a command registry, including filesystem commands and a diskpart-style disk tool (see [COMMANDS.md](COMMANDS.md))
- **Filesystem:** Cosmos VFS with disk-backed storage, GPT/MBR partition handling and FAT32 formatting
- **System registry:** hierarchical, case-insensitive key/value store with documented defaults, restart-required flags and on-disk persistence
- **Process and service model:** Task Manager, cooperative processes, timers, IPC messaging, and managed background services with dependencies and restart policies
- **Networking:** DHCP client, TLS 1.3 stack (P-256, AES-GCM, custom record layer), HTTP/S fetch, cookie jar
- **User accounts and lock screen:** multi-user login with a lock manager

## ELF Loader

Windose includes a self-contained x86-64 ELF loader (`System/ELF/`) that runs native programs written in C directly inside the Cosmos kernel — no dynamic linker, no separate process space.

### How it works

- `ElfRunner.Spawn` parses a PIE or static ELF64 binary, copies it into a pinned heap allocation, applies relocations (RELATIVE, 64, GLOB_DAT, JUMP_SLOT, RELR), and starts the program on its own `Thread`.
- Each program gets a **PID**, a **handle table**, a per-program **event queue**, and a **memory quota** (default 64 MB).
- Programs communicate with the kernel through a versioned `KernelApi` struct (v10) passed at startup — a table of `[UnmanagedCallersOnly]` function pointers.
- OS-specific calls (window creation, file I/O, etc.) are routed through `IElfHost`, implemented by `WindoseBridge`.

### Bundled programs

The following ELF programs are embedded in the kernel image and written to `/mnt/Programs/` at boot:

| Program | Description |
|---|---|
| `hello.elf` | Hello-world console program |
| `window.elf` | Minimal windowed app |
| `canvas.elf` | Pixel-drawing canvas demo |
| `explorer.elf` | Full file explorer (menu bar, tree, details list, copy/cut/paste, context menus) |
| `calc.elf` | Calculator |
| `search.elf` | File search |
| `run.elf` | Run-dialog launcher (Alt+R) |
| `pager.elf` | Text file pager |
| `widgets.elf` | Widget gallery (all supported controls) |
| `memhog.elf` | Memory stress test |
| `spin.elf` | CPU spin test |
| `libctest.elf` | libc unit tests |

### Kernel API (k_* calls)

Programs include `kernel.h` and call `k_*` wrappers. The full API covers:

- **Output:** `k_write`
- **Windows:** `k_create_window`, `k_resize_window`, `k_close`
- **Widgets:** buttons, labels, text fields, checkboxes, radio buttons, sliders, progress bars, combo boxes, list boxes, tab controls, tree views, toolbars, status bars, popup menus, canvases, grids, color pickers, numeric spinners, calendars, menu bars, message boxes, file dialogs
- **Events:** `k_poll_event` (click, close, key, mouse move/down/up, focus, drop)
- **File I/O:** `k_read_file`, `k_write_file`, `k_list_dir`, `k_stat_file`, `k_make_dir`, `k_delete_path`, `k_rename_path`
- **Drives:** `k_list_drives`, `k_drive_stat`
- **Processes:** `k_spawn`, `k_kill`, `k_yield`, `k_pid`
- **Desktop:** `k_desktop_count`, `k_desktop_current`, `k_desktop_switch`, `k_window_set_desktop`
- **Shell integration:** `k_launch_app`, `k_open_file`, `k_notify_send`
- **Trash:** `k_trash_delete`, `k_trash_list`, `k_trash_restore`, `k_trash_empty`
- **Misc:** `k_sleep`, `k_ticks`

### Writing a native program

```c
// hello.c
#include <stdio.h>
#include "kernel.h"

int main(int argc, char **argv) {
    int win = k_create_window(400, 200, "Hello");
    int btn = k_create_button(win, 10, 10, 120, 30, "Click me");

    KEvent e;
    while (k_poll_event(&e) >= 0) {
        if (e.type == EV_CLICK && e.handle == btn)
            k_write("clicked!\n", 9);
        if (e.type == EV_CLOSE) break;
    }
    k_close(win);
    return 0;
}
```

Build on Linux/WSL:

```sh
cd System/ELF/programs
sh build.sh hello        # output: bin/hello.elf
```

Or on Windows (LLVM, experimental):

```bat
build.bat hello
```

Then launch at runtime:

```csharp
string err;
ElfProgram p = ElfRunner.Spawn(File.ReadAllBytes("/mnt/Programs/hello.elf"), "hello", null, out err);
if (p == null) SystemLogger.WriteLine("ELF", "Launch failed: " + err, ConsoleMessageType.Error);
```

### Adding a kernel API function

1. **`programs/include/kernel.h`** — add a new field at the **end** of `KernelApi` and a `k_*` wrapper macro.
2. **`System/ELF/ElfRunner.cs`** — add the matching field at the end of `KernelApi`, write an `[UnmanagedCallersOnly]` handler, and assign it in `FillApi`. Bump `ApiVersion`.
3. If the call needs OS access, add a method to `IElfHost` and implement it in `WindoseBridge.cs`.

### Limitations

- Programs run in **ring 0** on kernel threads — a buggy program can crash or corrupt the kernel.
- Kill is **cooperative**: a program stuck in a pure compute loop that never calls the kernel cannot be stopped. Call `k_yield()` in hot loops.
- No floating-point `printf`, no global constructors, no TLS, no `fopen` (use `k_read_file` / `k_write_file`), no threads inside a program.
- Older `bin/*.elf` files built against a previous API version will exit 127 until rebuilt.

## Architecture

Every program in Windose is a `Window`. The desktop window manager draws windows and the process manager updates them on the main thread. Heavier work, such as running shell commands or ELF programs, runs on its own `System.Threading.Thread`.

```
Kernel.cs                  entry point (Windose.Kernel)
 ├─ Compositor / DWM       window drawing, input, focus
 ├─ GUI toolkit            Component, Window
 ├─ Process manager        window updates, processes, services, Task Manager
 ├─ ELF loader             ElfRunner, ElfReader, WindoseBridge, IElfHost
 ├─ Lua runtime            Cosmos.Executable.Lua, shell bindings
 ├─ VFS / storage          Cosmos VFS, partitions, GPT, FAT32
 └─ System services        registry, accounts, file associations, networking
```

## Repository layout

| Path | Contents |
|---|---|
| `Kernel.cs` | Kernel entry point |
| `System/` | Core system code |
| `System/ELF/` | ELF loader, kernel API, `WindoseBridge`, bundled programs and libc |
| `Programs/` | Built-in C# programs |
| `Resources/` | Embedded fonts, icons, cursors and wallpapers |
| `Bootloader/` | Limine bootloader configuration |
| `Installer/` | Setup wizard |

## Building

Requirements:

- .NET 10 SDK
- Cosmos SDK 3.0.94 (restored through NuGet, see `.appdata/NuGet/NuGet.Config`)
- [Windose Installer](https://github.com/justaSalam/Windose-Installer) (for boot-from-disk)

```sh
cosmos build
```

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
