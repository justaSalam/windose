# Windows 7 Aero Experience & Expanded ELF Kernel API Design

**Date:** 2026-11-02  
**Status:** Approved by Builder Review  
**Topic:** Windows 7 Aero Window Management, Dynamic Cursors, Aero Snap, Superbar, Start Menu, Built-in Applets, Audio Feedback, and ELF Kernel API v13  

---

## 1. Intent & Context
Windose is a hobby desktop operating system written in C# on Cosmos (NativeAOT). While it already features double-buffered compositing, Aero glass frames, desktop icons, and basic windowing, it still lacks several fundamental interactions that define the Windows 7 desktop experience:
1. Window resizing is restricted to the bottom-right corner rather than all eight borders and corners (N, S, E, W, NW, NE, SW, SE).
2. The mouse cursor does not change when hovering over window edges, text fields, or hyperlinks, nor during busy waits.
3. Aero Snap lacks translucent docking preview outlines when dragging a window to screen edges.
4. Essential keyboard shortcuts are missing: `Alt+F4` (close window), `Alt+Space` (system menu), `Win` / `Ctrl+Esc` (toggle Start Menu), and `Win+Up/Down/Left/Right` (snap / maximize / restore).
5. Taskbar buttons do not toggle minimize/restore on click, nor do they support right-click window menus.
6. The Start Menu does not follow the Windows 7 dual-column layout (pinned/recent apps + search box on left, user links + shutdown split button on right).
7. Iconic Windows 7 applets are missing: Run Dialog (`Win+R`), About Windows (`winver`), Calculator, and MS Paint.
8. System audio feedback is absent for standard shell events (logon chime, navigation click, exclamation error beep, recycle bin empty).
9. The ELF Kernel API (`KernelApi`) lacks calls for audio playback, dynamic cursor changes, window state management (minimize/maximize/restore), and system metrics queries, limiting native C applications from building first-class Windows experiences.

This project delivers a complete, cohesive Windows 7 Aero desktop experience in Windose and extends the C ELF Kernel API (v13) to full parity so that any application can be authored in C and run natively inside Windose.

---

## 2. Goals, Non-Goals & Anti-Goals

### Goals
1. **8-Direction Window Resizing & Edge Detection**:
   * Windows can be resized smoothly from any edge (Left, Right, Top, Bottom) or corner (Top-Left, Top-Right, Bottom-Left, Bottom-Right).
   * Proper cursor feedback for each resize edge/corner: `SizeWE`, `SizeNS`, `SizeNWSE`, `SizeNESW`.
2. **Dynamic Cursor Subsystem**:
   * `CursorManager` / `Cursors` with standard shapes: Default Arrow, Hand (links/buttons), I-Beam (text fields), Wait/Hourglass (busy), and directional resize cursors.
   * `DisplayManager` dynamically renders the active cursor with proper hotspot offsets.
3. **Aero Snap & Docking Previews**:
   * Dragging a window titlebar to the top screen edge previews a full-screen translucent docking rectangle and maximizes on release.
   * Dragging to the left or right screen edge previews a 50% split-screen translucent docking rectangle and snaps on release.
   * Dragging down a maximized or snapped window restores its original pre-maximize geometry and position centered under the cursor.
4. **Standard Windows 7 Keyboard Shortcuts & System Menus**:
   * `Alt+F4`: closes the currently active/focused window.
   * `Alt+Space`: opens the window's System Menu (Restore, Move, Size, Minimize, Maximize, Close) at the top-left corner.
   * Left-clicking the titlebar icon or right-clicking anywhere on the titlebar opens the System Menu.
   * `Ctrl+Esc` / `Win`: toggles the Start Menu.
   * `Win+R` / `Alt+R`: launches the native Run Dialog.
   * `Win+E` / `Alt+E`: launches File Explorer.
   * `Win+D` / `Ctrl+Alt+D`: toggles Show Desktop.
   * `Win+Up`: maximizes window. `Win+Down`: restores or minimizes window. `Win+Left/Right`: snaps to left/right half.
5. **Windows 7 Superbar & Start Menu**:
   * Clicking the taskbar button of the focused window minimizes it. Clicking an unfocused or minimized window's taskbar button restores and focuses it.
   * Right-clicking any taskbar window button displays the window system menu (Restore, Minimize, Maximize, Close).
   * Windows 7 Start Menu layout with search bar, left application list, right profile shortcuts (User Files, Documents, Pictures, Computer, Control Panel, Run...), and Shut Down split button (Shut Down, Restart, Lock, Log Off).
