# Windows 7 Aero Experience & Expanded ELF Kernel API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement 8-direction window resizing, dynamic cursors, Aero Snap docking previews, standard Windows 7 hotkeys, taskbar toggle and window menus, Windows 7 dual-column Start Menu, built-in applets (Run, winver, Calculator, Paint), system audio feedback, and the ELF Kernel API v13 expansion for Windose.

**Architecture:** Window management extensions in `Window.cs` and `WindowManager.cs` provide 8-direction hit testing and translucent Aero Snap outlines. `CursorManager.cs` supplies procedural cursor states blitted by `DisplayManager.cs`. `Taskbar.cs` and `StartMenu.cs` provide authentic Windows 7 taskbar toggles and dual-column navigation. New C# applets (`RunDialog.cs`, `AboutWindows.cs`, `Calculator.cs`, `Paint.cs`) supply classic utilities with first-class Aero frames. `AudioEngine.cs` generates in-memory WAV audio for shell events. `ElfRunner.cs`, `WindoseBridge.cs`, and C headers extend `KernelApi` to v13 for complete ELF developer parity.

**Tech Stack:** C# 12 / .NET 10 (NativeAOT), Cosmos Kernel GUI, `Cosmos.Kernel.System.Graphics`, C99 ELF toolchain.

**Spec:** `docs/superpowers/specs/2026-11-02-windows-7-aero-and-elf-api-design.md`

## Global Constraints
- Every change must compile cleanly via `dotnet build` with zero errors.
- Preserve backward binary compatibility for existing ELF programs by strictly appending new functions at the end of `KernelApi`.
- Only PCM `.wav` format is supported for audio playback on Cosmos.
- Maintain documentation integrity and preserve existing comments.

## Review Focus
1. Window resizing in all 8 directions clamps to minimum bounds (120x60) without inverted negative coordinates.
2. Un-maximizing or un-snapping by dragging the titlebar down smoothly restores floating geometry without jumping off-screen.
3. System sound effects triggered during active `MediaPlayer` playback do not abort or crash background audio streams.
4. Calling `RunDialog`, `Calculator`, or `Paint` via Start Menu, Run, or shell command launches the new native C# window seamlessly.
5. Invoking new ELF Kernel API v13 functions from native C binaries properly dispatches through `WindoseBridge` without pointer alignment faults.

---

### Task 1: Fix Existing Compilation Defects & Baseline Build

**Files:**
- Modify: `System/Media/AviDecoder.cs:300-307`
- Modify: `System/Shell/NetworkFlyout.cs:88-105`

**Interfaces:**
- Consumes: `AudioEngine.WrapPcmInWave(byte[], int, ushort, ushort)`
- Produces: Clean baseline build of `Windose.csproj`

- [ ] **Step 1: Fix type conversion errors in `System/Media/AviDecoder.cs`**
Cast `AudioChannels` and `AudioBitsPerSample` to `(ushort)` in line 303.
- [ ] **Step 2: Fix non-null comparison warnings in `System/Shell/NetworkFlyout.cs`**
Replace redundant null checks on non-nullable `NetworkAdapter` with valid state checks.
- [ ] **Step 3: Verify clean `dotnet build`**
Run `dotnet build` and ensure 0 errors.
- [ ] **Step 4: Commit baseline fixes**
`git add System/Media/AviDecoder.cs System/Shell/NetworkFlyout.cs; git commit -m "fix: resolve AviDecoder and NetworkFlyout build warnings"`

---

### Task 2: Dynamic Cursor Subsystem

**Files:**
- Create: `System/Drivers/CursorManager.cs`
- Modify: `Resources/Cursors/Cursors.cs`
- Modify: `System/Drivers/DisplayManager.cs:20-70`

**Interfaces:**
- Produces:
  - `public enum CursorType { Arrow, Hand, IBeam, SizeWE, SizeNS, SizeNWSE, SizeNESW, SizeAll, Wait }`
  - `public static class CursorManager { public static CursorType Current; public static void SetCursor(CursorType); public static void SetBusy(bool); public static (int hotspotX, int hotspotY) GetHotspot(CursorType); public static Bitmap GetCursorBitmap(CursorType); }`
  - `DisplayManager.Present(...)` rendering the active cursor at `(mx - hotspotX, my - hotspotY)`

