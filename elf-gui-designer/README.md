# ⚡ ELF Studio — Visual GUI Designer for ELF Programs on Windows

**ELF Studio** is a modern visual GUI designer and Rapid Application Development (RAD) tool built natively for Windows, specifically engineered to design, simulate, generate code, and build executables for **ELF (Executable and Linkable Format)** programs targeting Linux, embedded systems, and custom OS kernels (such as Cosmos OS and Windose x86_64).

---

## 🌟 Key Features

### 1. 🎨 Interactive WYSIWYG Visual Canvas
* **Full Drag-and-Drop**: Smooth positioning of components with smart alignment guides.
* **8-Point Resize Handles**: Resize widgets with corner and edge handles with coordinate feedback.
* **Grid Snapping**: Customizable grid (4px, 8px, 16px, 32px) with toggleable snap.
* **Precision Nudging**: Use Arrow keys (`←`, `↑`, `→`, `↓`) to nudge selected widgets by 1px (or `Shift + Arrow` for grid-size nudges).
* **Multi-Selection & Alignment**: Select multiple widgets (`Shift+Click` or `Ctrl+Click` or `Ctrl+A`) and align them with one click:
  - Align Left, Center Horizontally, Align Right
  - Align Top, Center Vertically, Align Bottom
  - Distribute Horizontally and Vertically