6. **Built-in Windows 7 Applets**:
   * **Run Dialog (`Win+R`)**: Authentic Windows 7 Run dialog with icon, text entry with history/lookup, OK, Cancel, and Browse buttons; executes programs, shell paths, documents, or URLs.
   * **About Windows (`winver`)**: Displays Windows 7 Aero dialog with system build, version, CPU, registered owner, and memory.
   * **Calculator (`calc`)**: Clean Windows 7 standard calculator with display, digit keypad, arithmetic operations (+, -, *, /), percentage, square root, reciprocal (1/x), sign inversion (+/-), backspace, clear (C/CE), and keyboard support.
   * **MS Paint (`mspaint`)**: Windows 7 drawing applet featuring drawing canvas, tool selection (Pencil, Brush, Eraser, Fill Bucket, Line, Rectangle, Ellipse), palette picker, stroke thickness, New, Open, and Save.
7. **Windows 7 System Sounds & Audio Feedback**:
   * Waveform synthesizer in `AudioEngine` generating authentic Windows sound events:
     * Startup / Logon chord chime
     * Navigation start / folder click sound
     * Exclamation / error beep
     * Recycle Bin empty swoosh
8. **Expanded ELF Kernel API (v13)**:
   * Maintain backward binary compatibility by appending to `KernelApi` without disturbing existing function offsets.
   * New APIs:
     * `k_play_sound(int sound_id)` / `k_play_wave(const void *wave_data, int len)`
     * `k_set_cursor(int cursor_id)`
     * `k_window_minimize(handle_t win)`, `k_window_maximize(handle_t win)`, `k_window_restore(handle_t win)`
     * `k_window_get_state(handle_t win, int *out_state)`
     * `k_shell_run_dialog()`
     * `k_shell_about_dialog()`
     * `k_get_system_metrics(int index)`
   * Update C headers: `windose/core.h`, `windose/gui.h`, `windose/sys.h`, and `kernel.h`.

### Non-Goals
* Re-implementing Microsoft's proprietary closed PE/COFF loader or Win32 COM DLL subsystem; native programs compile as standard 64-bit ELF binaries.
* Writing low-level hardware sound card drivers; audio routes through Cosmos `AudioManager` and `AudioEngine`.

### Anti-Goals
* Breaking existing ELF binaries or changing offsets of existing `KernelApi` (v12) function pointers.
* Heavy memory churn or frame rate degradation during window dragging, resizing, or cursor composition.

---

## 3. Approaches Considered & Decision

### Approach 1: Full Native In-Kernel C# Architecture
Implement window management, 8-way resizing, dynamic cursors, Aero Snap previews, Superbar behaviors, Start Menu, applets (Run, winver, calc, paint), and system audio events directly in C#.
* **Trade-offs**: Immediate zero-latency startup, full access to `DirectBitmap` compositor, glass effects, and type safety, but increases kernel C# source size.

### Approach 2: Hybrid C# Shell with ELF C-based Accessories
Implement window management in C#, but write Calc, Paint, and Run as external ELF C binaries.
* **Trade-offs**: Offloads applet code to C binaries, but ELF apps lack Aero glass styling, run under a restricted widget API, and require secondary compilation steps.

### Selected Decision: Native C# Experience + Full ELF Kernel API Parity
We combine the strengths of both: all built-in apps and window interactions are natively implemented in C# for a flawless out-of-the-box Windows 7 Aero experience, while simultaneously exposing every single capability (window control, cursors, audio, dialogs, drawing) through the ELF Kernel API (v13) so developers can write full-featured C applications that look and feel identical.

---

## 4. Technical Specification & Builder Decisions

### 4.1 Window Management & 8-Direction Resizing (`Window.cs`, `WindowManager.cs`)
* **Resize Edge Hit-Testing**:
  ```csharp
  [Flags]
  public enum ResizeBorder { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }
  ```
  In `Window.HitTestResizeBorder(int mouseX, int mouseY)`:
  * Check against window outer boundary with a margin of 6px (`resizeMargin = 6`).
  * Corners: `Top | Left`, `Top | Right`, `Bottom | Left`, `Bottom | Right`.
  * Borders: `Left`, `Right`, `Top`, `Bottom`.
