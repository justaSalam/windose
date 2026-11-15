# Windose ELF Loader & Dynamic Subsystem (v16)

Windose features a full-fledged native x86-64 ELF execution subsystem running within the Cosmos NativeAOT x86-64 operating system kernel. It provides simultaneous execution of multiple ELF binaries, complete POSIX/Linux C library compatibility, dynamic shared library loading (`.so`), Linux system call emulation, an extensive Windows-style GUI component bridge, process lifecycle management with cooperative scheduling, and a comprehensive suite of utilities.

```
System/ELF/
├── ElfReader.cs             # High-performance ELF32/ELF64 parser, segment loader & fast relocation engine
├── ElfDynLinker.cs          # Runtime dynamic linker for ET_DYN shared libraries (DT_NEEDED) & symbol resolution
├── LinuxSyscall.cs          # Linux x86-64 syscall emulation, POSIX file descriptor table & System V stack setup
├── ElfRunner.cs             # Process manager, execution scheduler, API dispatch table (v16), handle cleanup
├── ElfProgram.cs            # Program instance model, memory quota, environment table, thread lifecycle & deferred windows
├── ElfGui.cs                # GUI integration, native widget bridge, input mapping & keyboard focus handling
├── WindoseBridge.cs         # IElfHost bridge implementing kernel UI, filesystem, dialogs & system integration
├── ElfSelfTests.cs          # In-kernel boot-time test harness validating loader, dynamic linker, syscalls & utilities
├── ElfEvents.cs             # Ring buffer event queue for GUI and keyboard/mouse input
├── HandleTable.cs           # O(1) integer-handle table for UI components and system resources
├── docs/                    # Interactive web documentation & API specifications
└── programs/                # C runtime (libc), POSIX layer, headers, build scripts & ELF binaries
    ├── include/             # C library & POSIX headers (kernel.h, unistd.h, fcntl.h, dirent.h, dlfcn.h, sys/*)
    ├── libc/                # Freestanding C runtime (crt0, stdio, stdlib, string, math, posix, kapi_global)
    ├── src/                 # Source code for applications and utilities (explorer, taskmgr, tcc, grep, etc.)
    └── bin/                 # Compiled binaries & shared libraries (libc.so, libwindose.so, *.elf)
```

---

## 1. Architecture & Execution Model

### Cooperative Process Execution in Ring 0
ELF executables run directly as native x86-64 machine code in Ring 0 on dedicated kernel threads managed by the Cosmos scheduler.
- **Thread per Process**: Calling `ElfRunner.Spawn` assigns each program its own unique Process ID (`pid`), an isolated memory quota (default: 64 MB), an event queue (`ElfEventQueue`), a handle table (`HandleTable`), an environment dictionary (`p.Env`), and a standard I/O redirection pipeline.
- **Lifecycle & Cooperative Scheduling**: Programs yield execution to the kernel via `k_yield()` or while waiting for events with `k_poll_event()` and `k_sleep()`. The kernel monitors running programs every 10 ms.
- **Process Termination**: `ElfRunner.Kill(pid)` automatically tears down the process, unpins memory allocations, closes all owned GUI windows, flushes open file handles, and terminates the host thread. When a process terminates cleanly, `crt0.c` executes all registered `atexit()` / `at_quick_exit()` handlers, flushes stdio streams, and propagates the exit code.
- **Inter-Process Management**: Programs can launch other binaries via `k_spawn()` / `k_launch_app()`, wait on child processes via `k_wait_pid()`, and terminate processes with `k_kill()`.

### Memory Management & Quotas
- Every process is tracked against `ElfRunner.MemoryQuota` (default 64 MB).
- Native heap allocations requested through `malloc()` / `calloc()` / `realloc()` resolve to `k_alloc()`, which allocates GC-pinned byte arrays in the kernel heap and tracks them in `ElfProgram._pins`.
- On exit, all GC handles and allocated memory blocks are automatically freed in `ElfProgram.Dispose()`, preventing memory leaks even if a program crashes.

---

## 2. Binary Format & Dynamic Linker (`ElfDynLinker`)

