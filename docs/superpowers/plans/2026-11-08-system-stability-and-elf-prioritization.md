# System Stability and ELF Prioritization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix core system stability bugs, optimize dirty-region compositing, expand the ELF Kernel API with process, registry, and declarative anchoring APIs, implement missing ELF system utilities (`taskmgr.elf`, `regedit.elf`), improve existing ELF apps (`aeroeditor.elf`, `explorer.elf`, `calc.elf`), and prioritize ELF binaries across the desktop shell.

**Architecture:** A three-phase evolution: (1) Kernel & Compositor bugfixes and cleanup, (2) ELF `KernelApi` expansion with process management, registry, and declarative anchoring to replace heuristic layout in `ElfGui.cs`, and (3) Native ELF application suite implementation and priority shell wiring across Start Menu, Desktop, Taskbar, and File Associations.

**Tech Stack:** C# 12 / .NET 9 NativeAOT, Cosmos OS Kernel, x86-64 freestanding C99 (Clang 22.1 / LLD).

**Spec:** `docs/superpowers/specs/2026-11-08-system-stability-and-elf-prioritization-design.md`

## Global Constraints

- Kernel and managed code must compile cleanly with `cosmos build` (Roslyn -> NativeAOT `ilc` -> `ld.lld`).
- ELF C programs must compile cleanly with `clang --target=x86_64-unknown-none-elf` and `ld.lld -pie` via `System/ELF/programs/build.bat`.
- Binary struct compatibility between C# `KernelApi` (`ElfRunner.cs`) and C `KernelApi` (`kernel.h`) must be maintained in exact field order and size.
- Window resizing must preserve the bottom-right corner grip scaling behavior restored in commit `7dd1ae9`.

## Review Focus

1. `Session.isElevated` recursion: Setting `Session.isElevated` must never cause a `StackOverflowException`.
2. Process termination safety: Calling `ps_kill` from an ELF application must never terminate critical system processes (`canTerminate == false`, like `WindowManager` or `Explorer`).
3. `ScheduledProcess` CPU usage: Background worker threads must sleep by their update interval and not pin the CPU core at 100%.
4. Compositor dirty-rect clustering: Isolated screen updates (e.g. clock and cursor) must not cause full-screen 1080p clears.
5. ELF Editor file I/O: `aeroeditor.elf` must load a file passed via `argv[1]` and save changes back to disk via `k_write_file`.

---

### Task 1: Core System & Threading Bug Fixes

**Files:**
- Modify: `System/Kernel/Subsystem/Session.cs:10-25`
- Modify: `System/Kernel/Subsystem/UserAccount.cs:80-92`
- Modify: `System/Process/ScheduledProcess.cs:64-70`
- Modify: `Kernel.cs:143-162`

**Interfaces:**
- `Session.isElevated`: getter/setter using private `_isElevated` field.
- `ScheduledProcess.WorkerLoop()`: uses `GetNextUpdateIntervalMs()` for thread sleeping.

- [ ] **Step 1: Fix `Session.isElevated` recursive property setter in `System/Kernel/Subsystem/Session.cs`**
  Add `private static bool _isElevated;`. Update getter to `_isElevated || (CurrentUser != null && CurrentUser.privilege is PrivilegeLevel.Administrator or PrivilegeLevel.System);`. Update setter to `_isElevated = value;`.
- [ ] **Step 2: Fix `UserAccount.CreateAccount` formatting and existing directory handling in `System/Kernel/Subsystem/UserAccount.cs`**
  Ensure existing profile directories don't abort account creation. Append `Environment.NewLine` to the written accounts record.
- [ ] **Step 3: Fix `ScheduledProcess.WorkerLoop` sleep interval in `System/Process/ScheduledProcess.cs`**
  Replace `Thread.Sleep(0)` with `int interval = GetNextUpdateIntervalMs(); Thread.Sleep(interval > 0 ? interval : 10);`.
- [ ] **Step 4: Deduplicate global hotkeys in `Kernel.cs`**
  Remove redundant `HotkeyManager.RegisterHotkey` calls on lines 143-162 that are already registered inside `HotkeyManager`'s constructor.
- [ ] **Step 5: Verify build with `cosmos build`**
  Run `cosmos build` and verify exit code 0.