* **Resize Execution**:
  * Track active `ResizeBorder activeResizeBorder` and initial `Rectangle resizeStartBounds`.
  * Sizing maintains minimum dimensions (120x60px).
  * Display a translucent sizing preview rectangle `WindowManager.ShowPreviewRect(...)` while holding, and apply `Resize(newX, newY, newWidth, newHeight)` on release.

### 4.2 Dynamic Cursor Subsystem (`CursorManager.cs`, `Cursors.cs`, `DisplayManager.cs`)
* **Cursor Types**:
  ```csharp
  public enum CursorType
  {
      Arrow = 0,
      Hand = 1,
      IBeam = 2,
      SizeWE = 3,
      SizeNS = 4,
      SizeNWSE = 5,
      SizeNESW = 6,
      SizeAll = 7,
      Wait = 8
  }
  ```
* Standard procedural bitmaps generated with hotspot coordinates:
  * `SizeWE` hotspot (7, 4)
  * `SizeNS` hotspot (4, 7)
  * `SizeNWSE` hotspot (7, 7)
  * `SizeNESW` hotspot (7, 7)
  * `IBeam` hotspot (4, 7)
  * `Wait` hotspot (5, 7)
* `CursorManager.SetBusy(bool busy)` overrides hover updates during operations until released.
* `DisplayManager.Present(...)` clips and draws the active cursor at `(mx - hotspotX, my - hotspotY)`.

### 4.3 Aero Snap & Docking Previews (`Window.cs`, `WindowManager.cs`)
* **Drag-to-Edge Detection**:
  * Top edge (`mouseY <= 6`): preview full work area; release maximizes.
  * Left edge (`mouseX <= 6`): preview left half `new Rectangle(work.X, work.Y, work.Width / 2, work.Height)`.
  * Right edge (`mouseX >= Global.screenWidth - 6`): preview right half `new Rectangle(work.X + work.Width / 2, work.Y, work.Width - work.Width / 2, work.Height)`.
* **State & Restoration** *(Builder Decision)*:
  * Windows store `snapRestoreBounds` and `isSnapped` (mode: Normal = 0, Minimized = 1, Maximized = 2, SnappedLeft = 3, SnappedRight = 4).
  * Dragging the titlebar of a maximized or snapped window down immediately un-maximizes/un-snaps it, restoring its floating size centered horizontally under the cursor.

### 4.4 System Menus & Keyboard Shortcuts (`Window.cs`, `WindowManager.cs`, `HotkeyManager.cs`)
* **System Menu**:
  * Items: Restore, Move, Size, Minimize, Maximize, Close (`Alt+F4`).
  * Triggered by titlebar icon click, titlebar right click, taskbar button right click, or `Alt+Space`.
* **Keyboard Shortcuts & Hardware Modifiers** *(Builder Decision)*:
  * To ensure 100% reliability across all virtual machines and hardware BIOSes where Cosmos keyboard driver lacks a dedicated `Win` modifier flag, we support:
    * `Ctrl+Esc` and `Key.LWin` / `Key.RWin`: toggle Start Menu.
    * `Alt+F4`: close focused window.
    * `Alt+Space`: open System Menu.
    * `Win+R` & `Alt+R`: Run Dialog.
    * `Win+E` & `Alt+E`: File Explorer.
    * `Win+D` & `Ctrl+Alt+D`: Show Desktop.
    * `Win+Arrows` & `Alt+Arrows`: Aero Snap / Maximize / Restore.

### 4.5 Taskbar & Start Menu Authenticity (`Taskbar.cs`, `StartMenu.cs`)
* **Taskbar Buttons**:
  * Clicking an active focused window's button minimizes it.
  * Clicking an unfocused or minimized window button restores and focuses it.
  * Right-clicking opens the window's System Menu.
* **Windows 7 Start Menu**:
  * Two-column design:
    * Left pane: Search field at bottom, application list.
    * Right pane (dark slate styling): User Profile, Documents, Pictures, Music, Computer, Control Panel, Run..., Shut down button with flyout (Restart, Lock, Log Off).

### 4.6 Built-in Windows 7 Applets
1. **Run Dialog (`Programs/RunDialog.cs`)**:
   * Size: 420x190px, fixed size, Aero frame.
   * Text: "Type the name of a program, folder, document, or Internet resource, and Windose will open it for you."
   * OK, Cancel, Browse... buttons.
   * Registered to `run` command, `Win+R`, and Start Menu.
2. **About Windows (`Programs/AboutWindows.cs` / `winver`)**:
   * Size: 440x360px, fixed size, centered.
   * Windows 7 banner, OS build, available RAM, CPU model, registered username.
   * Registered to `winver` command.
