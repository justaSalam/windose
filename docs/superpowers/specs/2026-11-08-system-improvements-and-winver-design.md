# Design Spec: System Polish, ELF App Enhancements, and WinVer

**Author:** DeepMind Agentic Coding Team  
**Date:** 2026-11-08  
**Status:** Approved  

---

## 1. Overview
Following the successful implementation of core stability fixes, compositor dirty-region clustering, and ELF prioritization across the primary shell launchers, this specification covers targeted stability and polish improvements across the operating system:
1. Fix the double-launch bug in `explorer.c` where file opening triggers both external launch and internal file viewing.
2. Prioritize ELF programs in `Programs/RunDialog.cs` (`Win+R`).
3. Add declarative layout anchors to `terminal.c`, `explorer.c`, and `search.c`, and enforce fixed geometry on `run.c`.
4. Provide interactive mouse resize cursor (`CursorType.SizeNWSE`) visual feedback when hovering over window resize handles.
5. Implement a native x86-64 ELF `winver.c` system utility and route `winver`/`about` commands to it.
6. Unify common launcher aliases in `CommandRegistry.LaunchOrRun`.

---

## 2. Component Design & Changes

### 2.1 Bug Fix: Explorer Dual Launch (`explorer.c`)
- **Current Behavior:** `if (k_open_file(path) <= 0) open_viewer(path);`
- **Root Cause:** `k_open_file` calls `WindoseBridge.OpenFile` which returns `0` on success and `< 0` on failure. `0 <= 0` evaluates to true, so Explorer launches the associated program and then immediately opens its internal text/hex viewer.
- **Fix:** Update check to `if (k_open_file(path) != 0) open_viewer(path);`.

### 2.2 Shell Consistency: Run Dialog ELF Routing (`Programs/RunDialog.cs`)
- In `RunDialog.ExecuteRun`:
  - Before falling back to C# implementations of `calc`, `taskmgr`, `regedit`, `explorer`, `editor`/`notepad`, and `winver`, check if `/mnt/Programs/<app>.elf` exists on disk.
  - If it exists, launch via `ElfRunner.RunFile` (or `FileAssociations.Launch`).
  - Close the Run dialog after dispatching the process.

### 2.3 ELF Utility Layout Anchoring
- **`terminal.c`:**
  - Anchor the terminal view: `k_widget_set_anchors(term, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT | ANCHOR_BOTTOM);`.
- **`explorer.c`:**
  - Tree view: `k_widget_set_anchors(tree, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_BOTTOM);`.
  - Files listbox: `k_widget_set_anchors(files, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT | ANCHOR_BOTTOM);`.
  - Status bar: `k_widget_set_anchors(sb, ANCHOR_LEFT | ANCHOR_RIGHT | ANCHOR_BOTTOM);`.
  - Address textfield: `k_widget_set_anchors(addrField, ANCHOR_LEFT | ANCHOR_TOP);`.
  - Search label & textfield: `k_widget_set_anchors(searchLab, ANCHOR_TOP | ANCHOR_RIGHT);`, `k_widget_set_anchors(searchField, ANCHOR_TOP | ANCHOR_RIGHT);`.
- **`search.c`:**
  - Search query textfield: `k_widget_set_anchors(field, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT);`.
  - Search button: `k_widget_set_anchors(goBtn, ANCHOR_TOP | ANCHOR_RIGHT);`.
  - Results listbox: `k_widget_set_anchors(list, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT | ANCHOR_BOTTOM);`.
  - Status label: `k_widget_set_anchors(statusLab, ANCHOR_LEFT | ANCHOR_BOTTOM);`.
- **`run.c`:**
  - Add `WIN_FLAG_NO_RESIZE` to window creation flags so dialog geometry stays fixed at 420x140.

### 2.4 Interactive Window Resize Cursor
- In `System/GUI/Window API/Window.cs`:
  - When `mouseState.left == MouseEvents.None`, check `ResizeHitTest(mouseX, mouseY)`.
  - If hit test is true and `canResize` is true, call `CursorManager.SetCursor(CursorType.SizeNWSE)`.
  - When resizing starts or continues, maintain `CursorType.SizeNWSE`.
  - When mouse leaves resize margin and is not resizing, restore `CursorType.Arrow`.

### 2.5 Native ELF `winver.c`
- **Location:** `System/ELF/programs/src/winver.c`
- **Window:** 400x260, title "About Windose", `WIN_FLAG_NO_RESIZE`.
- **Icon:** `/mnt/System/Icons/about.png` or `shell32.dll,-25`.
- **Contents:**
  - OS title label: "Windose Operating System v10 (Aero)"
  - Version label: "Kernel Version 14 (Native x86-64 ELF Subsystem)"
  - Memory display: Total memory and active process count from `k_ps_list`.
  - Uptime display: Hours/minutes calculated from `k_ticks()`.
  - Copyright: "Copyright (c) 2026 Windose Project Contributors."
  - "OK" button anchored bottom-right to close the dialog.
- **Build & Integration:**
  - Compiled to `bin/winver.elf` via `build.bat`.
  - Registered as `<EmbeddedResource>` in `Windose.csproj`.
  - Deployed in `ResourceLoader.cs` to `/mnt/Programs/winver.elf`.
  - Wired into `StartMenu.cs`, `RunDialog.cs`, and `WindoseBridge.LaunchApp`.

---

## 3. Verification Plan
- `System/ELF/programs/build.bat` compiles all ELF programs without warnings or link errors.
- `cosmos build` verifies compilation of kernel, resources, and shell components into `output-x64/Windose.iso`.
- Commits are atomic per task.
