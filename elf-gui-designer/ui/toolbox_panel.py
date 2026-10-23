"""
Toolbox and Component Palette sidebar for ELF Studio.
Organized into categorized widget tools, resolution presets, alignment tools, and templates.
"""

import customtkinter as ctk
from typing import Callable, Optional
from core.models import WidgetType
from ui.theme import CANVAS_PRESETS


class ToolboxPanel(ctk.CTkFrame):
    """Left sidebar containing component tools, resolution presets, alignment tools, and templates."""

    def __init__(
        self,
        master,
        on_add_widget: Callable[[str], None],
        on_change_resolution: Callable[[int, int], None],
        on_load_template: Callable[[str], None],
        on_grid_settings_changed: Callable[[int, bool], None],
        on_align_action: Optional[Callable[[str], None]] = None,
        **kwargs
    ):
        super().__init__(master, fg_color="#16202c", corner_radius=0, width=230, **kwargs)
        self.on_add_widget = on_add_widget
        self.on_change_resolution = on_change_resolution
        self.on_load_template = on_load_template
        self.on_grid_settings_changed = on_grid_settings_changed
        self.on_align_action = on_align_action

        self._build_ui()

    def _build_ui(self) -> None:
        title_lbl = ctk.CTkLabel(
            self, text="COMPONENT PALETTE",
            font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold"),
            text_color="#93c5fd"
        )
        title_lbl.pack(pady=(14, 6), padx=12, anchor="w")

        scroll = ctk.CTkScrollableFrame(self, fg_color="transparent")
        scroll.pack(fill="both", expand=True, padx=6, pady=2)

        # Categories of widgets
        sections = [
            ("STANDARD CONTROLS", [
                (WidgetType.BUTTON, "Push Button", "🔘"),
                (WidgetType.LABEL, "Text Label", "🔤"),
                (WidgetType.ENTRY, "Text Input", "⌨️"),
                (WidgetType.TEXTAREA, "Multiline Text", "📝"),
                (WidgetType.CHECKBOX, "Checkbox", "☑️"),
                (WidgetType.RADIO, "Radio Button", "🔘"),
                (WidgetType.SWITCH, "Toggle Switch", "🔌"),
            ]),
            ("NUMERICAL & RANGES", [
                (WidgetType.SLIDER, "Range Slider", "🎚️"),
                (WidgetType.PROGRESSBAR, "Progress Bar", "📊"),
                (WidgetType.NUMERIC, "Numeric Spinner", "🔢"),
            ]),
            ("LISTS & SELECTION", [
                (WidgetType.DROPDOWN, "Dropdown List", "▼"),
                (WidgetType.LISTBOX, "List Box", "📜"),
                (WidgetType.DATAGRID, "Data Table", "▦"),
                (WidgetType.TABS, "Tab Strip", "📑"),
            ]),
            ("GRAPHICS & CONTAINERS", [
                (WidgetType.PANEL, "Container Card", "📦"),
                (WidgetType.IMAGE, "Graphic / Icon", "🖼️"),
                (WidgetType.CANVAS, "Drawing Canvas", "🎨"),
                (WidgetType.COLORPICKER, "Color Picker", "🎨"),
                (WidgetType.SEPARATOR, "Divider Line", "➖"),
                (WidgetType.STATUSBAR, "Status Bar", "🏷️"),
            ])
        ]

        for sec_name, tools in sections:
            sec_lbl = ctk.CTkLabel(
                scroll, text=sec_name,
                font=ctk.CTkFont(family="Segoe UI", size=10, weight="bold"),
                text_color="#64748b"
            )
            sec_lbl.pack(anchor="w", padx=4, pady=(8, 2))

            for wtype, label, icon in tools:
                btn = ctk.CTkButton(
                    scroll,
                    text=f"  {icon}  {label}",
                    anchor="w",
                    fg_color="#1e2c3c",
                    hover_color="#3464a8",
                    height=30,
                    corner_radius=4,
                    font=ctk.CTkFont(family="Segoe UI", size=11),
                    command=lambda t=wtype: self.on_add_widget(t)
                )
                btn.pack(fill="x", pady=1)

        # Alignment Tools
        sep0 = ctk.CTkFrame(scroll, height=2, fg_color="#243447")
        sep0.pack(fill="x", pady=10)

        align_lbl = ctk.CTkLabel(
            scroll, text="ALIGN & DISTRIBUTE",
            font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            text_color="#93c5fd"
        )
        align_lbl.pack(anchor="w", pady=(0, 4))

        align_row1 = ctk.CTkFrame(scroll, fg_color="transparent")
        align_row1.pack(fill="x", pady=2)
        align_buttons1 = [
            ("Left", "left", "⇤"),
            ("Center X", "center_x", "↔"),
            ("Right", "right", "⇥")
        ]
        for name, mode, icon in align_buttons1:
            btn = ctk.CTkButton(
                align_row1, text=f"{icon} {name}", width=66, height=24,
                fg_color="#1e2c3c", hover_color="#3464a8",
                font=ctk.CTkFont(family="Segoe UI", size=9),
                command=lambda m=mode: self._on_align(m)
            )
            btn.pack(side="left", padx=1)

        align_row2 = ctk.CTkFrame(scroll, fg_color="transparent")
        align_row2.pack(fill="x", pady=2)
        align_buttons2 = [
            ("Top", "top", "⇡"),
            ("Center Y", "center_y", "↕"),
            ("Bottom", "bottom", "⇣")
        ]
        for name, mode, icon in align_buttons2:
            btn = ctk.CTkButton(
                align_row2, text=f"{icon} {name}", width=66, height=24,
                fg_color="#1e2c3c", hover_color="#3464a8",
                font=ctk.CTkFont(family="Segoe UI", size=9),
                command=lambda m=mode: self._on_align(m)
            )
            btn.pack(side="left", padx=1)

        align_row3 = ctk.CTkFrame(scroll, fg_color="transparent")
        align_row3.pack(fill="x", pady=2)
        btn_dist_h = ctk.CTkButton(
            align_row3, text="Distribute ↔", width=100, height=24,
            fg_color="#1e2c3c", hover_color="#3464a8",
            font=ctk.CTkFont(family="Segoe UI", size=9),
            command=lambda: self._on_align("distribute_h")
        )
        btn_dist_h.pack(side="left", padx=1)
        btn_dist_v = ctk.CTkButton(
            align_row3, text="Distribute ↕", width=100, height=24,
            fg_color="#1e2c3c", hover_color="#3464a8",
            font=ctk.CTkFont(family="Segoe UI", size=9),
            command=lambda: self._on_align("distribute_v")
        )
        btn_dist_v.pack(side="left", padx=1)

        align_row4 = ctk.CTkFrame(scroll, fg_color="transparent")
        align_row4.pack(fill="x", pady=2)
        btn_match_w = ctk.CTkButton(
            align_row4, text="Same Width", width=66, height=24,
            fg_color="#1e2c3c", hover_color="#3464a8",
            font=ctk.CTkFont(family="Segoe UI", size=8),
            command=lambda: self._on_align("match_width")
        )
        btn_match_w.pack(side="left", padx=1)
        btn_match_h = ctk.CTkButton(
            align_row4, text="Same Height", width=66, height=24,
            fg_color="#1e2c3c", hover_color="#3464a8",
            font=ctk.CTkFont(family="Segoe UI", size=8),
            command=lambda: self._on_align("match_height")
        )
        btn_match_h.pack(side="left", padx=1)
        btn_match_s = ctk.CTkButton(
            align_row4, text="Same Size", width=66, height=24,
            fg_color="#1e2c3c", hover_color="#3464a8",
            font=ctk.CTkFont(family="Segoe UI", size=8),
            command=lambda: self._on_align("match_size")
        )
        btn_match_s.pack(side="left", padx=1)

        align_row5 = ctk.CTkFrame(scroll, fg_color="transparent")
        align_row5.pack(fill="x", pady=2)
        btn_ctr_h = ctk.CTkButton(
            align_row5, text="Center Win (H)", width=100, height=24,
            fg_color="#1e2c3c", hover_color="#3464a8",
            font=ctk.CTkFont(family="Segoe UI", size=9),
            command=lambda: self._on_align("center_canvas_h")
        )
        btn_ctr_h.pack(side="left", padx=1)
        btn_ctr_v = ctk.CTkButton(
            align_row5, text="Center Win (V)", width=100, height=24,
            fg_color="#1e2c3c", hover_color="#3464a8",
            font=ctk.CTkFont(family="Segoe UI", size=9),
            command=lambda: self._on_align("center_canvas_v")
        )
        btn_ctr_v.pack(side="left", padx=1)

        # Target Display Section
        sep1 = ctk.CTkFrame(scroll, height=2, fg_color="#243447")
        sep1.pack(fill="x", pady=10)

        res_lbl = ctk.CTkLabel(
            scroll, text="TARGET DISPLAY / RESOLUTION",
            font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            text_color="#93c5fd"
        )
        res_lbl.pack(anchor="w", pady=(0, 4))

        self.res_menu = ctk.CTkOptionMenu(
            scroll,
            values=[name for name, _, _ in CANVAS_PRESETS],
            command=self._on_resolution_selected,
            fg_color="#1e2c3c",
            button_color="#273a4f",
            button_hover_color="#3464a8",
            height=30,
            font=ctk.CTkFont(family="Segoe UI", size=11)
        )
        self.res_menu.set(CANVAS_PRESETS[0][0])
        self.res_menu.pack(fill="x", pady=(0, 10))

        # Grid Snapping
        grid_lbl = ctk.CTkLabel(
            scroll, text="GRID ALIGNMENT",
            font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            text_color="#93c5fd"
        )
        grid_lbl.pack(anchor="w", pady=(0, 4))

        self.snap_switch = ctk.CTkSwitch(
            scroll,
            text="Snap to Grid",
            command=self._on_grid_toggled,
            font=ctk.CTkFont(family="Segoe UI", size=11),
            progress_color="#3464a8"
        )
        self.snap_switch.select()
        self.snap_switch.pack(anchor="w", pady=(0, 6))

        self.grid_size_menu = ctk.CTkSegmentedButton(
            scroll,
            values=["4px", "8px", "16px", "32px"],
            command=self._on_grid_size_selected,
            selected_color="#3464a8",
            selected_hover_color="#2563eb",
            unselected_color="#1e2c3c",
            font=ctk.CTkFont(family="Segoe UI", size=10)
        )
        self.grid_size_menu.set("8px")
        self.grid_size_menu.pack(fill="x", pady=(0, 12))

        # Sample Templates Section
        sep2 = ctk.CTkFrame(scroll, height=2, fg_color="#243447")
        sep2.pack(fill="x", pady=6)

        tpl_lbl = ctk.CTkLabel(
            scroll, text="WINDOSE AERO TEMPLATES",
            font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            text_color="#93c5fd"
        )
        tpl_lbl.pack(anchor="w", pady=(0, 4))

        from core.templates import TEMPLATES
        self.tpl_menu = ctk.CTkOptionMenu(
            scroll,
            values=list(TEMPLATES.keys()),
            command=self.on_load_template,
            fg_color="#1e2c3c",
            button_color="#273a4f",
            button_hover_color="#3464a8",
            height=30,
            font=ctk.CTkFont(family="Segoe UI", size=11)
        )
        self.tpl_menu.set("Load Template...")
        self.tpl_menu.pack(fill="x", pady=(0, 16))

    def _on_align(self, mode: str) -> None:
        if self.on_align_action:
            self.on_align_action(mode)

    def _on_resolution_selected(self, choice: str) -> None:
        for name, w, h in CANVAS_PRESETS:
            if name == choice:
                self.on_change_resolution(w, h)
                break

    def _on_grid_toggled(self) -> None:
        self._emit_grid_settings()

    def _on_grid_size_selected(self, choice: str) -> None:
        self._emit_grid_settings()

    def _emit_grid_settings(self) -> None:
        val_str = self.grid_size_menu.get().replace("px", "")
        grid_sz = int(val_str) if val_str.isdigit() else 8
        snap_on = bool(self.snap_switch.get())
        self.on_grid_settings_changed(grid_sz, snap_on)