### Supported ELF Formats
The loader parses 64-bit Little-Endian ELF binaries (`ELFCLASS64`, `ELFDATA2LSB`) with full support for:
- **ET_EXEC & ET_DYN (PIE)**: Position-Independent Executables compiled with `-fPIE` / `-pie`.
- **PT_LOAD Segments**: Mapped with 4 KB page alignment (`0x1000`) into a continuous memory buffer.
- **PT_DYNAMIC Segment**: Extracts `DT_NEEDED`, `DT_RELA`, `DT_REL`, `DT_RELR`, `DT_JMPREL`, `DT_SYMTAB`, `DT_STRTAB`, `DT_INIT`, `DT_FINI`, `DT_INIT_ARRAY`, and `DT_FINI_ARRAY`.
- **PT_INTERP**: Dynamically linked executables requesting `/lib/ld-windose.so` are seamlessly dispatched to `ElfDynLinker`.

### Fast Pointer-Based Relocation Engine
`ElfReader.cs` applies relocations directly in memory using native pointers for maximum load-time performance:
- **Relocation Types**: `R_X86_64_RELATIVE`, `R_X86_64_64`, `R_X86_64_GLOB_DAT`, `R_X86_64_JUMP_SLOT`, `R_X86_64_32`, `R_X86_64_32S`, `R_X86_64_PC32`, `R_X86_64_PLT32`, `R_X86_64_COPY`, and `R_X86_64_IRELATIVE`.
- **Packed RELR Relocations**: Supports compact `DT_RELR` relocation tables generated by modern linkers (`ld.lld -z relr`).
- **Symbol Resolution Caching**: Uses a flat `resolvedSymbols[]` lookup array to avoid redundant linear symbol table scans.

### Dynamic Shared Libraries (`.so`)
`ElfDynLinker.cs` provides runtime dynamic linking equivalent to standard Unix `ld.so`:
1. **DT_NEEDED Resolution**: Automatically discovers and loads shared libraries referenced by the binary from standard search paths:
   - `/mnt/Programs`
   - `/mnt/Programs/lib`
   - `/mnt/System/lib`
   - `/mnt/lib`
   - `/System/lib`
   - `/lib`
2. **Cross-Image Symbol Resolution**: Resolves undefined symbols (`SHN_UNDEF`) against exported symbols in loaded libraries (`libc.so`, `libwindose.so`).
3. **Global Kernel API Injection**: Dynamically links `KernelApi *__k` exported by `libwindose.so`, allowing dynamic libraries to call kernel services without recompilation.
4. **Initialization & Finalization**: Executes `.preinit_array` and `.init_array` constructors in dependency order before `main()`, and `.fini_array` destructors in reverse order upon program termination.

---

## 3. POSIX & Linux C Library Subsystem

Windose provides a comprehensive, freestanding C runtime (`programs/libc/`) alongside standard headers (`programs/include/`):

### Standard Headers
- `<stdio.h>`: Formatted I/O (`printf`, `snprintf`, `vsnprintf`, `fprintf`, `puts`, `putchar`, `fputc`, `fputs`), file I/O (`fopen`, `fclose`, `fread`, `fwrite`, `fseek`, `ftell`, `fflush`, `fgetc`, `fgets`, `feof`, `ferror`), standard streams (`stdin`, `stdout`, `stderr`).
- `<stdlib.h>`: Memory management (`malloc`, `free`, `calloc`, `realloc`), numeric conversions (`strtol`, `strtoul`, `strtoll`, `atoi`, `atol`, `atof`), sorting & searching (`qsort`, `bsearch`), pseudorandom numbers (`rand`, `srand`), environment access (`getenv`, `setenv`, `unsetenv`, `putenv`), process exit (`exit`, `quick_exit`, `_Exit`, `abort`, `atexit`, `at_quick_exit`).
- `<string.h>`: Fast assembly/C memory and string primitives (`memcpy`, `memmove`, `memset`, `memcmp`, `memchr`, `strlen`, `strcpy`, `strncpy`, `strcat`, `strncat`, `strcmp`, `strncmp`, `strchr`, `strrchr`, `strstr`, `strtok`, `strdup`, `strerror`).
- `<math.h>`: Full floating-point mathematics: `sin`, `cos`, `tan`, `asin`, `acos`, `atan`, `atan2`, `exp`, `log`, `log10`, `log2`, `pow`, `sqrt`, `cbrt` (accelerated Newton-Raphson), `hypot`, `fabs`, `floor`, `ceil`, `round`, `fmod`, `modf`, `frexp`, `ldexp`.
- `<ctype.h>`: Character classification (`isalpha`, `isdigit`, `isspace`, `toupper`, `tolower`, etc.).
- `<unistd.h>`: POSIX low-level I/O (`read`, `write`, `open`, `close`, `lseek`, `access`, `unlink`), process IDs (`getpid`, `getppid`, `getuid`, `geteuid`), directory navigation (`getcwd`, `chdir`, `rmdir`), execution timing (`sleep`, `usleep`).
- `<fcntl.h>`: File control options (`O_RDONLY`, `O_WRONLY`, `O_RDWR`, `O_CREAT`, `O_TRUNC`, `O_APPEND`).
- `<dirent.h>`: Directory traversal (`opendir`, `readdir`, `closedir`, `struct dirent`).
- `<dlfcn.h>`: Dynamic library symbol lookups (`dlopen`, `dlsym`, `dlclose`, `dlerror`).
- `<sys/stat.h>`: File and folder metadata (`stat`, `fstat`, `lstat`, `mkdir`, `chmod`, `struct stat`).
- `<sys/mman.h>`: Virtual memory management (`mmap`, `munmap`, `mprotect`, `PROT_READ`, `PROT_WRITE`, `MAP_ANONYMOUS`, `MAP_PRIVATE`).
- `<sys/ioctl.h>`: Device and terminal control primitives (`ioctl`, `TIOCGWINSZ`).
- `<sys/utsname.h>`: System identity information (`uname`).