3. **Calculator (`Programs/Calculator.cs` / `calc`)**:
   * Standard Windows 7 calculator (260x320px).
   * Arithmetic (+, -, *, /), %, sqrt, 1/x, +/-, backspace, CE, C, memory functions.
   * Keyboard support.
   * Registered to `calc` command and Start Menu.
4. **MS Paint (`Programs/Paint.cs` / `mspaint`)**:
   * Windows 7 Paint layout (720x520px).
   * Tools: Pencil, Brush, Eraser, Fill Bucket, Line, Rectangle, Ellipse.
   * Color palette swatch (16 colors) and stroke widths.
   * Registered to `mspaint` and `paint` commands.

### 4.7 Audio Feedback Subsystem (`System/Media/AudioEngine.cs`)
* **Sound Generation & Concurrency** *(Builder Decision)*:
  * Synthesize authentic 44.1 kHz 16-bit WAV waveforms in memory without disk assets:
    * `PlayStartup()`: Harmonic major chord.
    * `PlayClick()`: 15ms crisp click.
    * `PlayError()`: Chord exclamation beep.
    * `PlayTrash()`: Noise swoosh.
  * When `AudioEngine.IsPlaying` is true (background music playing in `MediaPlayer`), UI feedback sounds do not terminate the active media stream.

### 4.8 Expanded Kernel API v13 (`System/ELF/ElfRunner.cs`, `WindoseBridge.cs`, C Headers)
* **Version Bump**: `KAPI_VERSION 13`.
* **System Metrics Constants** *(Builder Decision)*:
  ```c
  #define SM_CXSCREEN     0  // Screen width in pixels
  #define SM_CYSCREEN     1  // Screen height in pixels
  #define SM_CYCAPTION    2  // Titlebar height in pixels
  #define SM_CXBORDER     3  // Window border size in pixels
  #define SM_CYTASKBAR    4  // Taskbar height in pixels
  #define SM_WORKWIDTH    5  // Usable desktop work area width
  #define SM_WORKHEIGHT   6  // Usable desktop work area height
  ```
* **Window State Constants** *(Builder Decision)*:
  ```c
  #define WINDOW_STATE_NORMAL       0
  #define WINDOW_STATE_MINIMIZED    1
  #define WINDOW_STATE_MAXIMIZED    2
  #define WINDOW_STATE_SNAPPED_LEFT 3
  #define WINDOW_STATE_SNAPPED_RIGHT 4
  ```
* **New Function Signatures**:
  * `int k_play_sound(int sound_id)` (0: Startup, 1: Click, 2: Error, 3: Trash)
  * `int k_play_wave(const void *wave_data, int len)`
  * `int k_set_cursor(int cursor_id)`
  * `int k_window_minimize(handle_t win)`
  * `int k_window_maximize(handle_t win)`
  * `int k_window_restore(handle_t win)`
  * `int k_window_get_state(handle_t win, int *out_state)`
  * `int k_shell_run_dialog()` (non-blocking)
  * `int k_shell_about_dialog()` (non-blocking)
  * `int k_get_system_metrics(int index)`

---

## 5. Implementation Steps & Verification Plan

1. **Fix Existing Build Defects**: Resolve `AviDecoder.cs` cast errors and verify clean `dotnet build`.
2. **Cursor Subsystem**: Implement `CursorManager`, procedural bitmaps, and integrate with `DisplayManager`.
3. **8-Direction Window Resizing & Aero Snap**: Implement 8-direction hit testing, docking preview rectangles, and drag-down restore in `Window.cs` / `WindowManager.cs`.
4. **Window System Menu & Hotkeys**: Add System Menu popup, wire `Alt+F4`, `Alt+Space`, `Ctrl+Esc`, `Win+R`, `Win+E`, `Win+D`, and `Win+Arrows`.
5. **Taskbar & Start Menu**: Implement taskbar button minimize toggle, right-click menu, and Windows 7 dual-column Start Menu.
6. **Built-in Applets**: Implement `RunDialog.cs`, `AboutWindows.cs`, `Calculator.cs`, and `Paint.cs`.
7. **Audio Feedback**: Add dynamic sound generators to `AudioEngine.cs` and wire shell event hooks.
8. **ELF Kernel API v13**: Implement delegates in `ElfRunner.cs`, `WindoseBridge.cs`, and update C headers.
9. **Verification**: Compile with `dotnet build` and ensure zero errors and zero warnings.