- [ ] **Step 6: Commit changes**
  `git add System/Kernel/Subsystem/Session.cs System/Kernel/Subsystem/UserAccount.cs System/Process/ScheduledProcess.cs Kernel.cs; git commit -m "Fix Session elevation recursion, UserAccount serialization, ScheduledProcess CPU spinning, and duplicate hotkeys"`

---

### Task 2: Compositor Dirty-Region Redraw Optimization & Dead Code Cleanup

**Files:**
- Modify: `System/GUI/Window API/WindowManager.cs:256-285`
- Delete: `System/Kernel/MlfqScheduler.cs`
- Modify: `System/Shell/Desktop.cs:4-8`
- Modify: `System/Shell/StartMenu.cs:4,13`

**Interfaces:**
- `WindowManager.ComposeDirtyRegions()`: Clusters overlapping/adjacent dirty rectangles, rendering each cluster clipped to its bounds without merging distant rects into full-screen bounding boxes.

- [ ] **Step 1: Optimize `ComposeDirtyRegions` in `System/GUI/Window API/WindowManager.cs`**
  Replace single-bounding-box union with a clustering algorithm: group overlapping or touching rectangles together into clusters; render each cluster independently.
- [ ] **Step 2: Delete dead file `System/Kernel/MlfqScheduler.cs`**
  Remove the unused 833-line `MlfqScheduler.cs` from disk.
- [ ] **Step 3: Remove unused `using` statements in `Desktop.cs` and `StartMenu.cs`**
  Clean up unused Roslyn syntax and serialization imports in `Desktop.cs` and unused Lua imports in `StartMenu.cs`.
- [ ] **Step 4: Verify build with `cosmos build`**
  Run `cosmos build` and verify exit code 0.
- [ ] **Step 5: Commit changes**
  `git add System/GUI/Window\ API/WindowManager.cs System/Shell/Desktop.cs System/Shell/StartMenu.cs; git rm System/Kernel/MlfqScheduler.cs; git commit -m "Optimize compositor dirty rect clustering and remove MlfqScheduler dead code"`

---

### Task 3: ELF Subsystem KernelApi & kapi.h Extensions

**Files:**
- Modify: `System/ELF/programs/include/kernel.h`
- Modify: `System/ELF/ElfRunner.cs`
- Modify: `System/ELF/IElfHost.cs`
- Modify: `System/ELF/WindoseBridge.cs`

**Interfaces:**
- `ps_list(pid, char *buf, int max_bytes) -> int count`: Formats processes as `pid:name:type:mem\n`.
- `ps_kill(pid, int target_pid) -> int status`: Terminates target process if `canTerminate != false`.
- `reg_list(pid, const char *prefix, char *buf, int max_bytes) -> int count`: Formats matching keys as `key\n`.
- `reg_get(pid, const char *key, char *val_buf, int max_bytes) -> int len`: Reads registry string value.
- `reg_set(pid, const char *key, const char *val) -> int status`: Writes registry value with type coercion.
- `widget_set_anchors(pid, int handle, int anchor_flags) -> int status`: Sets edge anchor flags (`ANCHOR_LEFT=1`, `TOP=2`, `RIGHT=4`, `BOTTOM=8`).
- `widget_set_bounds(pid, int handle, int x, int y, int w, int h) -> int status`: Repositions/resizes child component.

- [ ] **Step 1: Add new syscalls and constants to `System/ELF/programs/include/kernel.h`**
  Add function pointers and anchor constants to `KernelApi` struct in exact matching order, and bump `KERNEL_API_VERSION` to 14. Add C helper wrappers (`k_ps_list`, `k_ps_kill`, `k_reg_list`, `k_reg_get`, `k_reg_set`, `k_widget_set_anchors`, `k_widget_set_bounds`).
- [ ] **Step 2: Update `KernelApi` struct and `ApiVersion` in `System/ELF/ElfRunner.cs`**
  Mirror the exact fields and delegates in `ElfRunner.KernelApi` and bump `ApiVersion = 14`.
- [ ] **Step 3: Update `IElfHost.cs` interface with the new host operations**
  Add `PsList`, `PsKill`, `RegList`, `RegGet`, `RegSet`, `WidgetSetAnchors`, `WidgetSetBounds`.
- [ ] **Step 4: Implement new host operations in `System/ELF/WindoseBridge.cs`**
  Implement `PsList` enumerating `ProcessManager.processes`, `PsKill` with `canTerminate` safety check, `RegList`/`RegGet`/`RegSet` interfacing with `Registry`, and `WidgetSetAnchors`/`WidgetSetBounds` forwarding to component.