* **Clipboard & Duplication**: `Ctrl+C` (Copy), `Ctrl+V` (Paste), `Ctrl+D` (Duplicate), `Delete` (Remove).
* **Viewport Presets**:
  - `800 x 480` (Classic Linux / Raspberry Pi 7" Touchscreen)
  - `1024 x 600` (Netbook & Embedded Linux)
  - `640 x 480` (Standard VGA / Framebuffer / Cosmos OS)
  - `480 x 320` (3.5" TFT SPI Display)
  - `320 x 240` (QVGA Micro-GUI)
  - `1280 x 720` (720p HD) & `1920 x 1080` (1080p FHD)
* **Full Undo / Redo**: Infinite undo history (`Ctrl+Z`, `Ctrl+Y`).
* **Pan & Zoom**: Smooth canvas zooming from 25% to 300% (`Ctrl+Wheel` or top-bar buttons).

### 2. 🧰 Rich 20-Component Palette (Categorized)
1. **Standard Controls**:
   - **Push Buttons**: Customizable colors, corner radius, and click event callbacks.
   - **Text Labels**: Font sizing, weights, alignments, and custom text colors.
   - **Text Input (Entry)**: Single-line input text boxes with placeholder captions.
   - **Multiline Text (Textarea)**: Code and multiline text editor with line numbers and scrollbar.
   - **Checkboxes**: Boolean toggles with custom captions.
   - **Radio Buttons**: Mutual exclusion groups for multi-choice selection.
   - **Toggle Switches**: Power state switches with animated knobs.
2. **Numerical & Ranges**:
   - **Range Sliders**: Sliders with live mouse dragging and value display.
   - **Progress Bars**: Status gauges with percentage indicators.
   - **Numeric Spinners**: Up/down chevron steppers with bounds and step increments.
3. **Lists & Navigation**:
   - **Dropdown Menus**: ComboBox option selectors.
   - **List Boxes**: Striped list item containers with row selection.
   - **Data Tables (Grids)**: Tabular datasets with column headers and cell rows.
   - **Tab Strips**: Multi-page tab bars with clickable page switching.
4. **Graphics & Containers**:
   - **Container Panels / Cards**: Group widgets with background fills, borders, and rounded corners.
   - **Graphic / Image Frames**: Image placeholders with icon graphics and captions.
   - **Drawing Canvases**: Raster canvas drawing regions.
   - **Color Pickers**: Swatch selector buttons with hex feedback.
   - **Divider Lines (Separators)**: Horizontal or vertical etched separators.
   - **Status Bars**: Screen footer status bars with segmented panels.

### 3. 🎮 Live Interactive Simulator
* Flip between **Design Mode** and **Live Simulation Mode** with one click.
* **Test in Real Time**:
  - Click buttons to trigger visual press animations and dispatch signals.
  - Drag sliders interactively and watch values update.
  - Toggle checkboxes, radio buttons (mutually exclusive in groups), and power switches.
  - Click numeric spinner up/down chevrons to increment/decrement.
  - Switch tab pages and select list box items.
  - Type directly into single-line entry fields and multiline textareas.
  - Cycle color swatches on color pickers.
  - Stream all UI events to the live status console at the bottom.

### 4. ⚡ Multi-Target Code Generation
ELF Studio instantly generates clean, idiomatic code for all major GUI frameworks:
1. **Native Windose ELF C (`kernel.h`)**: Native C code utilizing Windose Kernel API v10 (`k_create_window`, `k_create_button`, `k_create_textfield`, `k_textfield_set_multiline`, `k_create_radio`, `k_create_tabs`, `k_grid_create`, etc.).
2. **Cosmos OS C# (`Screen.cs`)**: Canvas drawing code for Cosmos OS x86 ELF kernels.
3. **LVGL C99 (`gui_screen.c`)**: The premier embedded and Linux GUI library for ELF binaries.
4. **Raylib + Raygui C99 (`main.c`)**: High-performance immediate-mode GUI for native Linux desktop ELF binaries.
5. **Standalone Framebuffer C (`fb_gui.c`)**: Freestanding, zero-dependency C program with built-in font rendering that writes directly to `/dev/fb0` or raw memory buffers! Can be compiled directly to an ELF binary on Windows!
6. **GTK 3 C (`main.c`)**: Canonical desktop Linux ELF binary utilizing GTK application and widgets.
7. **UI JSON Schema (`layout.json`)**: Machine-readable specification for project serialization and custom tooling.

### 5. 🛡️ Built-in ELF Compiler & Binary Inspector
* **Native Windose OS Integration (`build.bat`)**:
  - Automatically hooks into `System/ELF/programs/build.bat`.
  - Emits native `kernel.h` C code into `System/ELF/programs/src/<name>.c`.
  - Triggers `build.bat <name>` to compile with Clang and link with Windose's custom libc/`crt0.o`.
  - Produces PIE ELF64 binaries in `System/ELF/programs/bin/<name>.elf`.
* **1-Click Kernel Installation**:
  - Click **"📥 Install to Windose Kernel"** to automatically embed your compiled `.elf` into `Kernel.cs` and `Windose.csproj`, making it immediately accessible at `/mnt/Programs/<name>.elf` upon booting Windose!
* **1-Click Standalone ELF Builder**:
  - Automatically locates **Clang** and **LLD** on your Windows system (including Cosmos OS tools and standard PATH).
  - Cross-compiles standalone 64-bit ELF executables (`.elf`) or relocatable ELF objects (`.o`) directly on Windows!
* **ELF Binary Structure Inspector**:
  - Inspect any ELF binary on disk.
  - Decodes:
    - ELF Magic (`7F 45 4C 46`), 32/64-bit Class, Endianness, OS ABI.
    - Entry Point Virtual Address (e.g. `0x00000000000014a0`).
    - Section Headers table (`.text`, `.rodata`, `.data`, `.bss`, etc.) with sizes, file offsets, and execution flags (`AX`, `WA`).
    - Symbol table parser with names and address values.

### 6. 🚀 Built-in ELF Project Templates
* **Linux System Monitor / Dashboard (800x500)**: CPU / RAM utilization gauges, process controls, turbo boost, and kill signal actions.
* **Text Editor & IDE (800x500)**: File toolbar, multiline editor buffer, file paths, and footer status bar.
* **Database & Table Browser (800x500)**: DataGrid table view, search input, filter dropdowns, and export button.
* **System Preferences Dialog (680x460)**: Tab strip, mutual exclusion radio groups, Aero checkboxes, numeric spinners, and scaling slider.
* **Embedded IoT Touchscreen (480x320)**: Climate node with temperature/humidity displays, HVAC toggles, and target slider.
* **OS Login & Bootloader (640x480)**: System authentication dialog for Cosmos OS and bare-metal ELF kernels.

---

## 🏃 How to Run on Windows

### Method 1: 1-Click Batch File
Double-click `launch.bat` in the project directory:
```
c:\Users\salam\Desktop\windose\elf-gui-designer\launch.bat
```

### Method 2: Command Line (PowerShell / Command Prompt)
```powershell
cd c:\Users\salam\Desktop\windose\elf-gui-designer
python run.py
```

---

## ⌨️ Keyboard Shortcuts

| Shortcut | Action |
| :--- | :--- |
| `Ctrl + S` | Save Project (`.elfgui` / `.json`) |
| `Ctrl + O` | Open Existing Project |
| `Ctrl + N` | Create New Project |
| `Ctrl + Z` | Undo Canvas Modification |
| `Ctrl + Y` | Redo Canvas Modification |
| `Ctrl + A` | Select All Widgets |
| `Ctrl + C` | Copy Selected Widgets |
| `Ctrl + V` | Paste Copied Widgets |
| `Ctrl + D` | Duplicate Selected Widget |
| `Delete` / `Backspace` | Delete Selected Widgets |
| `Arrow Keys` | Precision Nudge Widgets (1px) |
| `Shift + Arrow Keys` | Snap Nudge Widgets (Grid Step) |
| `Ctrl + Mouse Wheel` | Canvas Zoom In / Out |
