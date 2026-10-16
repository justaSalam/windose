# Windose ELF loader (v10)

Runs x86-64 ELF programs written in C inside your Cosmos kernel: several at once, each with its own
handles, events, memory quota and process-list entry, with a small libc and normal `main(argc, argv)`.

```
kernel/            add all of these to your kernel project (AllowUnsafeBlocks on)
programs/          C library, headers, example programs, build scripts; prebuilt ELFs in bin/
tests/             desktop test suite (Linux/WSL + dotnet 8): format_test.c and ElfTests/
```

```csharp
byte[] data = File.ReadAllBytes(path);
string err; ElfProgram p = ElfRunner.Spawn(data, "hello", new string[] { "one" }, out err);
if (p == null) Terminal.WriteLine("exec failed: " + err);
```

## What it does now

* **PIE + relocations**: normal compiler output works, including tables of pointers (R_X86_64_RELATIVE/64/GLOB_DAT/JUMP_SLOT
  against symbols in the program itself, plus RELR packed relocations). Images are page-aligned. Unresolved symbols and TLS
  are rejected with a message. Extended section numbering (PN_XNUM) and header-count sanity caps included.
* **libc** (`programs/libc`, headers in `programs/include`): string.h, stdlib.h (malloc/free/calloc/realloc, strtol, qsort,
  bsearch, rand), stdio.h (printf family without floats, puts, `file_read`), ctype.h, `exit()` from any depth, `abort()`.
  `crt0` builds argc/argv and turns the return value / `exit()` into the program's exit code.
* **Processes**: `ElfRunner.Spawn` gives every program its own thread, pid, handle table, event queue and memory quota
  (`ElfRunner.MemoryQuota`, default 64 MB). Windows and memory are released when it exits. `ElfRunner.Kill(pid)` closes its windows
  at once and the program stops at its next kernel call (sleep/poll are checked every 10 ms). `ElfRunner.List()` for `ps`.
  Programs can `spawn`/`kill` each other and `yield`.
* **API** (`kernel.h` -> `k_*`): write, create_window, close, sleep, create_button, create_label, set_text, get_text,
  fill_rect, create_canvas, draw_pixels, resize_window, list_dir, stat_file, make_dir, delete_path, rename_path,
  create_checkbox, set/get_checked, create_radio, create_slider, slider_set/get, create_progress, progress_setup/set,
  create_textfield, textfield_setup, create_combo (+ add/remove/clear/get/set), create_listbox (+ add/remove/clear/get/select),
  create_tabs (+ add_page/select/get), menubar_create, menu_add_item, msgbox, statusbar_create (+ add_panel/set_text),
  tree (+ add_root/add_child/clear/selected/expanded), toolbar, popup, list columns (+ add_row/hit_test/screen_pos),
  grid (+ columns/rows/cells/sort), drop_accept, dnd_get_text, widget_set/get_enabled, widget_focus,
  spawn, kill, yield, poll_event, read_file, write_file, ticks, pid,
  launch_app, open_file, notify_send, desktop_count, desktop_current, desktop_switch, window_set_desktop,
  trash_delete, trash_list, trash_restore, trash_empty.
  Memory comes from malloc (kernel `alloc` underneath). Program output goes to the system log (`elf:<name>`).
* **Events**: click, close, key (code + cooked char), mouse move/down/up, focus in/out, drop with text payload.
  Widget changes (checkbox, combo, list, tabs, menus, dialogs, color, numeric, calendar, grid) arrive as
  click events with payloads (`programs/src/widgets.c` is a gallery of all of them).
* **Filesystem**: list_dir, stat_file, make_dir, delete_path, rename_path
  (`programs/src/explorer.c` mirrors the C# FileExplorer: menu bar, toolbar, address bar,
  tree + details list, status bar, context menus, back/forward history, text viewer,
  copy/cut/paste, delete, rename, new file/folder, go-to path, sorting).

## Writing programs

`programs/src/*.c`: write a normal `int main(int argc, char **argv)`; `#include <stdio.h>` etc. and `"kernel.h"` for k_* calls.
Build: Linux/WSL `sh build.sh [name]` (gcc + binutils), Windows `build.bat [name]` (LLVM, untested). Output: `bin/<name>.elf`.

Limits: no floating point in printf, no global constructors (`.init_array`), no TLS, no `fopen` (use `file_read` / `k_write_file`),
no threads inside a program. Kill cannot stop a program stuck in a pure compute loop that never calls the kernel.
API v10 needs rebuilt programs AND a rebuilt libc (older `bin/*.elf` exit 127 until rebuilt).

