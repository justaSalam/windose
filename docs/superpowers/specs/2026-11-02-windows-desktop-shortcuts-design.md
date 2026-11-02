# Windows Desktop Authenticity & Shortcut System Design

**Date:** 2026-11-02  
**Status:** In Review  
**Topic:** Desktop Shell, Shortcuts (`.lnk`), System Icons, and Marquee Selection  

---

## 1. Intent & Context
The Windose desktop shell currently mirrors the filesystem directory `/mnt/user/desktop/` using direct `FileEntry` items. Because there is no shortcut abstraction, users must copy physical binaries (e.g. `/mnt/Programs/terminal.elf`) onto the desktop to launch them. Furthermore, the desktop lacks key Windows behaviors:
* No curved-arrow shortcut badge on icons.
* Context menu entries like "Large/Medium/Small Icons" and "Personalise" are stubs.
* No rubber-band marquee selection rectangle when dragging the mouse over empty wallpaper space.
* Missing standard Windows desktop anchors ("This PC" and "User Files").

The goal is to elevate the desktop shell to match authentic Windows conventions in feel, function, and visual feedback.

---

## 2. Goals, Non-Goals & Anti-Goals

### Goals
1. **Text-Based `.lnk` Format (`ShortcutFile.cs`)**:
   * A clean, INI-style plain text format storing `Target`, `Arguments`, `WorkingDir`, `Name`, and optional `Icon`.
   * Directly readable/creatable by CLI commands (`cat`, `echo`), text editors, and desktop wizards.
2. **Unified Shortcut Resolution**:
   * Executing a `.lnk` file through `Desktop.OpenIcon`, `FileExplorer.OpenFilePath`, or `FileAssociations.Launch` seamlessly parses the target and runs the corresponding ELF binary, system program, or document.
3. **Shortcut Icon Badging**:
   * [`DesktopIcon.cs`](file:///c:/Users/salam/Desktop/windose/System/Shell/DesktopIcon.cs) recognizes `.lnk` files and draws the classic curved-arrow overlay badge in the lower-left corner of the icon.
4. **Standard Desktop System Icons**:
   * Pinned, undeletable system icons (`isSystemIcon = true`): "This PC" (opens File Explorer at root `/mnt`), "User Files" (opens `/mnt/user` or `/mnt/Documents`), and "Recycle Bin".
5. **Rubber-Band Drag Selection**:
   * Clicking and dragging on open wallpaper draws a translucent blue rectangular selection box, selecting all icons within its bounds.
6. **Desktop Context Menu Actions**:
   * Working icon sizing modes: Small (16px icon), Medium (32px icon), Large (48px icon).
   * Working "Personalise" item opening Display Settings.
   * Working "New -> Shortcut" option creating a prompt or default shortcut.
7. **Default Desktop Shortcuts**:
   * Fresh desktops generate `.lnk` shortcuts for Terminal (`terminal.elf` / `CommandPrompt`), Breeze Editor, and Media Player.

### Non-Goals
* Implementing Microsoft's binary `MS-SHLLINK` file format (unnecessary complexity for Cosmos).
* OLE/COM drag-and-drop shell extensions.

### Anti-Goals
* Hardcoding shortcuts in memory without disk persistence in `/mnt/user/desktop/`.
* Breaking direct double-click launching of raw documents or folders on the desktop.

---

## 3. Approaches Considered

### Approach 1: Text-Based `.lnk` Specification + Virtual System Icons (Selected)
* Shortcuts are text files in `/mnt/user/desktop/` with key-value pairs (`Target`, `Arguments`, `Icon`, `Name`).
* System icons are flagged with `isSystemIcon = true` to protect against deletion.
* Marquee selection is rendered directly in `Desktop.DrawLocal()` during active background mouse dragging.
* **Why Selected:** Matches authentic Windows desktop architecture, persists cleanly to disk, and requires zero binary parsing overhead.

### Approach 2: In-Memory / Database Desktop Hierarchy (`IDesktopItem`)
* Replaces `FileEntry` with an abstract item hierarchy (`FileSystemItem`, `ShortcutItem`, `VirtualFolderItem`) serialized to a central desktop database.
* **Why Rejected:** Violates the principle that `/mnt/user/desktop/` represents the desktop contents and complicates file system interactions.

---

## 4. Technical Specification

### 4.1 Shortcut File Architecture (`ShortcutFile.cs`)
Located at `System/Kernel/Subsystem/ShortcutFile.cs`:
```ini
[Shortcut]
Target=/mnt/Programs/terminal.elf
Arguments=
WorkingDir=/mnt
Name=Command Prompt
Icon=/mnt/System/Icons/console_prompt.png
```
* `ShortcutFile.Create(string path, string target, string name, string? icon = null, string? args = null, string? workingDir = null)`
* `ShortcutFile.TryRead(string path, out ShortcutInfo info)`
* `ShortcutInfo` contains `Target`, `Arguments`, `WorkingDir`, `Name`, `Icon`.

### 4.2 Desktop Icon Badging & Sizing (`DesktopIcon.cs`)
* In constructor: If `Path.GetExtension(fileEntry.FileName) == ".lnk"`, parse shortcut metadata. Use `info.Icon` if provided; otherwise query `FileAssociations.GetIcon(info.Target)`.
* In `DrawLocal()`:
  * Draw the base icon.
  * If the icon is a shortcut (`isShortcut == true`), overlay a 10x10 badge in the lower-left corner (white curved arrow with black border).
* Support dynamic icon sizing (`IconSize = 16 | 32 | 48`) with adjusted cell layouts.

### 4.3 Desktop Selection & System Icons (`Desktop.cs`)
* **System Icons**:
  * "This PC": opens `new FileExplorer(100, 100, 800, 500, "This PC", "/mnt")`. Icon: `/mnt/System/Icons/computermona.png`.
  * "User Files": opens `/mnt/user`. Icon: `/mnt/System/Icons/directory_open_file_mydocs.png`.
  * "Recycle Bin": opens Trash. Icon: `/mnt/System/Icons/trash_empty.png`.
* **Rubber-Band Selection**:
  * When `leftMousePress` occurs on wallpaper (no icon hit), set `isMarqueeDragging = true`, `marqueeStart = new Point(mouseX, mouseY)`.
  * On mouse move, update `marqueeCurrent`.
  * In `DrawLocal()`, draw filled translucent rectangle (`Color.FromArgb(40, 51, 153, 255)`) with a 1px solid border (`Color.FromArgb(180, 51, 153, 255)`).
  * Compute intersection with all `Icons` and update `selectedIcons`.
  * On mouse release, end marquee drag.
* **Context Menu Updates**:
  * "View -> Small / Medium / Large Icons": invokes `SetIconSizePreset(...)` updating icon bounds and grid spacing.
  * "Personalise": launches `DisplaySettings`.
  * "New -> Shortcut": creates a `.lnk` file pointing to `/mnt/Programs/terminal.elf` or prompts for path.

### 4.4 File Associations (`FileAssociations.cs`)
* In `Launch(string path)`: If `ext == ".lnk"`, invoke `ShortcutFile.TryRead(path, out var info)`. If successful, launch `info.Target` with `info.Arguments`.

---

## 5. Verification Plan
* Validate that `.lnk` files created on the desktop launch their respective target ELF programs or files.
* Verify the shortcut curved-arrow badge renders cleanly on `.lnk` icons.
* Test marquee drag selection selects multiple icons accurately without visual artifacts.
* Verify "This PC" and "User Files" appear and launch file explorer at appropriate paths.
* Verify context menu "View" icon size changes and "Personalise" opens Display Settings.
* Validate all C# files with non-build syntax checks.