- [ ] **Step 1: Create `System/Drivers/CursorManager.cs`**
Implement `CursorManager` with procedural 16x16 bitmaps for standard cursors:
- Default Arrow `↖`
- Hand / Pointer `👆`
- Text I-Beam `I`
- Horizontal resize `↔` (`SizeWE`)
- Vertical resize `↕` (`SizeNS`)
- Diagonal resizes `↖↘` (`SizeNWSE`) and `↗↙` (`SizeNESW`)
- Move `✥` (`SizeAll`)
- Hourglass / Wait `⏳`
- [ ] **Step 2: Update `Resources/Cursors/Cursors.cs`**
Expose `CursorManager` properties for backwards compatibility.
- [ ] **Step 3: Update `System/Drivers/DisplayManager.cs`**
In `Present()`, query `CursorManager.GetCursorBitmap()` and hotspot coordinates to blit the active cursor.
- [ ] **Step 4: Verify build and commit**
Run `dotnet build` and commit: `git commit -m "feat: implement dynamic cursor subsystem with procedural cursor states"`

---

### Task 3: 8-Direction Window Resizing & Aero Snap Docking Previews

**Files:**
- Modify: `System/GUI/Window API/Window.cs:55-70, 560-698`
- Modify: `System/GUI/Window API/WindowManager.cs:20-60`
- Modify: `System/GUI/Window API/WindowManager.Update.cs:40-80, 140-165`

**Interfaces:**
- Produces:
  - `[Flags] public enum ResizeBorder { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }`
  - `public ResizeBorder HitTestResizeBorder(int mouseX, int mouseY)`
  - Smooth 8-direction sizing preview and window resizing
  - Top edge drag -> maximize preview; Left/Right edge drag -> 50% split preview
  - Dragging down maximized/snapped titlebar restores floating size centered under cursor

- [ ] **Step 1: Implement `HitTestResizeBorder` in `Window.cs`**
Implement 6px outer boundary detection classifying all 8 borders and corners.
- [ ] **Step 2: Wire resize cursor updating in `Window.HandleInput` / `WindowManager.Update`**
When hovering over window resize edges without mouse press, set the cursor:
- Left / Right: `CursorType.SizeWE`
- Top / Bottom: `CursorType.SizeNS`
- Top-Left / Bottom-Right: `CursorType.SizeNWSE`
- Top-Right / Bottom-Left: `CursorType.SizeNESW`
- [ ] **Step 3: Implement 8-direction sizing in `Window.ResizeWindow`**
Maintain initial `resizeStartBounds` and `activeResizeBorder`. Calculate new dimensions enforcing `Width >= 120` and `Height >= 60`.
- [ ] **Step 4: Implement Aero Snap docking preview and restore in `Window.DragWindow`**
When `mouseY <= 6`, preview full work area. When `mouseX <= 6` or `mouseX >= ScreenWidth - 6`, preview 50% split-screen.
Render translucent docking outline in `WindowManager.DrawPreviewRect`.
Support dragging down titlebar of maximized/snapped window to restore floating geometry.
- [ ] **Step 5: Verify build and commit**
Run `dotnet build` and commit: `git commit -m "feat: implement 8-direction window resizing and Aero Snap docking previews"`

---

### Task 4: Window System Menu & Standard Windows 7 Keyboard Shortcuts

**Files:**
- Modify: `System/GUI/Window API/Window.cs:695-750`
- Modify: `System/GUI/Window API/WindowManager.Update.cs:265-315`
- Modify: `System/Features/Hotkeys/HotkeyManager.cs:15-50`

**Interfaces:**
- Produces:
  - Window System Menu with Restore, Move, Size, Minimize, Maximize, Close (`Alt+F4`)
  - Global hotkeys: `Alt+F4`, `Alt+Space`, `Ctrl+Esc`, `Key.LWin`/`Key.RWin`, `Win+R`/`Alt+R`, `Win+E`/`Alt+E`, `Win+D`/`Ctrl+Alt+D`, `Win+Arrows`/`Alt+Arrows`

