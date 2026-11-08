# Implementation Plan: System Polish, ELF App Enhancements, and WinVer

**Date:** 2026-11-08  
**Design Spec:** [docs/superpowers/specs/2026-11-08-system-improvements-and-winver-design.md](file:///c:/Users/salam/Desktop/windose/docs/superpowers/specs/2026-11-08-system-improvements-and-winver-design.md)  

---

### Task 1: Fix Explorer Dual-Launch Bug & RunDialog Routing
- **Step 1:** In `System/ELF/programs/src/explorer.c`, change `open_selected()` check to `if (k_open_file(path) != 0) open_viewer(path);`.
- **Step 2:** In `Programs/RunDialog.cs`, update `ExecuteRun` to check `File.Exists("/mnt/Programs/<name>.elf")` for `calc`, `taskmgr`, `regedit`, `explorer`, `editor`/`notepad`, and `winver`.
- **Step 3:** Recompile ELF binaries via `cmd.exe /c "cd System\ELF\programs && build.bat explorer"`.
- **Step 4:** Run `cosmos build` to verify C# compilation.
- **Step 5:** Commit: `git add System/ELF/programs/src/explorer.c Programs/RunDialog.cs; git commit -m "Fix explorer dual-launch and prioritize ELF in Run dialog"`.

---

### Task 2: Responsive Layout Anchoring in ELF Utilities
- **Step 1:** In `System/ELF/programs/src/terminal.c`, set anchors on `term`: `k_widget_set_anchors(term, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT | ANCHOR_BOTTOM);`.
- **Step 2:** In `System/ELF/programs/src/explorer.c`, set anchors on `tree` (`L|T|B`), `files` (`L|T|R|B`), `sb` (`L|R|B`), `addrField` (`L|T`), and `searchField`/`searchLab` (`T|R`).
- **Step 3:** In `System/ELF/programs/src/search.c`, set anchors on `field` (`L|T|R`), `goBtn` (`T|R`), `list` (`L|T|R|B`), and `statusLab` (`L|B`).
- **Step 4:** In `System/ELF/programs/src/run.c`, pass `WIN_FLAG_NO_RESIZE` to `k_create_window_ex`.
- **Step 5:** Recompile all ELF binaries via `cmd.exe /c "cd System\ELF\programs && build.bat"`.
- **Step 6:** Run `cosmos build` to verify image creation.
- **Step 7:** Commit: `git add System/ELF/programs/src/; git commit -m "Add responsive anchors to terminal, explorer, search and fixed geometry to run"`.

---

### Task 3: Interactive Window Resize Cursor
- **Step 1:** In `System/GUI/Window API/Window.cs`, update `HandleEvents` or `ResizeWindow` to detect when mouse is over `ResizeHitTest`.
- **Step 2:** If hovered over the resize handle and `canResize` is true, call `Windose.Drivers.CursorManager.SetCursor(CursorType.SizeNWSE)`.
- **Step 3:** While resizing, keep `CursorType.SizeNWSE`.
- **Step 4:** When mouse leaves the resize handle or drag finishes, restore `CursorType.Arrow`.
- **Step 5:** Run `cosmos build` to verify compilation.
- **Step 6:** Commit: `git add System/GUI/Window\ API/Window.cs; git commit -m "Provide dynamic mouse resize cursor on window grip hover"`.

---

### Task 4: Native ELF `winver.c` & Shell Integration
- **Step 1:** Create `System/ELF/programs/src/winver.c`:
  - 400x260 fixed window with title "About Windose", `WIN_FLAG_NO_RESIZE`.
  - Icon `/mnt/System/Icons/about.png`.
  - Labels showing OS version, kernel API version, process count, uptime, and copyright.
  - "OK" button to close the window.
- **Step 2:** Compile via `build.bat winver`.
- **Step 3:** Register `bin\winver.elf` in `Windose.csproj` as `<EmbeddedResource>`.
- **Step 4:** Add file deployment in `ResourceLoader.cs` (`SysRequired()`) to write `/mnt/Programs/winver.elf`.
- **Step 5:** Update `StartMenu.cs`, `RunDialog.cs`, and `WindoseBridge.LaunchApp` to launch `/mnt/Programs/winver.elf` for `winver`/`about`.
- **Step 6:** Run `cosmos build` to verify image creation.
- **Step 7:** Commit: `git add System/ELF/programs/src/winver.c Windose.csproj System/Kernel/ResourceLoader.cs System/Shell/StartMenu.cs Programs/RunDialog.cs System/ELF/WindoseBridge.cs; git commit -m "Implement native ELF winver utility and integrate across shell"`.

---

### Task 5: End-to-End Verification
- **Step 1:** Run `cmd.exe /c "cd System\ELF\programs && build.bat"` to ensure libc and all 25+ ELF binaries build cleanly.
- **Step 2:** Run `cosmos build` to ensure the final OS kernel ISO builds without warnings or errors.
- **Step 3:** Confirm git status is clean and all tasks are accounted for in the tracking ledger.
