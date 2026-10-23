"""
Main Application Window for ELF Studio.
Integrates Canvas, Component Toolbox, Property Inspector, Code Generator,
Simulation Console, and ELF Binary Toolsuite.
"""

import os
import tkinter as tk
from tkinter import filedialog, messagebox
import customtkinter as ctk
from PIL import Image, ImageGrab

from core.models import Project, Widget, WidgetType
from core.canvas_view import CanvasView
from core.templates import TEMPLATES, create_windose_aero_explorer
from core.validator import ProjectValidator, DiagnosticSeverity
from ui.toolbox_panel import ToolboxPanel
from ui.property_panel import PropertyPanel
from ui.hierarchy_panel import HierarchyPanel
from ui.codegen_panel import CodeGenPanel
from ui.diagnostics_panel import DiagnosticsPanel
from ui.elf_panel import ElfPanel
from ui.console_panel import LiveConsolePanel


class MainWindow(ctk.CTk):
    """Windose Designer Visual Studio Application Window."""

    def __init__(self):
        super().__init__()

        # Window Configuration
        self.title("Windose Designer - Visual Studio for Windose & ELF OS")
        self.geometry("1400x880")
        self.minsize(1080, 680)

        # Apply Aero Dark Chrome
        ctk.set_appearance_mode("dark")
        ctk.set_default_color_theme("blue")
        self.configure(fg_color="#101824")

        # Initialize Default Project (Windose Aero Explorer Flagship Template)
        self.project = create_windose_aero_explorer()
        self.project_filepath = None
        self._code_dirty = True
        self._console_visible = False

        self._build_header_toolbar()
        self._build_main_content()
        self._build_status_bar()
        self._bind_shortcuts()

        # Initial view sync
        self._sync_all()

    def _build_header_toolbar(self) -> None:
        """Top application header bar with branding and primary actions styled in Aero Slate."""
        self.top_bar = ctk.CTkFrame(self, fg_color="#182535", height=50, corner_radius=0)
        self.top_bar.pack(fill="x", side="top")

        # Logo and Title
        brand_frame = ctk.CTkFrame(self.top_bar, fg_color="transparent")
        brand_frame.pack(side="left", padx=(16, 20), pady=6)

        ctk.CTkLabel(
            brand_frame, text="🪐 WINDOSE DESIGNER",
            font=ctk.CTkFont(family="Segoe UI", size=15, weight="bold"),
            text_color="#93c5fd"
        ).pack(side="left")

        ctk.CTkLabel(
            brand_frame, text="  Visual Studio for Windose 7 Aero & ELF",
            font=ctk.CTkFont(family="Segoe UI", size=10),
            text_color="#64748b"
        ).pack(side="left", pady=(4, 0))

        # File Operations
        file_frame = ctk.CTkFrame(self.top_bar, fg_color="transparent")
        file_frame.pack(side="left", padx=4)

        b_new = ctk.CTkButton(
            file_frame, text="📄 New", width=60, height=28, fg_color="#22364c", hover_color="#3464a8",
            font=ctk.CTkFont(size=11), command=self._action_new_project
        )
        b_new.pack(side="left", padx=2)

        b_tpl = ctk.CTkButton(
            file_frame, text="✨ Templates", width=84, height=28, fg_color="#22364c", hover_color="#3464a8",
            font=ctk.CTkFont(size=11), command=self._action_show_templates_modal
        )
        b_tpl.pack(side="left", padx=2)

        b_open = ctk.CTkButton(
            file_frame, text="📂 Open", width=65, height=28, fg_color="#22364c", hover_color="#3464a8",
            font=ctk.CTkFont(size=11), command=self._action_open_project
        )
        b_open.pack(side="left", padx=2)

        b_save = ctk.CTkButton(
            file_frame, text="💾 Save", width=65, height=28, fg_color="#22364c", hover_color="#3464a8",
            font=ctk.CTkFont(size=11), command=self._action_save_project
        )
        b_save.pack(side="left", padx=2)

        # Mode Selector: Visual Design vs Simulation
        mode_frame = ctk.CTkFrame(self.top_bar, fg_color="#131e2b", height=32, corner_radius=6)
        mode_frame.pack(side="left", padx=24)

        self.btn_design_mode = ctk.CTkButton(
            mode_frame, text="✏️ Design View", width=105, height=26, fg_color="#3464a8", hover_color="#2563eb",
            corner_radius=4, font=ctk.CTkFont(size=11, weight="bold"),
            command=lambda: self._set_mode("design")
        )
        self.btn_design_mode.pack(side="left", padx=2, pady=2)

        self.btn_sim_mode = ctk.CTkButton(
            mode_frame, text="🎮 Live Simulator", width=115, height=26, fg_color="transparent", hover_color="#1e2f42",
            corner_radius=4, font=ctk.CTkFont(size=11, weight="bold"),
            command=lambda: self._set_mode("sim")
        )
        self.btn_sim_mode.pack(side="left", padx=2, pady=2)

        # View Tabs (Canvas / Code / Diagnostics / ELF Suite)
        self.view_switcher = ctk.CTkSegmentedButton(
            self.top_bar,
            values=["Visual Canvas", "C/ELF Code", "Diagnostics & Linting", "ELF Binary Suite"],
            command=self._on_view_switched,
            selected_color="#3464a8",
            selected_hover_color="#2563eb",
            unselected_color="#22364c",
            font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            height=30
        )
        self.view_switcher.set("Visual Canvas")
        self.view_switcher.pack(side="left", padx=16)

        # Zoom & Undo Controls (Right Side)
        right_ctrls = ctk.CTkFrame(self.top_bar, fg_color="transparent")
        right_ctrls.pack(side="right", padx=16)

        ctk.CTkButton(
            right_ctrls, text="⟲", width=30, height=28, fg_color="#22364c", hover_color="#3464a8",
            command=self._action_undo
        ).pack(side="left", padx=2)

        ctk.CTkButton(
            right_ctrls, text="⟳", width=30, height=28, fg_color="#22364c", hover_color="#3464a8",
            command=self._action_redo
        ).pack(side="left", padx=2)

        ctk.CTkButton(
            right_ctrls, text="➖", width=30, height=28, fg_color="#22364c", hover_color="#3464a8",
            command=lambda: self.canvas.zoom_out()
        ).pack(side="left", padx=(10, 2))

        ctk.CTkButton(
            right_ctrls, text="100%", width=46, height=28, fg_color="#22364c", hover_color="#3464a8",
            font=ctk.CTkFont(size=10), command=lambda: self.canvas.zoom_reset()
        ).pack(side="left", padx=2)

        ctk.CTkButton(
            right_ctrls, text="➕", width=30, height=28, fg_color="#22364c", hover_color="#3464a8",
            command=lambda: self.canvas.zoom_in()
        ).pack(side="left", padx=2)

    def _build_main_content(self) -> None:
        """Main workspace containing Left Toolbox, Center Canvas/Code/ELF panels, and Right Inspector."""
        self.main_container = ctk.CTkFrame(self, fg_color="transparent", corner_radius=0)
        self.main_container.pack(fill="both", expand=True)

        # 1. Left Sidebar: Toolbox
        self.toolbox = ToolboxPanel(
            self.main_container,
            on_add_widget=self._on_add_widget,
            on_change_resolution=self._on_change_resolution,
            on_load_template=self._on_load_template,
            on_grid_settings_changed=self._on_grid_settings_changed,
            on_align_action=self._on_align_action
        )
        self.toolbox.pack(side="left", fill="y")

        # 2. Right Sidebar: Property Inspector & Hierarchy
        self.right_sidebar = ctk.CTkFrame(self.main_container, fg_color="#16202c", width=290, corner_radius=0)
        self.right_sidebar.pack(side="right", fill="y")
        self.right_sidebar.pack_propagate(False)

        # Split right sidebar into Property Panel (top) and Hierarchy Panel (bottom)
        self.property_panel = PropertyPanel(
            self.right_sidebar,
            on_property_changed=self._on_property_changed,
            on_action=self._on_inspector_action,
            on_jump_to_code=self._switch_to_code_and_jump
        )
        self.property_panel.pack(fill="both", expand=True)

        self.hierarchy_panel = HierarchyPanel(
            self.right_sidebar,
            self.project,
            on_widget_selected=self._on_widget_selected_from_tree,
            on_reorder_widget=self._on_reorder_widget,
            height=200
        )
        self.hierarchy_panel.pack(fill="x", side="bottom")

        # 3. Center Workspace: Displays either Canvas, CodeGenPanel, DiagnosticsPanel, or ElfPanel
        self.center_area = ctk.CTkFrame(self.main_container, fg_color="#3a6ea5", corner_radius=0)
        self.center_area.pack(side="left", fill="both", expand=True)

        # Visual Canvas
        self.canvas = CanvasView(
            self.center_area,
            self.project,
            on_widget_selected=self._on_canvas_selection_changed,
            on_widget_modified=self._on_canvas_widget_modified,
            on_sim_event=self._on_sim_event,
            on_widget_double_click=self._on_widget_double_clicked
        )
        self.canvas.pack(fill="both", expand=True)

        # Code Generator Panel (initially hidden)
        self.codegen_panel = CodeGenPanel(
            self.center_area,
            self.project,
            on_request_elf_build=self._switch_to_elf_suite
        )

        # Diagnostics & Linting Panel (initially hidden)
        self.diagnostics_panel = DiagnosticsPanel(
            self.center_area,
            self.project,
            on_select_widget=self._on_diagnostics_select_widget,
            on_project_modified=self._on_diagnostics_fix_applied
        )

        # ELF Binary Suite Panel (initially hidden)
        self.elf_panel = ElfPanel(
            self.center_area,
            self.project
        )

        # Live Runtime Console Panel (docked at bottom of center area, collapsible)
        self.console_panel = LiveConsolePanel(
            self.center_area,
            on_command=self._on_console_command,
            on_close=self._toggle_console
        )

    def _build_status_bar(self) -> None:
        """Bottom status bar with real-time feedback and simulation event logs."""
        self.status_bar = ctk.CTkFrame(self, fg_color="#182535", height=26, corner_radius=0)
        self.status_bar.pack(fill="x", side="bottom")

        self.lbl_status_left = ctk.CTkLabel(
            self.status_bar,
            text=f"Project: {self.project.name} | Viewport: {self.project.canvas_width}x{self.project.canvas_height} px | Widgets: {len(self.project.widgets)}",
            font=ctk.CTkFont(family="Segoe UI", size=10),
            text_color="#94a3b8"
        )
        self.lbl_status_left.pack(side="left", padx=12)

        # Clickable health badge
        self.btn_health = ctk.CTkButton(
            self.status_bar,
            text="✓ Clean (0 Issues)",
            font=ctk.CTkFont(family="Segoe UI", size=9, weight="bold"),
            fg_color="#065f46",
            hover_color="#047857",
            height=20,
            width=120,
            command=self._switch_to_diagnostics
        )
        self.btn_health.pack(side="right", padx=(4, 10))

        # Clickable Console toggle button
        self.btn_toggle_console = ctk.CTkButton(
            self.status_bar,
            text="💻 Console (0)",
            font=ctk.CTkFont(family="Segoe UI", size=9, weight="bold"),
            fg_color="#1e293b",
            hover_color="#3464a8",
            height=20,
            width=100,
            command=self._toggle_console
        )
        self.btn_toggle_console.pack(side="right", padx=4)

        self.lbl_status_event = ctk.CTkLabel(
            self.status_bar,
            text="Windose OS v10 Aero Engine Active | Ready.",
            font=ctk.CTkFont(family="Segoe UI", size=10),
            text_color="#93c5fd"
        )
        self.lbl_status_event.pack(side="right", padx=10)

    def _bind_shortcuts(self) -> None:
        self.bind("<Control-s>", lambda e: self._action_save_project())
        self.bind("<Control-o>", lambda e: self._action_open_project())
        self.bind("<Control-n>", lambda e: self._action_new_project())
        self.bind("<F5>", lambda e: self._set_mode("sim" if not self.canvas.simulation_mode else "design"))
        self.bind("<F12>", lambda e: self._toggle_console())

    # -------------------------------------------------------------
    # Mode & View Switching
    # -------------------------------------------------------------
    def _set_mode(self, mode: str) -> None:
        if mode == "design":
            self.btn_design_mode.configure(fg_color="#2563eb")
            self.btn_sim_mode.configure(fg_color="transparent")
            self.canvas.set_simulation_mode(False)
            self.lbl_status_event.configure(
                text="Design Mode active. Click widgets to inspect or reposition.",
                text_color="#38bdf8"
            )
        else:
            self.btn_design_mode.configure(fg_color="transparent")
            self.btn_sim_mode.configure(fg_color="#10b981")
            self.canvas.set_simulation_mode(True)
            self.lbl_status_event.configure(
                text="Live Simulation running! Click buttons, drag sliders, type in fields to test UI events.",
                text_color="#10b981"
            )
            if not self._console_visible:
                self._toggle_console()

    def _toggle_console(self) -> None:
        """Toggles the collapsible Live Runtime Console dock at the bottom of workspace."""
        if self._console_visible:
            self.console_panel.pack_forget()
            self._console_visible = False
            self.btn_toggle_console.configure(fg_color="#1e293b")
        else:
            self.console_panel.pack(side="bottom", fill="x")
            self._console_visible = True
            self.btn_toggle_console.configure(fg_color="#3464a8")

    def _on_console_command(self, cmd: str) -> None:
        """Dispatches terminal commands entered in the Live Console."""
        parts = cmd.strip().split(maxsplit=2)
        if not parts:
            return
        action = parts[0].lower()

        if action == "click":
            if len(parts) > 1:
                target_name = parts[1]
                w = next((w for w in self.project.widgets if w.name.lower() == target_name.lower()), None)
                if w:
                    self.console_panel.log("CLI", f"Simulating click on widget '{w.name}'...")
                    self.canvas._handle_simulation_click(w.x + w.width // 2, w.y + w.height // 2)
                else:
                    self.console_panel.log("CLI", f"Widget '{target_name}' not found.", level="error")
            else:
                self.console_panel.log("CLI", "Usage: click <widget_name>", level="error")

        elif action == "msgbox":
            title = parts[1] if len(parts) > 1 else "Windose Alert"
            text = parts[2] if len(parts) > 2 else "Test message box from console."
            self.canvas.trigger_mock_msgbox(title, text, "OK", "info")
            self.console_panel.log("CLI", f"Triggered k_msgbox('{title}', '{text}')", level="api")

        elif action == "set":
            if len(parts) > 2:
                target_name = parts[1]
                val = parts[2]
                w = next((w for w in self.project.widgets if w.name.lower() == target_name.lower()), None)
                if w:
                    w.value = val
                    self.canvas.redraw()
                    self.console_panel.log("CLI", f"Set {w.name}.value = '{val}'")
                else:
                    self.console_panel.log("CLI", f"Widget '{target_name}' not found.", level="error")
            else:
                self.console_panel.log("CLI", "Usage: set <widget_name> <value>", level="error")

        else:
            self.console_panel.log("CLI", f"Unknown command '{action}'. Type 'help' for instructions.", level="error")

    def _on_view_switched(self, view_name: str) -> None:
        self.canvas.pack_forget()
        self.codegen_panel.pack_forget()
        self.diagnostics_panel.pack_forget()
        self.elf_panel.pack_forget()

        if view_name == "Visual Canvas":
            self.canvas.pack(fill="both", expand=True)
            self.canvas.redraw()
        elif view_name == "C/ELF Code":
            self.codegen_panel.pack(fill="both", expand=True)
            if self._code_dirty:
                self.codegen_panel.refresh_code()
                self._code_dirty = False
        elif view_name == "Diagnostics & Linting":
            self.diagnostics_panel.pack(fill="both", expand=True)
            self.diagnostics_panel.refresh_diagnostics()
        elif view_name == "ELF Binary Suite":
            self.elf_panel.pack(fill="both", expand=True)

        # Keep console pinned to bottom if visible
        if self._console_visible:
            self.console_panel.pack_forget()
            self.console_panel.pack(side="bottom", fill="x")

    def _switch_to_elf_suite(self) -> None:
        self.view_switcher.set("ELF Binary Suite")
        self._on_view_switched("ELF Binary Suite")

    def _switch_to_diagnostics(self) -> None:
        self.view_switcher.set("Diagnostics & Linting")
        self._on_view_switched("Diagnostics & Linting")

    def _on_widget_double_clicked(self, widget_id: str) -> None:
        """Double clicking any widget in canvas instantly switches to code view and jumps to its C callback."""
        w = self.project.get_widget(widget_id)
        if w:
            handler = w.event_handler or f"on_{w.name}_event"
            self._switch_to_code_and_jump(handler)

    def _switch_to_code_and_jump(self, handler_name: str) -> None:
        """Navigates editor to C event callback and focuses cursor inside function body."""
        self.view_switcher.set("C/ELF Code")
        self._on_view_switched("C/ELF Code")
        self.codegen_panel.jump_to_handler(handler_name)
        self.lbl_status_event.configure(
            text=f"Editing event callback: {handler_name}()",
            text_color="#34d399"
        )

    def _on_diagnostics_select_widget(self, widget_id: str) -> None:
        self.project.select_widget(widget_id)
        self.view_switcher.set("Visual Canvas")
        self._on_view_switched("Visual Canvas")
        self._sync_all()

    def _on_diagnostics_fix_applied(self) -> None:
        self._sync_all()
        self.lbl_status_event.configure(
            text="Applied automated layout & identifier repairs across project.",
            text_color="#34d399"
        )

    # -------------------------------------------------------------
    # Event Callbacks
    # -------------------------------------------------------------
    def _on_add_widget(self, widget_type: str) -> None:
        cx = self.project.canvas_width // 2 - 60
        cy = self.project.canvas_height // 2 - 20
        offset = len(self.project.widgets) * 16 % 120

        # Sensible defaults per widget type
        w, h = 140, 36
        if widget_type == WidgetType.PANEL:
            w, h = 260, 160
        elif widget_type == WidgetType.TEXTAREA:
            w, h = 260, 140
        elif widget_type == WidgetType.DATAGRID:
            w, h = 320, 160
        elif widget_type == WidgetType.LISTBOX:
            w, h = 200, 140
        elif widget_type == WidgetType.TABS:
            w, h = 320, 180
        elif widget_type == WidgetType.SEPARATOR:
            w, h = 240, 10
        elif widget_type == WidgetType.CANVAS:
            w, h = 220, 150
        elif widget_type == WidgetType.IMAGE:
            w, h = 140, 100
        elif widget_type == WidgetType.STATUSBAR:
            w, h = self.project.canvas_width, 26
            cx, cy, offset = 0, self.project.canvas_height - 26, 0
        elif widget_type == WidgetType.SWITCH:
            w, h = 120, 30
        elif widget_type in (WidgetType.CHECKBOX, WidgetType.RADIO):
            w, h = 140, 26
        elif widget_type == WidgetType.NUMERIC:
            w, h = 100, 32
        elif widget_type == WidgetType.COLORPICKER:
            w, h = 130, 32

        new_w = Widget(
            widget_type=widget_type,
            x=self.project.snap(cx + offset),
            y=self.project.snap(cy + offset),
            width=w,
            height=h
        )
        if widget_type == WidgetType.PANEL:
            new_w.bg_color = "#161e2e"

        self.project.add_widget(new_w)
        self._sync_all()
        self.lbl_status_event.configure(text=f"Added component '{new_w.name}' ({widget_type})")

    def _on_align_action(self, mode: str) -> None:
        self.project.align_selected(mode)
        self._sync_all()
        self.lbl_status_event.configure(text=f"Aligned widgets: {mode}")

    def _on_canvas_selection_changed(self, widget_id: Optional[str]) -> None:
        w = self.project.get_widget(widget_id)
        self.property_panel.inspect(w)
        self.hierarchy_panel.set_selected_widget(widget_id)
        if w:
            self.lbl_status_event.configure(
                text=f"Selected: {w.name} [{w.widget_type}] @ ({w.x}, {w.y}) - Size: {w.width}x{w.height}"
            )

    def _on_canvas_widget_modified(self) -> None:
        w = self.project.get_selected_widget()
        if w:
            self.property_panel.update_geometry_only(w)
        self._code_dirty = True
        if self.view_switcher.get() == "C/ELF Code":
            self.codegen_panel.refresh_code()

    def _on_property_changed(self) -> None:
        self.canvas.redraw()
        self._code_dirty = True
        if self.view_switcher.get() == "C/ELF Code":
            self.codegen_panel.refresh_code()

    def _on_widget_selected_from_tree(self, widget_id: Optional[str]) -> None:
        w = self.project.get_widget(widget_id)
        self.property_panel.inspect(w)
        self.canvas.redraw()

    def _on_reorder_widget(self, widget_id: str, direction: str) -> None:
        self.project.reorder(widget_id, direction)
        self.canvas.redraw()

    def _on_inspector_action(self, action: str) -> None:
        if action == "duplicate":
            new_w = self.project.duplicate_selected()
            if new_w:
                self._sync_all()
        elif action == "delete":
            if self.project.delete_selected():
                self._sync_all()
        elif action in ("front", "back"):
            if self.project.selected_id:
                self.project.reorder(self.project.selected_id, "top" if action == "front" else "bottom")
                self._sync_all()

    def _on_change_resolution(self, width: int, height: int) -> None:
        self.project.canvas_width = width
        self.project.canvas_height = height
        self._sync_all()
        self.lbl_status_event.configure(text=f"Canvas resized to {width}x{height} px")

    def _on_load_template(self, tpl_name: str) -> None:
        if tpl_name in TEMPLATES:
            self.project = TEMPLATES[tpl_name]()
            # Update canvas reference
            self.canvas.project = self.project
            self.hierarchy_panel.project = self.project
            self.codegen_panel.project = self.project
            self.elf_panel.project = self.project
            self._sync_all()
            self.lbl_status_event.configure(text=f"Loaded template: {tpl_name}")

    def _on_grid_settings_changed(self, grid_size: int, snap_enabled: bool) -> None:
        self.project.grid_size = grid_size
        self.project.snap_to_grid = snap_enabled
        self.canvas.redraw()

    def _on_sim_event(self, widget_name: str, details: str) -> None:
        self.lbl_status_event.configure(
            text=f"[EVENT] {widget_name} ➔ {details}",
            text_color="#38bdf8"
        )
        if hasattr(self, "console_panel"):
            self.console_panel.log(widget_name, details)
            if hasattr(self, "btn_toggle_console"):
                self.btn_toggle_console.configure(text=f"💻 Console ({self.console_panel.event_count})")

    def _sync_all(self) -> None:
        self.canvas.redraw()
        cur_w = self.project.get_selected_widget()
        self.property_panel.inspect(cur_w)
        self.hierarchy_panel.refresh_tree()
        self._code_dirty = True
        if self.view_switcher.get() == "C/ELF Code":
            self.codegen_panel.refresh_code()
            self._code_dirty = False
        elif self.view_switcher.get() == "Diagnostics & Linting":
            self.diagnostics_panel.refresh_diagnostics()

        self.lbl_status_left.configure(
            text=f"Project: {self.project.name} | Viewport: {self.project.canvas_width}x{self.project.canvas_height} px | Widgets: {len(self.project.widgets)}"
        )

        # Real-time health linting badge
        diags = ProjectValidator.validate_project(self.project)
        err_cnt = sum(1 for d in diags if d.severity == DiagnosticSeverity.ERROR)
        warn_cnt = sum(1 for d in diags if d.severity == DiagnosticSeverity.WARNING)

        if err_cnt > 0:
            self.btn_health.configure(
                text=f"❌ {err_cnt} Errors, {warn_cnt} Warn",
                fg_color="#7f1d1d",
                hover_color="#991b1b"
            )
        elif warn_cnt > 0:
            self.btn_health.configure(
                text=f"⚠️ {warn_cnt} Warnings",
                fg_color="#78350f",
                hover_color="#92400e"
            )
        else:
            self.btn_health.configure(
                text="✓ Clean (0 Issues)",
                fg_color="#065f46",
                hover_color="#047857"
            )

    # -------------------------------------------------------------
    # Project File Actions & Templates Modal
    # -------------------------------------------------------------
    def _action_show_templates_modal(self) -> None:
        """Opens interactive gallery of built-in Aero application templates."""
        dialog = ctk.CTkToplevel(self)
        dialog.title("✨ Project Templates Gallery - Windose Aero Studio")
        dialog.geometry("640x520")
        dialog.resizable(False, False)
        dialog.grab_set()

        header = ctk.CTkFrame(dialog, fg_color="#131e2b", height=50, corner_radius=0)
        header.pack(fill="x", side="top")
        ctk.CTkLabel(
            header,
            text="✨ WINDOSE OS APPLICATION TEMPLATES",
            font=ctk.CTkFont(family="Segoe UI", size=13, weight="bold"),
            text_color="#38bdf8"
        ).pack(side="left", padx=16)

        ctk.CTkLabel(
            header,
            text="Select a pre-built Aero layout to start",
            font=ctk.CTkFont(family="Segoe UI", size=10),
            text_color="#94a3b8"
        ).pack(side="right", padx=16)

        scroll = ctk.CTkScrollableFrame(dialog, fg_color="transparent")
        scroll.pack(fill="both", expand=True, padx=14, pady=12)

        meta = {
            "Windose Aero Explorer (800x500)": ("🪟", "Windows 7 Aero Glass File Explorer with navigation pane & datagrid."),
            "Windose Kernel Monitor (800x500)": ("📊", "Kernel process monitor, CPU graph, and system telemetry bars."),
            "Windose Control Panel (720x480)": ("⚙️", "Category-based settings center for devices, display, and security."),
            "Windose Aero Notepad (640x480)": ("📝", "Lightweight document editor with quick action toolbar and status bar."),
            "Database Browser (800x520)": ("🗄️", "Records browser with search filter, tabular view, and action controls."),
            "IoT Device Hub (700x450)": ("📡", "Industrial sensor dashboard with dials, switches, and sliders."),
            "Windose Login Screen (500x320)": ("🔑", "Authentic user lockscreen dialog with avatar and password input.")
        }

        for tpl_name, fn in TEMPLATES.items():
            icon, desc = meta.get(tpl_name, ("📦", "Custom Windose application layout."))
            card = ctk.CTkFrame(scroll, fg_color="#182535", corner_radius=6)
            card.pack(fill="x", pady=5, padx=2)

            left_box = ctk.CTkFrame(card, fg_color="transparent")
            left_box.pack(side="left", fill="both", expand=True, padx=12, pady=10)

            title_row = ctk.CTkFrame(left_box, fg_color="transparent")
            title_row.pack(fill="x")

            ctk.CTkLabel(
                title_row,
                text=f"{icon}  {tpl_name}",
                font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold"),
                text_color="#f8fafc"
            ).pack(side="left")

            ctk.CTkLabel(
                left_box,
                text=desc,
                font=ctk.CTkFont(family="Segoe UI", size=10),
                text_color="#94a3b8",
                wraplength=420,
                justify="left"
            ).pack(anchor="w", pady=(2, 0))

            btn_load = ctk.CTkButton(
                card,
                text="⚡ Load",
                width=75,
                height=30,
                fg_color="#3464a8",
                hover_color="#2563eb",
                font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
                command=lambda name=tpl_name: [self._on_load_template(name), dialog.destroy()]
            )
            btn_load.pack(side="right", padx=12, pady=10)

    def _action_new_project(self) -> None:
        if messagebox.askyesno("New Project", "Start a new project? Unsaved changes will be lost."):
            self.project = Project("WindoseApp", 800, 500, "#f5f6f7")
            self.project_filepath = None
            self.canvas.project = self.project
            self.hierarchy_panel.project = self.project
            self.codegen_panel.project = self.project
            self.diagnostics_panel.project = self.project
            self.elf_panel.project = self.project
            self._sync_all()

    def _action_open_project(self) -> None:
        path = filedialog.askopenfilename(
            filetypes=[("ELF GUI Project", "*.elfgui;*.json"), ("All Files", "*.*")]
        )
        if path:
            try:
                self.project.load_file(path)
                self.project_filepath = path
                self.canvas.project = self.project
                self.hierarchy_panel.project = self.project
                self.codegen_panel.project = self.project
                self.diagnostics_panel.project = self.project
                self.elf_panel.project = self.project
                self._sync_all()
                messagebox.showinfo("Opened", f"Loaded project from:\n{path}")
            except Exception as ex:
                messagebox.showerror("Error", f"Failed to load project:\n{ex}")

    def _action_save_project(self) -> None:
        if not self.project_filepath:
            path = filedialog.asksaveasfilename(
                defaultextension=".elfgui",
                initialfile=f"{self.project.name.lower()}.elfgui",
                filetypes=[("ELF GUI Project", "*.elfgui"), ("JSON Schema", "*.json"), ("All Files", "*.*")]
            )
            if not path:
                return
            self.project_filepath = path

        try:
            self.project.save_file(self.project_filepath)
            self.lbl_status_event.configure(text=f"Project saved to: {os.path.basename(self.project_filepath)}")
            messagebox.showinfo("Saved", f"Project saved successfully!\n{self.project_filepath}")
        except Exception as ex:
            messagebox.showerror("Save Error", str(ex))

    def _action_undo(self) -> None:
        if self.project.undo():
            self._sync_all()
            self.lbl_status_event.configure(text="Undone last action")

    def _action_redo(self) -> None:
        if self.project.redo():
            self._sync_all()
            self.lbl_status_event.configure(text="Redone action")