### POSIX Emulation Details (`programs/libc/posix.c`)
- **File Descriptors**: Full POSIX file descriptor table mapping FD 0 (stdin), FD 1 (stdout), FD 2 (stderr), and FDs 3+ to kernel file records.
- **Streaming Stdin Support**: Standard input reads (`read(0, ...)` / `fgetc(stdin)`) are hooked into `ElfProgram.InputHandler`, allowing live keyboard reading and command redirection from the Windose Command Prompt.
- **Stack Smashing Protection**: Exports `__stack_chk_guard` and `__stack_chk_fail` to support GCC and Clang `-fstack-protector`.
- **Thread-Safe Errno**: Provides `__errno_location()` returning a per-process address.

---

## 4. Linux Syscall Emulation (`LinuxSyscall.cs`)

For binaries using direct Linux x86-64 system calls, `LinuxSyscall.cs` provides emulation and System V AMD64 ABI setup:
- **System V Initial Stack**:
  - `argc` count
  - `argv[]` null-terminated array of string pointers
  - `envp[]` null-terminated array of environment variable pointers
  - `auxv[]` auxiliary vector containing `AT_PHDR`, `AT_PHENT`, `AT_PHNUM`, `AT_PAGESZ` (4096), `AT_BASE`, `AT_ENTRY`, `AT_RANDOM`, `AT_UID`, `AT_GID`, and `AT_NULL`.
- **Syscall Dispatch Table**:
  - `SYS_READ` (0), `SYS_WRITE` (1), `SYS_OPEN` (2), `SYS_CLOSE` (3), `SYS_STAT` (4), `SYS_FSTAT` (5), `SYS_LSTAT` (6), `SYS_POLL` (7), `SYS_LSEEK` (8), `SYS_MMAP` (9), `SYS_MPROTECT` (10), `SYS_MUNMAP` (11), `SYS_BRK` (12), `SYS_IOCTL` (16), `SYS_ACCESS` (21), `SYS_GETPID` (39), `SYS_UNAME` (63), `SYS_GETCWD` (79), `SYS_CHDIR` (80), `SYS_MKDIR` (83), `SYS_RMDIR` (84), `SYS_EXIT` (60), `SYS_EXIT_GROUP` (231).

---

## 5. Kernel API (`kernel.h` & `WindoseBridge.cs`)

ELF programs access the native Windose operating system services via `kernel.h` through the `k_*` API (ABI Version 16):

