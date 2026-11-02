# Windows Desktop Authenticity & Shortcut System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement human-readable `.lnk` shortcuts, visual arrow badging, protected Windows system icons, desktop marquee drag selection, and context menu enhancements for Windose.

**Architecture:** A text-based INI shortcut format parsed by `ShortcutFile.cs` integrated into `FileAssociations.Launch` and `Desktop.OpenIcon`. `DesktopIcon.cs` renders a classic curved arrow overlay badge on `.lnk` files and supports Small/Medium/Large sizing presets. `Desktop.cs` handles rubber-band drag selection, initializes pinned system icons ("This PC", "User Files", "Recycle Bin"), and provides working context menu actions.

**Tech Stack:** C# 12 / .NET 10 (NativeAOT), Cosmos Kernel GUI, `Cosmos.Kernel.System.Graphics`.

**Spec:** `docs/superpowers/specs/2026-11-02-windows-desktop-shortcuts-design.md`

## Global Constraints
- Never run builds (`dotnet build`, `csc`, `build.bat`) in this workspace. Validate edits with file-level diagnostics or non-build checks.
- Maintain documentation integrity and preserve existing comments.
- Only PCM `.wav` audio can be played on Cosmos.

## Review Focus
1. Malformed or corrupted `.lnk` file: `ShortcutFile.TryRead` safely returns `false` without throwing uncaught exceptions.
2. Missing target binary for a shortcut: `FileAssociations.Launch` warns and returns `false` gracefully without crashing the shell.
3. Target ELF binary with custom arguments: arguments specified in `.lnk` are split and forwarded to `ElfRunner.Spawn`.
4. Marquee selection extending off-screen: coordinates are clamped to desktop work area (`Global.screenWidth` by `Global.screenHeight - TaskbarHeight`).
5. Attempted deletion or rename of system icons: guarded by `isSystemIcon` check to prevent accidental loss of "This PC" or "Recycle Bin".

---

### Task 1: Shortcut File Specification & Parser (`ShortcutFile.cs`)

**Files:**
- Create: `System/Kernel/Subsystem/ShortcutFile.cs`

**Interfaces:**
- Produces:
  - `struct ShortcutInfo { public string Target; public string Arguments; public string WorkingDir; public string Name; public string? Icon; }`
  - `static bool ShortcutFile.TryRead(string path, out ShortcutInfo info)`
  - `static bool ShortcutFile.Create(string path, string target, string name, string? icon = null, string? args = null, string? workingDir = null)`

- [x] **Step 1: Write `System/Kernel/Subsystem/ShortcutFile.cs`**
Implement `ShortcutFile` with robust key-value parsing for `[Shortcut]` sections (`Target`, `Arguments`, `WorkingDir`, `Name`, `Icon`). Ignore comments (`#`, `;`) and empty lines. Validate target presence.
- [x] **Step 2: Non-build validation**
Verify file structure and syntax balance with `python scratch/check_syntax.py`.

---

### Task 2: Shortcut Resolution in `FileAssociations.cs`

**Files:**
- Modify: `System/Kernel/Subsystem/FileAssociations.cs`

**Interfaces:**
- Consumes: `ShortcutFile.TryRead(string path, out ShortcutInfo info)`
- Produces: Automatic redirection of `.lnk` files in `FileAssociations.Launch(path)`

- [x] **Step 1: Update `FileAssociations.Launch`**
If `string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase)`:
Invoke `ShortcutFile.TryRead(path, out ShortcutInfo info)`. If valid, resolve target and invoke `Launch(info.Target)` or if it is an `.elf` file, spawn it with `info.Arguments` and `info.WorkingDir`.
- [x] **Step 2: Update `FileAssociations.GetIconPath`**
If `ext == ".lnk"`, attempt to read `ShortcutInfo.Icon`. If empty, inspect target file extension and return its icon.
- [x] **Step 3: Non-build validation**
Verify balanced braces and references in `FileAssociations.cs`.

---

### Task 3: Shortcut Icon Badging & Sizing in `DesktopIcon.cs`

**Files:**
- Modify: `System/Shell/DesktopIcon.cs`

**Interfaces:**
- Consumes: `ShortcutFile.TryRead`, `FileAssociations.GetIcon`
- Produces:
  - `public bool isShortcut { get; }`
  - `public static int CurrentIconSize { get; set; } = 32;`
  - Visual shortcut arrow badge rendered in lower-left corner

