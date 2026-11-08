# Design Spec: Comprehensive ELF Application Suite & Kernel API Expansion

**Author:** DeepMind Agentic Coding Team  
**Date:** 2026-11-08  
**Status:** In Review  

---

## 1. Overview
The goal of this initiative is to elevate all Windose native ELF applications to fully functional, visually polished system tools. It addresses missing features across all core ELF utilities, completes the list and tree icon pipeline in the ELF GUI bridge, adds critical system syscalls to `KernelApi` (v15), and introduces a full-featured native `paint.c` application to replace legacy C# managed `Paint.cs`.

---

## 2. Kernel API & Subsystem Extensions (`KernelApi` v15)

### 2.1 Syscall Table Additions
The `KernelApi` struct in `core.h` and `ElfRunner.cs` will be incremented from `KAPI_VERSION 14` to `15`, adding:

1. **`list_set_item_icon(int pid, handle_t obj, int item_idx, const char *icon_path)`**
   - Sets the 16x16 icon for a specific row in an `ElfListBox` (`ListViewItem.Icon`).
   - Resolves via `IconResource.Load(path, 16)` or `FileAssociations.GetIcon(path)`.

2. **`sys_mem(int pid, uint64_t *out_total, uint64_t *out_used, uint64_t *out_free)`**
   - Computes physical system RAM using `Cosmos.Core.CPU.GetAmountOfRAM() * 1024 * 1024`.
   - Computes managed heap usage via `GC.GetTotalMemory(false)`.
   - Computes free bytes as `total - used`.
   - Populates 64-bit output pointers.

3. **`reg_delete(int pid, const char *key)`**
   - Calls `Registry.DeleteKey(key)` and `Registry.DeleteRuntimeValue(key)`.
   - Returns `1` on success, `0` if key was not found or could not be removed.

### 2.2 Automatic Icon Resolution in `WindoseBridge`
- **`ListAddRow(object obj, string[] cells)`:**
  - If `cells.Length > 0 && !string.IsNullOrEmpty(cells[0])`:
    - If `cells.Length >= 3 && cells[2] == "File Folder"`: resolves `FileAssociations.GetIcon("folder")`.
    - Else: resolves `FileAssociations.GetIcon(cells[0])` based on file extension.
  - Passes the loaded `Image` to `box.List.AddItem(cells, icon)`.
- **`ListAddItem(object obj, string text)`:**
  - Auto-resolves `FileAssociations.GetIcon(text)` if it contains an extension or matches a known type.
- **`AddTreeNode(TreeView parent, TreeViewItem parentItem, string text, string icon)`:**
  - Fix root node bug: When `parentItem == null` and `!string.IsNullOrEmpty(icon)`, instantiate root item with icon using `new TreeViewItem(text, icon, null)` instead of dropping it.

---

## 3. ELF Application Overhauls

### 3.1 `explorer.c` (File Explorer)
- **File Icons:** Automatically display 16x16 shell icons for every file, directory, and drive in the details list (`files`) and navigation tree (`tree`).
- **File Types:** Expand type mapping for modern formats (`.elf` -> "ELF Executable", `.iso` -> "Disk Image", `.wav` -> "Wave Audio", `.breeze` -> "Breeze Script", etc.).
- **Toolbar & Status:** Add icons or clean visual indicators, responsive anchoring, and accurate selection sizes.

### 3.2 `taskmgr.c` (Task Manager)
- **Live Memory Meter:** Call `k_sys_mem` on refresh to display:
  - Total Physical RAM (e.g. `1024 MB`)
  - Used Memory (e.g. `142 MB`)
  - Free Memory percentage
- **Active Process Count & Uptime:** Display total active processes and elapsed system uptime.
- **New Task Action:** Add a "New Task..." button that invokes `k_shell_run_dialog()`, allowing users to spawn any program directly from Task Manager.
- **Protected Processes:** Disable or warn when attempting to terminate PID 1 (Kernel/DWM).

### 3.3 `regedit.c` (Registry Editor)
- **New Key:** Add "New Key" button prompting for key name and default value, creating it via `k_reg_set`.
- **Delete Key:** Add "Delete Key" button calling `k_reg_delete(key)` to remove the selected key from the system registry.
- **Key Prefix Filter:** Add a filter textfield to quickly search or filter registry keys by hive/prefix (e.g. `System/`, `Software/`, `User/`).

### 3.4 `calc.c` (Calculator)
- **Extended Math Operations:**
  - Negation (`+/-`): flips the sign of the current operand or result.
  - Percentage / Modulo (`%`): computes `v / 100` or remainder.
  - Square Root (`sqrt`): computes `sqrt(v)` using Newton-Raphson approximation.
  - Reciprocal (`1/x`): computes `1.0 / v`.
- **Memory Functions:**
  - `MC`: Memory Clear
  - `MR`: Memory Recall
  - `M+`: Add current value to memory
- **Layout:** Re-arranged keypad grid keeping the fixed window size (320x420).

### 3.5 `aeroeditor.c` (Aero Text Editor)
- **Top MenuBar:**
  - `File`: New, Open, Save, Save As, Exit.
  - `Edit`: Cut, Copy, Paste, Clear.
- **Status Bar:** Dynamic live panel displaying line count, column position, total characters, and encoding (`UTF-8`).
- **Save As:** Prompts for target path and writes current buffer.

### 3.6 `paint.c` (New Native ELF Paint Application)
- **Window:** 640x480, title "Paint", with icon `/mnt/System/Icons/paint.png`.
- **Canvas:** 620x360 pixel buffer canvas (`k_create_canvas` / `k_draw_pixels`).
- **Toolbar Controls:**
  - Palette buttons: Black, White, Red, Green, Blue, Yellow, Cyan, Magenta, Gray, Orange.
  - Tool buttons: Brush, Eraser, Clear Canvas.
  - Brush sizes: Small (2px), Medium (5px), Large (10px).
- **Interactive Drawing:** Tracks `EV_MOUSEDOWN`, `EV_MOUSEMOVE`, and `EV_MOUSEUP` to draw continuous lines or strokes on the pixel buffer.
- **Shell Replacement:** Updates Start Menu, Desktop shortcuts, and Run dialog to point `paint`/`mspaint` to `/mnt/Programs/paint.elf`.

---

## 4. Verification & Testing
1. **ELF Subsystem Compilation:** Build all libc and program objects via `build.bat` with zero errors.
2. **Kernel Build:** Build `output-x64/Windose.iso` via `cosmos build` with zero errors.
3. **Functional Verification:**
   - Verify `explorer.elf` displays file and drive icons in tree and list.
   - Verify `taskmgr.elf` displays system RAM and launches the run dialog.
   - Verify `regedit.elf` creates and deletes keys.
   - Verify `calc.elf` computes extended math operations (`+/-`, `%`, `sqrt`).
   - Verify `aeroeditor.elf` MenuBar actions and Save As.
   - Verify `paint.elf` draws strokes on mouse drag and responds to palette picks.