### Windowing & GUI Creation
| Function | Description |
|---|---|
| `k_create_window(title, x, y, w, h)` | Creates a top-level Aero window with client layout management. |
| `k_create_button(win, x, y, w, h, text)` | Creates a native Aero push button. |
| `k_create_label(win, x, y, text)` | Creates a text label with automatic font sizing. |
| `k_create_textfield(win, x, y, w, h)` | Creates a single-line or multi-line editable text box. |
| `k_create_checkbox(win, x, y, text)` | Creates a check box control. |
| `k_create_radio(win, x, y, text, group)` | Creates a radio button grouped by integer identifier. |
| `k_create_slider(win, x, y, w, h, min, max)` | Creates a trackbar/slider control. |
| `k_create_progress(win, x, y, w, h)` | Creates a progress bar control. |
| `k_create_combo(win, x, y, w, h)` | Creates a drop-down combo box. |
| `k_create_listbox(win, x, y, w, h)` | Creates a scrollable single-column or multi-column list box. |
| `k_create_tabs(win, x, y, w, h)` | Creates a tabbed container control. |
| `k_create_canvas(win, x, y, w, h)` | Creates a direct drawing surface for software rendering. |
| `k_draw_pixels(canvas, x, y, w, h, buffer)` | Blits a 32-bit ARGB pixel buffer directly to a canvas. |
| `k_fill_rect(win, x, y, w, h, argb)` | Draws a filled rectangle with 32-bit ARGB color. |
| `k_menubar_create(win)` | Creates a Windows-style menu bar with top-level dropdowns. |
| `k_statusbar_create(win)` | Creates a bottom status bar with partitioned text panels. |
| `k_tree_create(win, x, y, w, h)` | Creates a hierarchical tree view with collapsible nodes. |
| `k_grid_create(win, x, y, w, h)` | Creates a sortable data grid view with column headers. |

### Flicker-Free Deferred Window Registration
To prevent unsightly visual pop-in while controls are being constructed:
- Windows created by `k_create_window` start in a deferred state (`IsDeferredRegistration = true`).
- The window is only registered with `WindowManager` when the program enters its event loop via `k_poll_event()` or calls `k_sleep()`, ensuring all child widgets are fully instantiated and positioned before the first frame is rendered.

### Filesystem & Volume Services
| Function | Description |
|---|---|
| `k_list_dir(path, buf, max)` | Enumerates folder contents separated by newlines. |
| `k_stat_file(path, out_stat)` | Retrieves file size, directory flag, and modification timestamp. |
| `k_read_file(path, buf, max)` | Reads binary file contents (or reads from `"stdin"`). |
| `k_write_file(path, buf, size)` | Writes or creates a binary file. |
| `k_make_dir(path)` | Creates a new directory. |
| `k_delete_path(path)` | Deletes a file or recursively deletes a directory. |
| `k_rename_path(old_path, new_path)`| Renames or moves a file or folder. |
| `k_list_drives(buf, max)` | Lists all mounted system drives and volumes (e.g. `0:/`, `/mnt`). |
| `k_drive_stat(path, out_stat)` | Returns volume label, total capacity, and available free space. |

### Shell Integration, Drag & Drop, & Clipboard
| Function | Description |
|---|---|
| `k_launch_app(name, arg)` | Launches a system application by friendly name (`explorer`, `calc`, `notepad`, etc.). |
| `k_open_file(path)` | Opens a file using its associated system handler. |
| `k_clipboard_get(buf, max)` | Retrieves UTF-8 text from the system clipboard. |
| `k_clipboard_set(text)` | Copies UTF-8 text to the system clipboard. |
| `k_notify_send(title, text, type)` | Displays a desktop balloon/toast notification. |
| `k_drop_accept(handle, accept)` | Registers a window or canvas as an OLE drag-and-drop target. |
| `k_dnd_get_text(buf, max)` | Retrieves dragged file paths or text during a drop event. |
| `k_trash_delete(path)` | Moves a file to the system Recycle Bin. |
| `k_trash_restore(name)` | Restores an item from the Recycle Bin. |
| `k_trash_empty()` | Permanently purges the Recycle Bin. |

---

## 6. Applications & CLI Utilities

The ELF subsystem includes both graphical applications and a rich set of command-line tools:

### GUI Applications
- **`explorer.elf`**: Complete Windows 7-style File Explorer with address bar, search filtering, back/forward history, folder tree view, file list with view modes (Large Icons, Small Icons, Details), context menus, properties dialogs, and a dedicated **Computer / Drives** view displaying drive volume stats and free space bars.
- **`taskmgr.elf`**: Task Manager displaying CPU and memory metrics, process list with PIDs, and interactive termination via the **Delete** hotkey and **F5** refresh.
- **`regedit.elf`**: Windows Registry Editor browsing system keys and values.
- **`aeroeditor.elf`**: Syntax-highlighting text and code editor with file operations and autocomplete.
- **`calc.elf`**: Standard Windows-style calculator.
- **`paint.elf`**: Graphical painting program with brush sizing and color palette.
- **`winver.elf`**: Windows 7 About / Version information dialog.
- **`tcc.elf`**: Tiny C Compiler ported to run natively inside Windose.