## Ideas worth adding next

Ordered roughly by value over effort. Anything marked ABI needs the append-at-end dance from "Adding a kernel function".

* **Shared libraries (DT_NEEDED)** — the last big loader gap. Load named ET_DYN files from `/mnt/System/lib` via the host, bias-relocate them, resolve cross-image symbols against already-loaded images plus a host export table (the `KernelApi` functions). Medium effort, unlocks real toolchains.
* **Crash isolation** — native faults in a program thread kill ring 0. Short of page tables: validate jump/call targets stay inside the image at load (reject wild relocations already helps), zero freed host memory, and catch/report managed exceptions at every host boundary (done). Real fix is ring 3 + per-process paging.
* **Preemptive kill** — today a compute loop that never calls the kernel can't be stopped. Needs thread abort/suspend support from the scheduler; until then document `k_yield()` in hot loops.
* **Stdin / console read** — programs can print but not read. Add a `read_console` call fed by focused-terminal input, or a `read_line` on the owning terminal.
* **File metadata** — ~~`stat`/`readdir`/`mkdir`/`delete` so programs can browse the FS~~ done (v4: list_dir, stat_file, make_dir, delete_path, rename_path; v8: list_drives, drive_stat).
* **`waitpid`** — `spawn` exists but the parent can't wait; add a blocking wait on child exit code.
* **Clipboard + Env vars** — `getenv`/`setenv` (tiny) and clipboard get/set (WM clipboard exists; just expose it).
* **VSync / wait-vblank** — throttle animation loops to the display instead of `k_sleep` guessing.
* **Sockets** — Cosmos has a network stack; a `tcp_connect`/`send`/`recv` trio would make networked ELF programs possible.
* **Message boxes + file dialogs** — ~~`msgbox(text)` and `open_file()`~~ done (v5/v6: msgbox, filedialog_open, open_file, launch_app).
* **Drag & drop / taskbar progress / notifications** — ~~host-side features programs could drive~~ done (v6/v9: drop_accept, dnd_get_text, notify_send; taskbar progress still open).
* **`.init_array` constructors + TLS** — parse and run init arrays at load (easy); TLS needs a thread-pointer the kernel can't provide yet (hard).
* **mmap/sbrk + `free` to kernel** — libc sub-allocates from kernel chunks and never returns them; add a `release` call so long-lived programs can give whole chunks back.
* **Fuzz the loader** — feed mutated ELFs to `ElfFile.Parse` + `ApplyRelocations` on desktop; every error path returns a string instead of throwing, so this is cheap and high-value.

## Adding a kernel function

1. `programs/include/kernel.h`: new field at the END of `KernelApi` (first parameter is always `int pid`) + a `k_*` wrapper.
2. `kernel/ElfRunner.cs`: same field at the END of `KernelApi`, an `[UnmanagedCallersOnly]` handler (start with
   `Live(pid)`, try/catch, validate everything), and assign it in `FillApi`. Bump `KAPI_VERSION`/`ApiVersion` if you change existing ones.
3. Need OS access? Add a method to `IElfHost`, implement it in `WindoseBridge.cs`.

## If it breaks

| Symptom | Fix |
|---|---|
| Page fault on the first instruction | Heap isn't executable: set `ElfRunner.MakeExecutable` to mark the pages. |
| Crash on the first kernel call | ABI mismatch: in `kernel.h` change `sysv_abi` to `ms_abi`, rebuild libc + programs. |
| `kernel API version mismatch` | Program built against an older `kernel.h`: rebuild. |
| `exec failed: ... unresolved symbol 'x'` | The program calls a function that isn't in libc: add it to `programs/libc`. |
| `exec failed: ET_EXEC linked at a fixed address` | Use the provided build scripts (they link PIE). |

## Testing on a desktop first

```
cd tests && gcc -O1 -o /tmp/format_test format_test.c ../programs/libc/format.c && /tmp/format_test    # printf engine vs glibc
cd ElfTests && dotnet run ../../programs/bin                                                           # loader, processes, kill, quota
```

## Isolation

Programs run in ring 0 on kernel threads: a buggy program can still crash or corrupt the kernel, and `Kill` is cooperative.
Programs only use the `k_*` wrappers, so a future ring-3 transport (syscall instruction, per-process page tables) only
changes `kernel.h` and the kernel entry path, not program sources.