- [ ] **Step 1: Implement `ShowSystemMenu(int x, int y)` in `Window.cs`**
Create popup menu with state-aware enabled/disabled items (Restore, Move, Size, Minimize, Maximize, Close).
- [ ] **Step 2: Hook titlebar triggers in `Window.cs`**
Left-clicking titlebar icon or right-clicking titlebar opens the System Menu.
- [ ] **Step 3: Wire keyboard shortcuts in `WindowManager.Update.cs` and `HotkeyManager.cs`**
- `Alt+F4`: closes focused window.
- `Alt+Space`: opens System Menu for focused window.
- `Ctrl+Esc` and `Key.LWin` / `Key.RWin`: toggles Start Menu.
- `Win+R` / `Alt+R`: opens Run Dialog.
- `Win+E` / `Alt+E`: opens File Explorer.
- `Win+D` / `Ctrl+Alt+D`: toggles Show Desktop.
- `Win+Up` / `Alt+Up`: maximizes focused window.
- `Win+Down` / `Alt+Down`: restores or minimizes focused window.
- `Win+Left` / `Win+Right` / `Alt+Left` / `Alt+Right`: snaps focused window to half screen.
- [ ] **Step 4: Verify build and commit**
Run `dotnet build` and commit: `git commit -m "feat: add window system menu and Windows 7 global hotkeys"`

---

### Task 5: Taskbar Window Minimize Toggle & Windows 7 Dual-Column Start Menu

**Files:**
- Modify: `System/Shell/Taskbar.cs:80-230`
- Modify: `System/Shell/StartMenu.cs:20-350`

**Interfaces:**
- Produces:
  - Taskbar button click toggles minimize/restore on active window
  - Taskbar button right-click opens window System Menu
  - Windows 7 dual-column Start Menu with profile shortcuts, search bar, and shutdown split flyout

- [ ] **Step 1: Update taskbar button interaction in `Taskbar.cs`**
- Left-click on focused window's button minimizes the window.
- Left-click on unfocused or minimized window's button restores it and brings it to front.
- Right-click on taskbar button opens window's System Menu adjacent to the taskbar.
- [ ] **Step 2: Overhaul Start Menu to Windows 7 dual-column design in `StartMenu.cs`**
- Left column: search field at bottom, programs list with icons.
- Right column: user tile, links to User Folder, Documents, Pictures, Music, Computer ("This PC"), Control Panel, Run...
- Bottom right: **Shut down** button with flyout menu (Restart, Lock, Log Off).
- [ ] **Step 3: Verify build and commit**
Run `dotnet build` and commit: `git commit -m "feat: update Taskbar window toggling and Windows 7 dual-column Start Menu"`

---

### Task 6: Built-in Windows 7 Applets (Run Dialog, Winver, Calculator, MS Paint)

**Files:**
- Create: `Programs/RunDialog.cs`
- Create: `Programs/AboutWindows.cs`
- Create: `Programs/Calculator.cs`
- Create: `Programs/Paint.cs`
- Modify: `System/Shell/Commands/CommandRegistry.cs`
- Modify: `System/Kernel/Subsystem/FileAssociations.cs`

**Interfaces:**
- Produces:
  - `class RunDialog : Window` (`Win+R`, `run` command)
  - `class AboutWindows : Window` (`winver` command)
  - `class Calculator : Window` (`calc` command)
  - `class Paint : Window` (`mspaint` / `paint` command)

- [ ] **Step 1: Create `Programs/RunDialog.cs`**
Aero-framed dialog with prompt text, text field with Enter handler, OK button (launches target), Cancel button, and Browse button (opens `FileDialog`).
- [ ] **Step 2: Create `Programs/AboutWindows.cs`**
Aero dialog displaying Windows 7 logo banner, system version, OS build, available RAM, CPU model, registered username, and OK button.
- [ ] **Step 3: Create `Programs/Calculator.cs`**
Standard Windows 7 layout (260x320px) with digital display, buttons 0-9, decimal, arithmetic (+, -, *, /), =, +/-, %, sqrt, 1/x, backspace, CE, C, and keyboard input handling.
- [ ] **Step 4: Create `Programs/Paint.cs`**
Windows 7 Paint applet (720x520px) with canvas, tools (Pencil, Brush, Eraser, Fill Bucket, Line, Rectangle, Ellipse), color palette swatch, and stroke width selector.
- [ ] **Step 5: Register shell commands and file associations**
Register `run`, `winver`, `calc`, and `mspaint`/`paint` in `CommandRegistry.cs` and `FileAssociations.cs`.
- [ ] **Step 6: Verify build and commit**
Run `dotnet build` and commit: `git commit -m "feat: implement Run Dialog, About Windows, Calculator, and MS Paint applets"`

