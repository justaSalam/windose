# System Stability and ELF Subsystem Prioritization Design

## 1. Intent & The Why

Windose has evolved from a simple managed C# environment into a hybrid operating system capable of executing native x86-64 ELF binaries directly in Ring 0. However, the system currently suffers from two major structural tensions:

1. **System Stability & Compositor Inefficiencies:** Latent bugs (such as a recursive property setter causing stack overflows on elevation changes, un-formatted account serialization, and worker threads pinning CPU cores with `Thread.Sleep(0)`) hinder reliability. In addition, the compositor invalidates and redraws whole-screen bounding boxes on disconnected screen updates.
2. **Ecosystem Duality & ELF Incompleteness:** The system maintains duplicate implementations of basic apps (C# vs C/ELF). Despite having an ELF runtime, ELF applications are second-class citizens because `KernelApi` lacks essential OS capabilities (process inspection, registry manipulation, explicit control layout anchoring), forcing `ElfGui.cs` into fragile heuristic layout guessing.

The goal of this design is to resolve all core system bugs and transform native ELF programs into the **primary, first-class application tier** of Windose, while keeping C# managed code as the robust kernel and fallback infrastructure.

---

## 2. Goals, Non-Goals, & Anti-Goals

### Goals
- Fix all identified core stability bugs (`Session.isElevated` recursion, `UserAccount.CreateAccount` formatting, `ScheduledProcess` CPU spinning, duplicate hotkeys).
- Optimize dirty-region compositing in `WindowManager` to avoid full-screen clearing when updating disparate screen coordinates.
- Remove dead code (`MlfqScheduler.cs`, dead usings/scripts) to keep the kernel clean.
- Expand `KernelApi` and `WindoseBridge` with APIs needed for complete system apps:
  - Process enumeration and termination (`ps_list`, `ps_kill`).
  - Registry querying, enumeration, and updating (`reg_list`, `reg_get`, `reg_set`).
  - Explicit declarative control anchoring (`widget_set_anchors`) and dynamic widget bounds control (`widget_set_bounds`) to replace heuristic guessing in `ElfGui.cs`.
- Implement missing core utilities as native ELF binaries (`taskmgr.c`, `regedit.c`) and improve existing ELF apps (`explorer.c`, `terminal.c`, `calc.c`, `aeroeditor.c`).
- Prioritize `.elf` binaries across all desktop entry points: Start Menu, Desktop shortcuts, Taskbar, Hotkeys, Run dialog, and File Associations.

### Non-Goals
- Hardware memory protection / Ring 3 paging (all ELF code runs in Ring 0 per current architecture).
- Full reimplementation or deletion of `System/Web` in this milestone (deferred to a dedicated browser engine refactor).
- Removing C# fallback classes entirely (keeping C# fallbacks provides safety if an ELF binary is missing or fails to load).

### Anti-Goals
- Breaking binary or struct compatibility between `KernelApi` in C# and `kapi.h` in C.
- Breaking the bottom-right corner window scaling behavior restored in commit `7dd1ae9`.

---

## 3. Approaches Considered & Selected

### Approach 1: Phased Stabilization & ELF Platform Expansion (Selected)
- **Phase 1:** Fix kernel stability bugs, compositor dirty-rect invalidation, scheduled process sleep, and remove dead code.
- **Phase 2:** Expand `KernelApi` (`kapi.h` & `WindoseBridge.cs`) with process management, registry, and declarative control anchoring; replace heuristic layout in `ElfGui.cs`.
- **Phase 3:** Implement native ELF utilities (`taskmgr.c`, `regedit.c`), enhance existing ELF apps, compile via `build.bat`, embed into assembly resources, and update the shell/launcher to prioritize ELF binaries everywhere.
- **Why Chosen:** Eliminates risks of regression by establishing a solid kernel and API foundation before deploying native C applications.

### Approach 2: Full In-Place Rewrite (Rejected)
Attempting all changes across 15+ subsystems simultaneously creates severe build/debug friction and makes diagnosing NativeAOT compilation failures difficult.

### Approach 3: ELF-Only Aggressive Cutover (Rejected)
Deleting C# utilities immediately leaves the OS with zero fallback if an ELF binary crashes or is missing from disk during early boot.

---

## 4. Detailed Design & Architecture Calls

### Phase 1: Core Stability & Compositor Optimization

#### 1.1 `Session.isElevated` Fix
In `System/Kernel/Subsystem/Session.cs`:
- Introduce private static backing field `private static bool _isElevated;`.
- Getter returns `_isElevated || (CurrentUser != null && CurrentUser.privilege is PrivilegeLevel.Administrator or PrivilegeLevel.System)`.
- Setter writes `_isElevated = value;`, resolving the recursive stack overflow.

#### 1.2 `UserAccount.CreateAccount` Fix
In `System/Kernel/Subsystem/UserAccount.cs`:
- If `profilePath` already exists, do not reject account creation; ensure the directory exists and continue.
- In `File.AppendAllText`, append `Environment.NewLine` (`$"{account.username}:{account.password}:{account.privilege}{Environment.NewLine}"`).

#### 1.3 `ScheduledProcess` CPU Usage Fix
In `System/Process/ScheduledProcess.cs`:
- In `WorkerLoop()`, replace `Thread.Sleep(0)` with:
  ```csharp
  int interval = GetNextUpdateIntervalMs();
  Thread.Sleep(interval > 0 ? interval : 10);
  ```
  This prevents background threads from spinning continuously at 100% CPU.

#### 1.4 Deduplicate System Hotkeys
In `Kernel.cs`:
- Remove duplicate calls to `HotkeyManager.RegisterHotkey(...)` that are already registered inside `HotkeyManager`'s constructor.

#### 1.5 Compositor Dirty-Region Redraw Optimization
In `System/GUI/Window API/WindowManager.cs` (`ComposeDirtyRegions`):
- Cluster dirty rectangles: merge only rectangles that intersect or directly touch.
- For disjointed dirty areas (e.g., cursor blink and clock tick), clip and render each cluster individually.
- This prevents a top-left change and bottom-right change from blowing out into a 1920x1080 full-screen clear and repaint.

#### 1.6 Dead Code & Cleanup
- Remove unused 833-line `System/Kernel/MlfqScheduler.cs`.
- Clean up unused using directives across `Desktop.cs` and `StartMenu.cs`.

---

### Phase 2: ELF Subsystem Expansion & Declarative Control Anchoring

#### 2.1 `KernelApi` and `kapi.h` Extensions
Extend `KernelApi` in both `System/ELF/ElfRunner.cs` and `System/ELF/programs/include/kernel.h` with new system calls (appended to maintain binary compatibility):
1. **Process Management (`ps_list`, `ps_kill`):**
   - `ps_list(pid, char *buf, int max_bytes) -> int count`: Formats running processes from `ProcessManager.processes` into buffer (`pid:name:type:mem\n`). Uses unified `Process.id` and reports memory for ELF tasks via `ElfProgram.MemoryBytes`.
   - `ps_kill(pid, int target_pid) -> int status`: Terminates target process. Verifies `process.canTerminate != false` to prevent killing critical OS processes (such as `Explorer` or `WindowManager`).
2. **Registry Access (`reg_list`, `reg_get`, `reg_set`):**
   - `reg_list(pid, const char *prefix, char *buf, int max_bytes) -> int count`: Lists all registry keys matching the prefix separated by `\n`.
   - `reg_get(pid, const char *key, char *val_buf, int max_bytes) -> int len`: Reads registry string value.
   - `reg_set(pid, const char *key, const char *val) -> int status`: Writes registry value. Automatically coerces to `long` or `bool` if the key already exists as a numeric or boolean entry in `Registry.cs`.
3. **Declarative Control Anchoring & Widget Bounds:**
   - `widget_set_anchors(pid, int handle, int anchor_flags) -> int status`:
     Flags: `ANCHOR_LEFT = 1`, `ANCHOR_TOP = 2`, `ANCHOR_RIGHT = 4`, `ANCHOR_BOTTOM = 8`.
   - `widget_set_bounds(pid, int handle, int x, int y, int w, int h) -> int status`:
     Allows programs (like custom calculators or dialogs) to reposition and resize widgets dynamically.

#### 2.2 Replacing Heuristics in `ElfGui.cs`
- In `ElfWindow`:
  - Store `AnchorFlags` for each child component.
  - On `OnWindowResized()`, calculate each child's new bounds using its explicit anchor flags:
    - If anchored `LEFT | RIGHT`: width stretches with window width.
    - If anchored only `RIGHT`: component moves horizontally with right edge.
    - If anchored `TOP | BOTTOM`: height stretches with window height.
    - If anchored only `BOTTOM`: component moves vertically with bottom edge.
    - Default (no anchors): remains anchored `TOP | LEFT`.
  - Delete the 700 lines of brittle heuristic cases (`isCenteredDialog`, `buttonCount >= 10`, etc.).
  - Dispatch `ElfEvent.Resize` to the owning ELF program so custom-drawn canvases or complex apps can re-layout their own custom content.

---

### Phase 3: Native ELF Applications & Priority Wiring

#### 3.1 New & Enhanced ELF Programs
1. **`taskmgr.c` (Task Manager):**
   - Windowed application with title "Task Manager".
   - Shows active processes (PID, Name, Memory) in a `DataGrid` or `ElfListBox`.
   - "End Process" button calling `ps_kill`.
   - "Refresh" button and periodic timer refresh.
2. **`regedit.c` (Registry Editor):**
   - Windowed application displaying system registry entries discovered via `reg_list`.
   - Text fields to search/view keys and update values via `reg_get` and `reg_set`.
3. **`explorer.c` (File Explorer Enhancements):**
   - Double-clicking `.elf` files executes them via `spawn_proc`.
   - Double-clicking text/media files calls `open_file`.
   - Breadcrumb navigation and proper directory traversal.
4. **`calc.c` (Calculator):**
   - Standard fixed-size window (`canResize = false`) matching Windows Calculator conventions.
5. **`aeroeditor.c` (Text Editor):**
   - Accepts CLI argument `argv[1]` for initial file loading.
   - Implements functional `New`, `Open` (reads file via `k_read_file`), and `Save` (writes buffer via `k_write_file`).
   - Anchors text buffer to `TOP | LEFT | RIGHT | BOTTOM` to stretch cleanly on window resize.

#### 3.2 Prioritizing ELF Programs Across the Shell
In `WindoseBridge.LaunchApp`, `StartMenu.cs`, `Desktop.cs`, `Taskbar.cs`, `HotkeyManager.cs`, and `FileAssociations.cs`:
- Always search `/mnt/Programs/<name>.elf` first.
- If the `.elf` binary exists on disk, launch via `ElfRunner.Spawn` / `ElfRunner.RunFile`.
- Fall back to internal C# managed classes only if the ELF binary is missing.

#### 3.3 Build & Packaging Integration
- Ensure `build.bat` compiles all programs (including `taskmgr` and `regedit`) into `bin/*.elf`.
- Ensure `Windose.csproj` embeds all `.elf` binaries as `EmbeddedResource`.
- Update `ResourceLoader.cs` to unpack all `.elf` binaries into `/mnt/Programs/` on first boot.

---

## 5. Verification & Testing

1. **Compilation & Assembly:**
   - Compile all ELF binaries with `System\ELF\programs\build.bat`.
   - Build entire OS with `cosmos build` and ensure exit code 0.
2. **Functional Verification:**
   - Verify `Session.TryElevate` and `Session.Logout` do not trigger StackOverflowException.
   - Verify `ScheduledProcess` CPU utilization remains normal and responsive.
   - Verify taskbar buttons, start menu, desktop shortcuts, and `Alt+R` (Run dialog) launch `.elf` programs.
   - Verify `taskmgr.elf` lists processes and can terminate processes.
   - Verify `aeroeditor.elf` opens and saves text files.
   - Verify window resizing works smoothly with declarative anchors and bottom-right scaling.