- [ ] **Step 5: Test ELF compilation and kernel build**
  Run `cmd.exe /c "cd System\ELF\programs && build.bat hello"` and `cosmos build`.
- [ ] **Step 6: Commit changes**
  `git add System/ELF/programs/include/kernel.h System/ELF/ElfRunner.cs System/ELF/IElfHost.cs System/ELF/WindoseBridge.cs; git commit -m "Expand KernelApi with process, registry, anchor, and widget bounds syscalls"`

---

### Task 4: Declarative Control Anchoring in `ElfGui.cs`

**Files:**
- Modify: `System/ELF/ElfGui.cs`

**Interfaces:**
- `ElfWindow.SetChildAnchors(Component child, int anchorFlags)`: Stores anchor flags for child.
- `ElfWindow.SetChildBounds(Component child, int x, int y, int w, int h)`: Explicitly sets child position/size and updates initial layout info.
- `ElfWindow.OnWindowResized()`: Uses declarative anchor math to adjust child positions and sizes, and dispatches `ElfEvent.Resize` to the ELF owner.

- [ ] **Step 1: Add anchor flag storage and management in `ElfWindow` (`System/ELF/ElfGui.cs`)**
  Define `AnchorFlags` on `ChildLayoutInfo` (default `ANCHOR_LEFT | ANCHOR_TOP`).
- [ ] **Step 2: Replace 700 lines of heuristic guesswork in `ElfGui.RecalculateLayout` with declarative anchor math**
  Remove calculator/card/workspace heuristic guessing. For each child, adjust `(X, Y, Width, Height)` according to whether `ANCHOR_RIGHT` and `ANCHOR_BOTTOM` are set.
- [ ] **Step 3: Implement `SetChildBounds` in `ElfWindow`**
  Allow runtime bounds updates to child components and sync with `childLayouts`.
- [ ] **Step 4: Verify build with `cosmos build`**
  Run `cosmos build` and verify exit code 0.
- [ ] **Step 5: Commit changes**
  `git add System/ELF/ElfGui.cs; git commit -m "Replace heuristic layout with declarative anchor math in ElfGui"`

---

### Task 5: Native ELF System Utilities (`taskmgr.c` & `regedit.c`)

**Files:**
- Create: `System/ELF/programs/src/taskmgr.c`
- Create: `System/ELF/programs/src/regedit.c`
- Modify: `Windose.csproj`
- Modify: `System/Kernel/ResourceLoader.cs`

**Interfaces:**
- `taskmgr.elf`: Lists running processes in a list/grid with PID, name, memory; provides "End Process" and "Refresh".
- `regedit.elf`: Lists registry keys from `k_reg_list`, shows selected key value via `k_reg_get`, updates via `k_reg_set`.

- [ ] **Step 1: Create `System/ELF/programs/src/taskmgr.c`**
  Implement windowed Task Manager using `k_create_window`, `k_create_listbox`, `k_ps_list`, `k_ps_kill`, and anchor controls to window edges.
- [ ] **Step 2: Create `System/ELF/programs/src/regedit.c`**
  Implement windowed Registry Editor using `k_create_window`, `k_create_listbox`, `k_create_textfield`, `k_reg_list`, `k_reg_get`, and `k_reg_set`.
- [ ] **Step 3: Compile all ELF binaries with `build.bat`**
  Run `cmd.exe /c "cd System\ELF\programs && build.bat"` and verify `bin/taskmgr.elf` and `bin/regedit.elf` are generated.
- [ ] **Step 4: Register new binaries in `Windose.csproj` and `ResourceLoader.cs`**
  Add `EmbeddedResource` entries in `Windose.csproj` for `taskmgr.elf` and `regedit.elf`. In `ResourceLoader.SysRequired()`, unpack them to `/mnt/Programs/taskmgr.elf` and `/mnt/Programs/regedit.elf`.
- [ ] **Step 5: Verify build with `cosmos build`**
  Run `cosmos build` and verify exit code 0.
- [ ] **Step 6: Commit changes**
  `git add System/ELF/programs/src/taskmgr.c System/ELF/programs/src/regedit.c Windose.csproj System/Kernel/ResourceLoader.cs; git commit -m "Implement native ELF Task Manager and Registry Editor"`

---

### Task 6: Enhancing Existing ELF Utilities (`explorer.c`, `aeroeditor.c`, `calc.c`)