- [x] **Step 1: Detect `.lnk` files and load target metadata**
In `DesktopIcon` constructor: check if `Path.GetExtension(fileEntry.FileName ?? "") == ".lnk"`. If so, set `isShortcut = true`, read `ShortcutInfo`, and set `icon` to the target's icon.
- [x] **Step 2: Implement curved arrow badge overlay in `DrawLocal()`**
Draw a 10x10 white arrow badge with a black outline in the lower-left corner of the icon bounding box if `isShortcut` is true.
- [x] **Step 3: Add icon size scaling support**
Allow icon size to adapt to Small (16px), Medium (32px), and Large (48px) view modes.
- [x] **Step 4: Non-build validation**
Run syntax check to verify `DesktopIcon.cs`.

---

### Task 4: Rubber-Band Drag Selection & Desktop Context Menu in `Desktop.cs`

**Files:**
- Modify: `System/Shell/Desktop.cs`

**Interfaces:**
- Produces:
  - Translucent marquee drag selection box on empty wallpaper space
  - Working "View -> Small / Medium / Large Icons"
  - Working "Personalise" opening `DisplaySettings`
  - Working "New -> Shortcut"

- [x] **Step 1: Add marquee selection tracking variables**
Add `bool isMarqueeDragging`, `Point marqueeStart`, `Point marqueeCurrent` in `Desktop.cs`.
- [x] **Step 2: Handle mouse events for marquee selection**
In `HandleInput`:
When left button is pressed on wallpaper (not clicking an icon), begin marquee dragging.
When mouse moves during marquee dragging, compute bounding rectangle, update `marqueeCurrent`, and select all intersecting icons in `selectedIcons`.
On mouse release, end marquee dragging.
- [x] **Step 3: Render marquee selection in `DrawLocal()`**
In `Desktop.DrawLocal()`: if `isMarqueeDragging`, fill translucent selection rectangle (`Color.FromArgb(45, 51, 153, 255)`) with a 1px border (`Color.FromArgb(180, 51, 153, 255)`).
- [x] **Step 4: Connect context menu actions**
Hook up:
- "View -> Large Icons" -> sets size 48 and adjusts grid
- "View -> Medium Icons" -> sets size 32 and adjusts grid
- "View -> Small Icons" -> sets size 16 and adjusts grid
- "Personalise" -> `LaunchTracker.Start(() => new DisplaySettings(contextX, contextY))`
- "New -> Shortcut" -> creates a default `.lnk` or shortcut creation dialog
- [x] **Step 5: Non-build validation**
Run syntax check to verify `Desktop.cs`.

---

### Task 5: Protected System Icons & Default Desktop Shortcuts Provisioning in `Desktop.cs`

**Files:**
- Modify: `System/Shell/Desktop.cs`

**Interfaces:**
- Produces:
  - Pinned "This PC" system icon
  - Pinned "User Files" system icon
  - Protected against deletion (`isSystemIcon = true`)
  - Default `.lnk` shortcuts created in `/mnt/user/desktop/`

- [x] **Step 1: Implement `EnsureSystemIcons()`**
Ensure "This PC" (`computermona.png`, opens File Explorer at `/mnt`), "User Files" (`directory_open_file_mydocs.png`, opens `/mnt/user` or `/mnt/Documents`), and "Recycle Bin" (`trash_empty.png`, opens Trash) are pinned on the desktop.
- [x] **Step 2: Implement `EnsureDefaultShortcuts()`**
On first boot, if desktop folder `/mnt/user/desktop/` is empty or missing shortcuts, create:
- `Command Prompt.lnk` -> `Target=/mnt/Programs/terminal.elf`, `Name=Command Prompt`, `Icon=/mnt/System/Icons/console_prompt.png`
- `Breeze Editor.lnk` -> `Target=/mnt/Programs/write_wordpad.png`, `Name=Breeze Editor`
- `Media Player.lnk` -> `Target=/mnt/System/Icons/media_player.png`, `Name=Windows Media Player`
- [x] **Step 3: Update `OpenIcon()`**
Handle system icons opening:
If icon is "This PC", launch File Explorer at `/mnt`.
If icon is "User Files", launch File Explorer at `/mnt/Documents`.
If icon is a `.lnk`, route through `FileAssociations.Launch`.
- [x] **Step 4: Non-build validation**
Run syntax check on all modified files.

---

### Task 6: Full System Verification & Sanity Checks

**Files:**
- Review: `System/Kernel/Subsystem/ShortcutFile.cs`, `System/Kernel/Subsystem/FileAssociations.cs`, `System/Shell/DesktopIcon.cs`, `System/Shell/Desktop.cs`

- [x] **Step 1: Run complete non-build AST / syntax validation**
Execute `python scratch/check_syntax.py` across all touched files.
- [x] **Step 2: Verify git status and diff integrity**
Verify no unintended files were touched and all changes match the plan.