---

### Task 7: Windows 7 Audio Feedback Subsystem

**Files:**
- Modify: `System/Media/AudioEngine.cs:30-80, 315-368`
- Modify: `Kernel.cs`
- Modify: `System/Shell/StartMenu.cs`
- Modify: `System/Shell/Desktop.cs`

**Interfaces:**
- Produces:
  - `AudioEngine.PlayStartup()`, `AudioEngine.PlayClick()`, `AudioEngine.PlayError()`, `AudioEngine.PlayTrash()`
  - Audio playback that safely coexists with background `MediaPlayer`

- [ ] **Step 1: Add sound synthesis methods in `AudioEngine.cs`**
Synthesize 44.1 kHz 16-bit WAV waveforms in memory:
- Startup chime: harmonic chord
- Navigation click: short 15ms click
- Exclamation / Error: bell chord
- Empty Recycle Bin: swoosh noise
- [ ] **Step 2: Integrate audio triggers into shell events**
- Boot sequence calls `AudioEngine.Instance.PlayStartup()`.
- Start Menu item selection calls `PlayClick()`.
- MessageBox error dialogs call `PlayError()`.
- Recycle Bin emptying calls `PlayTrash()`.
- [ ] **Step 3: Verify build and commit**
Run `dotnet build` and commit: `git commit -m "feat: add Windows 7 audio feedback subsystem and shell sound hooks"`

---

### Task 8: Expanded ELF Kernel API v13 & C Headers

**Files:**
- Modify: `System/ELF/ElfRunner.cs:13-75, 120-180, 2450-2550`
- Modify: `System/ELF/IElfHost.cs:15-50`
- Modify: `System/ELF/WindoseBridge.cs:50-120`
- Modify: `System/ELF/programs/include/windose/core.h:10-40, 160-175`
- Modify: `System/ELF/programs/include/windose/gui.h`
- Modify: `System/ELF/programs/include/windose/sys.h`
- Modify: `System/ELF/programs/include/kernel.h`

**Interfaces:**
- Produces:
  - `KAPI_VERSION 13`
  - `KernelApi` struct extended with:
    - `play_sound`, `play_wave`, `set_cursor`, `window_minimize`, `window_maximize`, `window_restore`, `window_get_state`, `shell_run_dialog`, `shell_about_dialog`, `get_system_metrics`
  - C wrappers in `kernel.h`, `windose/sys.h`, `windose/gui.h`
  - System metrics (`SM_*`) and window state (`WINDOW_STATE_*`) constants

- [ ] **Step 1: Update `System/ELF/programs/include/windose/core.h` and `kernel.h`**
Append new function pointers to `KernelApi` and bump `KAPI_VERSION 13`. Define `SM_*` and `WINDOW_STATE_*` constants.
- [ ] **Step 2: Add C API wrapper functions in `sys.h`, `gui.h`, and `kernel.h`**
Implement `k_play_sound`, `k_play_wave`, `k_set_cursor`, `k_window_minimize`, `k_window_maximize`, `k_window_restore`, `k_window_get_state`, `k_shell_run_dialog`, `k_shell_about_dialog`, `k_get_system_metrics`.
- [ ] **Step 3: Update `KernelApi` struct in `System/ELF/ElfRunner.cs`**
Add matching delegates at the end of `struct KernelApi` and register `[UnmanagedCallersOnly]` handler functions in `FillApi`.
- [ ] **Step 4: Implement host methods in `IElfHost.cs` and `WindoseBridge.cs`**
Connect delegates to `AudioEngine`, `CursorManager`, `WindowManager`, and shell dialogs.
- [ ] **Step 5: Verify build and commit**
Run `dotnet build` and commit: `git commit -m "feat: extend ELF Kernel API to v13 with audio, cursors, and window controls"`

---

### Task 9: Full Build Verification & System Integration Review

**Files:**
- Review all touched files across `System/`, `Programs/`, `Resources/`

- [ ] **Step 1: Run comprehensive build verification**
Execute `dotnet build` to verify clean build with zero errors.
- [ ] **Step 2: Inspect git diff and status**
Verify all changes adhere to project patterns and constraints.
- [ ] **Step 3: Final commit**
Commit any remaining integration adjustments.