**Files:**
- Modify: `System/ELF/programs/src/aeroeditor.c`
- Modify: `System/ELF/programs/src/explorer.c`
- Modify: `System/ELF/programs/src/calc.c`

**Interfaces:**
- `aeroeditor.elf`: Functional text editor supporting CLI `argv[1]` initial path, New, Open, and Save via `k_read_file` / `k_write_file`.
- `explorer.elf`: Launches `.elf` binaries on double-click via `spawn_proc` and other files via `open_file`.
- `calc.elf`: Sets fixed window size (`canResize = false` or explicit fixed bounds).

- [ ] **Step 1: Enhance `aeroeditor.c`**
  Parse `argv[1]` to load file on startup using `k_read_file`. Implement functional "Open" and "Save" handlers reading/writing via `k_read_file` and `k_write_file`. Set control anchors on toolbar, text area (`ANCHOR_LEFT | TOP | RIGHT | BOTTOM`), and status bar.
- [ ] **Step 2: Enhance `explorer.c`**
  When double-clicking an item: if it ends in `.elf`, execute via `k_spawn_proc(itemPath)`. Otherwise call `k_open_file(itemPath)`.
- [ ] **Step 3: Fix `calc.c` window behavior**
  Ensure Calculator remains at its standard fixed dimensions without layout distortion.
- [ ] **Step 4: Recompile all ELF binaries via `build.bat`**
  Run `cmd.exe /c "cd System\ELF\programs && build.bat"` to update `bin/*.elf`.
- [ ] **Step 5: Verify build with `cosmos build`**
  Run `cosmos build` and verify exit code 0.
- [ ] **Step 6: Commit changes**
  `git add System/ELF/programs/src/aeroeditor.c System/ELF/programs/src/explorer.c System/ELF/programs/src/calc.c; git commit -m "Enhance aeroeditor with file I/O, explorer with ELF execution, and calc layout"`

---

### Task 7: Prioritizing ELF Programs Across the Shell & Launchers

**Files:**
- Modify: `System/ELF/WindoseBridge.cs:1205-1262`
- Modify: `System/Shell/StartMenu.cs:57-93`
- Modify: `System/Shell/Desktop.cs:250-265, 368-375`
- Modify: `System/Shell/Taskbar.cs:72-76`
- Modify: `System/Kernel/Subsystem/FileAssociations.cs`

**Interfaces:**
- `WindoseBridge.LaunchApp`: Prioritizes `/mnt/Programs/<name>.elf` before C# fallbacks.
- `StartMenu`: Launches `.elf` versions of Terminal, Task Manager, Editor, Calculator, Explorer, Settings, and Registry Editor.
- `Taskbar`: Context menu launches `taskmgr.elf`.
- `FileAssociations`: Routes text/code extensions (`.txt`, `.c`, `.h`, `.breeze`) to `aeroeditor.elf` if present.

- [ ] **Step 1: Update `WindoseBridge.LaunchApp` to prioritize ELF binaries**
  Check `/mnt/Programs/<app>.elf` first for all commands (`taskmgr`, `registry`, `editor`, `explorer`, `terminal`, `calc`) before falling back to C# classes.
- [ ] **Step 2: Update Start Menu items to prioritize ELF binaries**
  In `StartMenu.cs`, wire left-column items to launch via `WindoseBridge.LaunchApp` (or check for `.elf` first).
- [ ] **Step 3: Update Taskbar context menu to launch `taskmgr.elf`**
  In `Taskbar.cs`, update "Task Manager" action to launch `taskmgr` via `LaunchApp` or `ElfRunner.RunFile("/mnt/Programs/taskmgr.elf")`.
- [ ] **Step 4: Update Desktop shortcuts and FileAssociations to route to ELF binaries**
  Ensure desktop shortcuts point to `/mnt/Programs/*.elf` and `FileAssociations` routes text files to `aeroeditor.elf`.
- [ ] **Step 5: Verify end-to-end build with `cosmos build`**
  Run `cosmos build` and verify exit code 0.
- [ ] **Step 6: Commit changes**
  `git add System/ELF/WindoseBridge.cs System/Shell/StartMenu.cs System/Shell/Desktop.cs System/Shell/Taskbar.cs System/Kernel/Subsystem/FileAssociations.cs; git commit -m "Prioritize ELF binaries across shell launch points, start menu, taskbar, and file associations"`