### CLI Utilities Suite
- **`donut.elf`**: 3D ASCII spinning donut animation calculating real-time mathematical torus projections and illumination.
- **`micromax.elf`**: Micro-Max 4.8 chess engine running full chess search and evaluation inside the terminal.
- **`grep.elf`**: Regular expression and substring search across text files and streams.
- **`wc.elf`**: Word, line, and character counter for files and pipelines.
- **`head.elf` & `tail.elf`**: Display the beginning or end of text streams.
- **`pwd.elf`**: Prints the current working directory.
- **`uname.elf`**: Prints system architecture and OS kernel information.
- **`yes.elf`**: Continuous string generator for piping into interactive scripts.
- **`strings.elf`**: Extracts printable character sequences from binary files.
- **`dynhello.elf`**: Dynamically linked demonstration program linked against `libc.so`.

---

## 7. Building Programs & Shared Libraries

### Toolchain Requirements
- LLVM / Clang and LLD (`clang`, `ld.lld`).
- Windows: Run `System/ELF/programs/build.bat [program_name]`.
- Linux/WSL: Run `System/ELF/programs/build.sh [program_name]`.

### Compilation Workflow (`build.bat`)
```bat
rem 1. Compile libc static objects
clang --target=x86_64-unknown-none-elf -c -O2 -ffreestanding -fPIE ... libc/*.c

rem 2. Build shared libraries libc.so and libwindose.so
clang --target=x86_64-unknown-none-elf -c -O2 -ffreestanding -fPIC -fvisibility=default ...
ld.lld -shared -soname libc.so -z norelro -z noseparate-code ... -o bin\libc.so obj\so_*.o
copy /y bin\libc.so bin\libwindose.so

rem 3. Build a static PIE program
clang --target=x86_64-unknown-none-elf -c -O2 -fPIE ... src/myapp.c -o obj/prog_myapp.o
ld.lld -pie --no-dynamic-linker -e _start ... -o bin/myapp.elf obj/crt0.o obj/prog_myapp.o obj/*.o

rem 4. Build a dynamically linked program
clang --target=x86_64-unknown-none-elf -c -O2 -fPIC ... src/dynhello.c -o obj/prog_dynhello.o
ld.lld -pie -dynamic-linker /lib/ld-windose.so -e _start ... -o bin/dynhello.elf obj/crt0.o obj/prog_dynhello.o bin/libc.so
```

---

## 8. In-Kernel Self-Tests (`ElfSelfTests.cs`)

During boot, `ElfSelfTests.Run()` is executed in `Kernel.cs` to verify all components of the ELF subsystem:
1. `TestParseStaticElf`: Validates header parsing, program headers, and section headers of static PIE binaries.
2. `TestParseDynamicElf`: Checks `PT_DYNAMIC`, `PT_INTERP`, and relocation entries in dynamically linked binaries.
3. `TestParseSharedLibrary`: Validates `ET_DYN` shared libraries (`libc.so`), export tables, and string tables.
4. `TestCliUtilitySuite`: Verifies file presence and header integrity for all core CLI tools (`grep`, `wc`, `head`, `tail`, `pwd`, `uname`, `yes`, `strings`, `donut`, `micromax`).
5. `TestDynamicLibraryAliases`: Tests that alias mappings (e.g. `libwindose.so` and `libc.so`) resolve correctly.
6. `TestDynamicLinkerSimulation`: Simulates cross-image symbol resolution and external GOT fixups.
7. `TestLinuxSyscallDispatch`: Validates the kernel syscall table and dispatch handler.
8. `TestInitialStackSetup`: Verifies System V AMD64 ABI stack creation, argument vectors, environment pointers, and auxiliary vector alignments.

---

## 9. Troubleshooting & FAQ

| Symptom | Cause | Solution |
|---|---|---|
| `Page fault on first instruction` | Memory page is not executable. | Verify `ElfRunner.MakeExecutable` is enabled so memory pages are marked RX. |
| `kernel API version mismatch` | Binary was built against an older `kernel.h`. | Recompile the program with the latest `kernel.h` (API v16). |
| `unresolved symbol 'xyz'` | The program calls a C library function not yet in `libc`. | Add the missing function to `programs/libc/` and recompile. |
| `ET_EXEC linked at a fixed address` | Binary was compiled with absolute addresses (`-no-pie`). | Use `-fPIE` and `-pie` in compiler and linker flags. |
| `Cannot find shared library 'x.so'` | The library is not in standard library search paths. | Place the `.so` file in `/mnt/Programs/lib` or `/mnt/System/lib`. |
