# Implementation Plan: Comprehensive ELF Application Suite & Kernel API Expansion

**Date:** 2026-11-08  
**Design Spec:** [docs/superpowers/specs/2026-11-08-elf-full-suite-enhancements-design.md](file:///c:/Users/salam/Desktop/windose/docs/superpowers/specs/2026-11-08-elf-full-suite-enhancements-design.md)  

---

### Task 1: KernelApi v15 Extension & Icon Resolution Pipeline
- **Step 1:** In `System/ELF/programs/include/windose/core.h`, bump `KAPI_VERSION` to 15.
- **Step 2:** Add syscall prototypes to `core.h`:
  - `int (KABI *list_set_item_icon)(int pid, handle_t obj, int item, const char *icon_path);`
  - `int (KABI *sys_mem)(int pid, uint64_t *out_total, uint64_t *out_used, uint64_t *out_free);`
  - `int (KABI *reg_delete)(int pid, const char *key);`
- **Step 3:** In `System/ELF/programs/include/kernel.h`, add inline wrapper functions:
  - `k_list_set_item_icon(handle_t obj, int item, const char *icon_path)`
  - `k_sys_mem(uint64_t *out_total, uint64_t *out_used, uint64_t *out_free)`
  - `k_reg_delete(const char *key)`
- **Step 4:** In `System/ELF/ElfRunner.cs`:
  - Bump `ApiVersion` to 15.
  - Add delegates and wire `api.ListSetItemIcon`, `api.SysMem`, `api.RegDelete`.
- **Step 5:** In `System/ELF/IElfHost.cs`, declare:
  - `bool ListSetItemIcon(object obj, int item, string iconPath);`
  - `bool SysMem(out ulong total, out ulong used, out ulong free);`
  - `bool RegDelete(string key);`
- **Step 6:** In `System/ELF/WindoseBridge.cs`:
  - Implement `ListSetItemIcon`: sets `box.List.items[item].Icon` via `FileAssociations.GetIcon(iconPath)` or `IconResource.Load`.
  - Update `ListAddRow` and `ListAddItem`: automatically resolve file icons via `FileAssociations.GetIcon` for items based on extension/folder status.
  - Fix `AddTreeNode`: when `parentItem == null` (root node), instantiate root with icon `new TreeViewItem(text, icon, null)`.
  - Implement `SysMem`: computes RAM via `CPU.GetAmountOfRAM() * 1024 * 1024`, heap via `GC.GetTotalMemory(false)`.
  - Implement `RegDelete`: calls `Registry.DeleteKey(key)` and `Registry.DeleteRuntimeValue(key)`.
- **Step 7:** Compile libc and test via `cmd.exe /c "cd System\ELF\programs && build.bat hello"`.
- **Step 8:** Run `cosmos build` to verify host compilation.
- **Step 9:** Commit: `git add System/ELF/programs/include/ System/ELF/ElfRunner.cs System/ELF/IElfHost.cs System/ELF/WindoseBridge.cs; git commit -m "Expand KernelApi to v15 with list icons, sys mem, and reg delete"`.

---

### Task 2: File Explorer Overhaul (`explorer.c`)
- **Step 1:** In `System/ELF/programs/src/explorer.c`, verify `cells` formatted for `k_list_add_row` pass proper folder indicator in cell 2 ("File Folder").
- **Step 2:** Expand `file_type()` in `explorer.c` with accurate descriptions:
  - `.elf` -> "Application", `.iso` -> "Disk Image", `.wav` -> "Wave Audio", `.breeze` -> "Breeze Script", `.json` -> "JSON Document", `.cs` -> "C# Source File", `.c` -> "C Source File", `.h` -> "Header File".
- **Step 3:** Ensure root drive nodes and subdirectory nodes in `rebuild_tree()` and `tree_load_children()` use `/mnt/System/Icons/hard_disk_drive.png` and `/mnt/System/Icons/directory_closed.png`.
- **Step 4:** Recompile via `cmd.exe /c "cd System\ELF\programs && build.bat explorer"`.
- **Step 5:** Run `cosmos build` to verify image creation.
- **Step 6:** Commit: `git add System/ELF/programs/src/explorer.c; git commit -m "Overhaul explorer with rich icons and file type descriptions"`.

---

### Task 3: Task Manager & Registry Editor Overhauls (`taskmgr.c` & `regedit.c`)
- **Step 1:** In `System/ELF/programs/src/taskmgr.c`:
  - Query `k_sys_mem` on refresh and format: `"Physical RAM: %lu MB | In Use: %lu MB (%lu%% free)"`.
  - Query `k_ticks()` to display uptime in status bar.
  - Add "New Task..." button calling `k_shell_run_dialog()`.
  - Protect PID 1 with confirmation alert.
- **Step 2:** In `System/ELF/programs/src/regedit.c`:
  - Add "Delete Key" button calling `k_reg_delete(key)` on the selected key and refreshing.
  - Add "New Key" button displaying prompt or populating key field with default `System/NewKey`.
  - Add Hive prefix filter input (e.g. `System/`, `Software/`, `User/`) updating the visible list.
- **Step 3:** Recompile both via `build.bat taskmgr` and `build.bat regedit`.
- **Step 4:** Run `cosmos build` to verify image creation.
- **Step 5:** Commit: `git add System/ELF/programs/src/taskmgr.c System/ELF/programs/src/regedit.c; git commit -m "Overhaul taskmgr with live RAM bar and regedit with key deletion"`.

---

### Task 4: Calculator & AeroEditor Overhauls (`calc.c` & `aeroeditor.c`)
- **Step 1:** In `System/ELF/programs/src/calc.c`:
  - Add negation `+/-` toggling operand sign.
  - Add percentage `%` (`v / 100.0`).
  - Add square root `sqrt` using Newton-Raphson approximation.
  - Add reciprocal `1/x` (`1.0 / v`).
  - Add memory storage variable `static double mem_val = 0;` and memory buttons `MC` (clear), `MR` (recall), `M+` (accumulate).
  - Expand keypad grid to 24 buttons (6 rows x 4 cols or 5 rows x 5 cols) within fixed 320x420 window.
- **Step 2:** In `System/ELF/programs/src/aeroeditor.c`:
  - Create top `MenuBar`:
    - `File`: "New", "Open", "Save", "Save As", "Exit".
    - `Edit`: "Cut", "Copy", "Paste", "Clear All".
  - Add dynamic status bar panel displaying current buffer length, line count, and UTF-8 encoding.
  - Implement "Save As" prompting for target path.
- **Step 3:** Recompile both via `build.bat calc` and `build.bat aeroeditor`.
- **Step 4:** Run `cosmos build` to verify image creation.
- **Step 5:** Commit: `git add System/ELF/programs/src/calc.c System/ELF/programs/src/aeroeditor.c; git commit -m "Overhaul calc with extended math and aeroeditor with MenuBar and Save As"`.

---

### Task 5: Native Paint Application (`paint.c`) & Shell Integration
- **Step 1:** Create `System/ELF/programs/src/paint.c`:
  - Window 640x480 with title "Paint", icon `/mnt/System/Icons/paint.png`.
  - Canvas 620x370.
  - Toolbar buttons: Color swatches (Black, White, Red, Green, Blue, Yellow, Cyan, Magenta, Gray, Orange).
  - Tool selectors: Brush, Eraser, Clear Canvas.
  - Brush sizes: Small (2px), Medium (5px), Large (10px).
  - Track `EV_MOUSEDOWN`, `EV_MOUSEMOVE`, `EV_MOUSEUP` for continuous drawing into pixel buffer, blitted with `k_draw_pixels`.
- **Step 2:** Compile via `build.bat paint`.
- **Step 3:** Register `bin\paint.elf` in `Windose.csproj` as `<EmbeddedResource>`.
- **Step 4:** Deploy `paint.elf` in `ResourceLoader.cs` (`SysRequired()`) to `/mnt/Programs/paint.elf`.
- **Step 5:** Update shell launchers:
  - `StartMenu.cs`: Pinned item "Paint", Accessories "Paint", and search query `paint`/`mspaint` prioritize `/mnt/Programs/paint.elf`.
  - `RunDialog.cs`: Alias `paint`/`mspaint` launches `/mnt/Programs/paint.elf`.
  - `Desktop.cs`: Ensure "Paint.lnk" shortcut exists pointing to `/mnt/Programs/paint.elf`.
  - `WindoseBridge.cs`: LaunchApp alias `paint`/`mspaint` launches `/mnt/Programs/paint.elf`.
- **Step 6:** Run `cosmos build` to verify image creation.
- **Step 7:** Commit: `git add System/ELF/programs/src/paint.c Windose.csproj System/Kernel/ResourceLoader.cs System/Shell/StartMenu.cs Programs/RunDialog.cs System/Shell/Desktop.cs System/ELF/WindoseBridge.cs; git commit -m "Implement native paint ELF app and route paint across shell"`.

---

### Task 6: End-to-End Build & Final Verification
- **Step 1:** Compile all 29 ELF binaries with `cmd.exe /c "cd System\ELF\programs && build.bat"`.
- **Step 2:** Run `cosmos build` to verify final kernel ISO.
- **Step 3:** Verify git cleanliness and update tracking artifacts.
