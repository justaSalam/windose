"""
Property Inspector Panel for ELF Studio.
Allows real-time inspection and editing of widget properties, geometry, and events.
"""

import customtkinter as ctk
from typing import Optional, Callable
from core.models import Widget, WidgetType
from ui.theme import PRESET_COLORS


class PropertyPanel(ctk.CTkFrame):
    """Right sidebar property inspector with real-time validation and code navigation."""

    def __init__(
        self,
        master,
        on_property_changed: Callable[[], None],
        on_action: Callable[[str], None],  # 'duplicate', 'delete', 'front', 'back'
        on_jump_to_code: Optional[Callable[[str], None]] = None,
        **kwargs
    ):
        super().__init__(master, fg_color="#16202c", corner_radius=0, width=290, **kwargs)
        self.on_property_changed = on_property_changed
        self.on_action = on_action
        self.on_jump_to_code = on_jump_to_code
        self.current_widget: Optional[Widget] = None
        self._updating = False

        self._build_ui()

    def _build_ui(self) -> None:
        title = ctk.CTkLabel(
            self, text="PROPERTY INSPECTOR",
            font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold"),
            text_color="#93c5fd"
        )
        title.pack(pady=(14, 6), padx=12, anchor="w")

        self.scroll = ctk.CTkScrollableFrame(self, fg_color="transparent")
        self.scroll.pack(fill="both", expand=True, padx=8, pady=2)

        # Empty state label
        self.no_sel_lbl = ctk.CTkLabel(
            self.scroll,
            text="No widget selected.\nClick on a widget in the canvas\nor add one from the palette.",
            text_color="#64748b",
            font=ctk.CTkFont(family="Segoe UI", size=12),
            justify="center"
        )
        self.no_sel_lbl.pack(pady=40)

        # Properties container
        self.form_frame = ctk.CTkFrame(self.scroll, fg_color="transparent")

        # --- Identification ---
        self._add_section_header("IDENTIFICATION")
        self.ent_name = self._add_entry_field("C Identifier (Name):", self._on_name_change)
        self.lbl_name_alert = ctk.CTkLabel(self.form_frame, text="", text_color="#34d399", font=ctk.CTkFont(family="Segoe UI", size=9))
        self.lbl_name_alert.pack(anchor="w", padx=4, pady=(0, 2))
        self.lbl_type = ctk.CTkLabel(self.form_frame, text="", text_color="#93c5fd", font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"))
        self.lbl_type.pack(anchor="w", padx=4, pady=(0, 6))

        # --- Transform / Layout ---
        self._add_section_header("GEOMETRY & LAYOUT")
        grid_geo = ctk.CTkFrame(self.form_frame, fg_color="transparent")
        grid_geo.pack(fill="x", pady=2)

        # X, Y
        ctk.CTkLabel(grid_geo, text="X:", font=ctk.CTkFont(size=11), text_color="#94a3b8").grid(row=0, column=0, sticky="w")
        self.ent_x = ctk.CTkEntry(grid_geo, width=70, height=26, font=ctk.CTkFont(size=11))
        self.ent_x.grid(row=0, column=1, padx=4, pady=2)
        self.ent_x.bind("<FocusOut>", lambda e: self._on_geometry_change())
        self.ent_x.bind("<Return>", lambda e: self._on_geometry_change())

        ctk.CTkLabel(grid_geo, text="Y:", font=ctk.CTkFont(size=11), text_color="#94a3b8").grid(row=0, column=2, sticky="w", padx=(6, 0))
        self.ent_y = ctk.CTkEntry(grid_geo, width=70, height=26, font=ctk.CTkFont(size=11))
        self.ent_y.grid(row=0, column=3, padx=4, pady=2)
        self.ent_y.bind("<FocusOut>", lambda e: self._on_geometry_change())
        self.ent_y.bind("<Return>", lambda e: self._on_geometry_change())

        # Width, Height
        ctk.CTkLabel(grid_geo, text="W:", font=ctk.CTkFont(size=11), text_color="#94a3b8").grid(row=1, column=0, sticky="w")
        self.ent_w = ctk.CTkEntry(grid_geo, width=70, height=26, font=ctk.CTkFont(size=11))
        self.ent_w.grid(row=1, column=1, padx=4, pady=2)
        self.ent_w.bind("<FocusOut>", lambda e: self._on_geometry_change())
        self.ent_w.bind("<Return>", lambda e: self._on_geometry_change())

        ctk.CTkLabel(grid_geo, text="H:", font=ctk.CTkFont(size=11), text_color="#94a3b8").grid(row=1, column=2, sticky="w", padx=(6, 0))
        self.ent_h = ctk.CTkEntry(grid_geo, width=70, height=26, font=ctk.CTkFont(size=11))
        self.ent_h.grid(row=1, column=3, padx=4, pady=2)
        self.ent_h.bind("<FocusOut>", lambda e: self._on_geometry_change())
        self.ent_h.bind("<Return>", lambda e: self._on_geometry_change())

        # --- Appearance ---
        self._add_section_header("APPEARANCE")
        self.ent_text = self._add_entry_field("Text / Caption:", self._on_text_change)
        self.ent_placeholder = self._add_entry_field("Placeholder / Hint:", self._on_placeholder_change)
        self.ent_bg = self._add_entry_field("Background Color (#Hex):", self._on_bg_change)

        # Color Palette Chips
        chip_frame = ctk.CTkFrame(self.form_frame, fg_color="transparent")
        chip_frame.pack(fill="x", pady=4)
        for hex_col, _ in PRESET_COLORS:
            btn = ctk.CTkButton(
                chip_frame, text="", fg_color=hex_col, hover_color=hex_col,
                width=22, height=22, corner_radius=4,
                command=lambda c=hex_col: self._apply_color_chip(c)
            )
            btn.pack(side="left", padx=2)

        self.ent_text_color = self._add_entry_field("Text Color (#Hex):", self._on_text_color_change)
        self.ent_radius = self._add_entry_field("Corner Radius (px):", self._on_radius_change)
        self.ent_fsize = self._add_entry_field("Font Size (pt):", self._on_font_size_change)

        # --- Widget Specific Fields ---
        self._add_section_header("SPECIFIC ATTRIBUTES")
        self.ent_group = self._add_entry_field("Radio Group Name:", self._on_group_change)
        self.ent_items = self._add_entry_field("Items / Pages / Columns (,):", self._on_items_change)

        # Min / Max / Step
        grid_ranges = ctk.CTkFrame(self.form_frame, fg_color="transparent")
        grid_ranges.pack(fill="x", pady=2)
        ctk.CTkLabel(grid_ranges, text="Min:", font=ctk.CTkFont(size=11), text_color="#94a3b8").grid(row=0, column=0, sticky="w")
        self.ent_min = ctk.CTkEntry(grid_ranges, width=60, height=26, font=ctk.CTkFont(size=11))
        self.ent_min.grid(row=0, column=1, padx=2, pady=2)
        self.ent_min.bind("<FocusOut>", lambda e: self._on_range_change())
        self.ent_min.bind("<Return>", lambda e: self._on_range_change())

        ctk.CTkLabel(grid_ranges, text="Max:", font=ctk.CTkFont(size=11), text_color="#94a3b8").grid(row=0, column=2, sticky="w", padx=(4, 0))
        self.ent_max = ctk.CTkEntry(grid_ranges, width=60, height=26, font=ctk.CTkFont(size=11))
        self.ent_max.grid(row=0, column=3, padx=2, pady=2)
        self.ent_max.bind("<FocusOut>", lambda e: self._on_range_change())
        self.ent_max.bind("<Return>", lambda e: self._on_range_change())

        ctk.CTkLabel(grid_ranges, text="Step:", font=ctk.CTkFont(size=11), text_color="#94a3b8").grid(row=0, column=4, sticky="w", padx=(4, 0))
        self.ent_step = ctk.CTkEntry(grid_ranges, width=50, height=26, font=ctk.CTkFont(size=11))
        self.ent_step.grid(row=0, column=5, padx=2, pady=2)
        self.ent_step.bind("<FocusOut>", lambda e: self._on_range_change())
        self.ent_step.bind("<Return>", lambda e: self._on_range_change())

        # --- Logic & Events ---
        self._add_section_header("ELF EVENT & CALLBACK")
        self.ent_event = self._add_entry_field("Event Callback:", self._on_event_change)
        self.ent_value = self._add_entry_field("Initial Value / State:", self._on_value_change)

        self.btn_jump_code = ctk.CTkButton(
            self.form_frame,
            text="⚡ Jump to C Callback in Editor",
            fg_color="#0284c7",
            hover_color="#0369a1",
            height=28,
            font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            command=self._on_jump_clicked
        )
        self.btn_jump_code.pack(fill="x", pady=(4, 6))

        # --- Action Buttons ---
        self._add_section_header("ACTIONS & ORDERING")
        btn_grid = ctk.CTkFrame(self.form_frame, fg_color="transparent")
        btn_grid.pack(fill="x", pady=4)

        b_dup = ctk.CTkButton(
            btn_grid, text="📋 Duplicate", fg_color="#1e2c3c", hover_color="#3464a8",
            height=28, font=ctk.CTkFont(size=11), command=lambda: self.on_action("duplicate")
        )
        b_dup.grid(row=0, column=0, padx=2, pady=2, sticky="ew")

        b_del = ctk.CTkButton(
            btn_grid, text="🗑️ Delete", fg_color="#c31e1e", hover_color="#e81123",
            height=28, font=ctk.CTkFont(size=11), command=lambda: self.on_action("delete")
        )
        b_del.grid(row=0, column=1, padx=2, pady=2, sticky="ew")

        b_front = ctk.CTkButton(
            btn_grid, text="⬆ To Front", fg_color="#1e2c3c", hover_color="#3464a8",
            height=28, font=ctk.CTkFont(size=11), command=lambda: self.on_action("front")
        )
        b_front.grid(row=1, column=0, padx=2, pady=2, sticky="ew")

        b_back = ctk.CTkButton(
            btn_grid, text="⬇ To Back", fg_color="#1e2c3c", hover_color="#3464a8",
            height=28, font=ctk.CTkFont(size=11), command=lambda: self.on_action("back")
        )
        b_back.grid(row=1, column=1, padx=2, pady=2, sticky="ew")
        btn_grid.columnconfigure(0, weight=1)
        btn_grid.columnconfigure(1, weight=1)

    def _add_section_header(self, text: str) -> None:
        lbl = ctk.CTkLabel(
            self.form_frame, text=text,
            font=ctk.CTkFont(family="Segoe UI", size=10, weight="bold"),
            text_color="#64748b"
        )
        lbl.pack(anchor="w", pady=(10, 2))

    def _add_entry_field(self, label: str, callback: Callable) -> ctk.CTkEntry:
        lbl = ctk.CTkLabel(self.form_frame, text=label, font=ctk.CTkFont(size=11), text_color="#94a3b8")
        lbl.pack(anchor="w", pady=(2, 0))
        entry = ctk.CTkEntry(self.form_frame, height=28, font=ctk.CTkFont(size=11))
        entry.pack(fill="x", pady=(0, 4))
        entry.bind("<FocusOut>", lambda e: callback())
        entry.bind("<Return>", lambda e: callback())
        return entry

    def _validate_identifier_display(self, name: str) -> None:
        from core.validator import RESERVED_C_KEYWORDS, IDENTIFIER_REGEX
        clean = name.strip()
        if not clean:
            self.lbl_name_alert.configure(text="❌ Identifier cannot be empty", text_color="#ef4444")
        elif clean.lower() in RESERVED_C_KEYWORDS:
            self.lbl_name_alert.configure(text=f"❌ '{clean}' is a reserved C keyword", text_color="#ef4444")
        elif not IDENTIFIER_REGEX.match(clean):
            self.lbl_name_alert.configure(text="❌ Invalid characters (letters, digits, _ only)", text_color="#ef4444")
        else:
            self.lbl_name_alert.configure(text=f"✓ Valid C identifier (h_{clean})", text_color="#34d399")

    def inspect(self, widget: Optional[Widget]) -> None:
        self.current_widget = widget
        if not widget:
            self.form_frame.pack_forget()
            self.no_sel_lbl.pack(pady=40)
            return

        self._updating = True
        self.no_sel_lbl.pack_forget()
        self.form_frame.pack(fill="both", expand=True)

        self.lbl_type.configure(text=f"TYPE: {widget.widget_type.upper()}")
        self._set_text(self.ent_name, widget.name)
        self._validate_identifier_display(widget.name)
        self._set_text(self.ent_x, str(widget.x))
        self._set_text(self.ent_y, str(widget.y))
        self._set_text(self.ent_w, str(widget.width))
        self._set_text(self.ent_h, str(widget.height))
        self._set_text(self.ent_text, widget.text)
        self._set_text(self.ent_placeholder, widget.placeholder)
        self._set_text(self.ent_bg, widget.bg_color)
        self._set_text(self.ent_text_color, widget.text_color)
        self._set_text(self.ent_radius, str(widget.corner_radius))
        self._set_text(self.ent_fsize, str(widget.font_size))
        self._set_text(self.ent_group, widget.group)

        # Items / columns
        if widget.widget_type == WidgetType.DATAGRID:
            self._set_text(self.ent_items, ", ".join(widget.columns or []))
        elif widget.widget_type in (WidgetType.LISTBOX, WidgetType.TABS):
            self._set_text(self.ent_items, ", ".join(widget.items or []))
        elif widget.widget_type == WidgetType.DROPDOWN:
            self._set_text(self.ent_items, ", ".join(widget.options or []))
        else:
            self._set_text(self.ent_items, "")

        self._set_text(self.ent_min, str(widget.min_val))
        self._set_text(self.ent_max, str(widget.max_val))
        self._set_text(self.ent_step, str(widget.step))
        self._set_text(self.ent_event, widget.event_handler)
        self._set_text(self.ent_value, str(widget.value if widget.value is not None else ""))

        self._updating = False

    def update_geometry_only(self, widget: Widget) -> None:
        if not self.current_widget or self.current_widget.id != widget.id:
            return
        self._updating = True
        self._set_text(self.ent_x, str(widget.x))
        self._set_text(self.ent_y, str(widget.y))
        self._set_text(self.ent_w, str(widget.width))
        self._set_text(self.ent_h, str(widget.height))
        self._updating = False

    def _set_text(self, entry: ctk.CTkEntry, text: str) -> None:
        entry.delete(0, "end")
        entry.insert(0, text)

    def _on_name_change(self) -> None:
        if self._updating or not self.current_widget: return
        raw_name = self.ent_name.get().strip()
        self._validate_identifier_display(raw_name)
        clean_name = "".join(c if c.isalnum() or c == "_" else "_" for c in raw_name)
        self.current_widget.name = clean_name or "widget"
        self.on_property_changed()

    def _on_jump_clicked(self) -> None:
        if self.current_widget and self.on_jump_to_code:
            handler = self.current_widget.event_handler or f"on_{self.current_widget.name}_event"
            self.on_jump_to_code(handler)

    def _on_geometry_change(self) -> None:
        if self._updating or not self.current_widget: return
        try:
            self.current_widget.x = int(self.ent_x.get())
            self.current_widget.y = int(self.ent_y.get())
            self.current_widget.width = max(6, int(self.ent_w.get()))
            self.current_widget.height = max(6, int(self.ent_h.get()))
            self.on_property_changed()
        except ValueError:
            pass

    def _on_text_change(self) -> None:
        if self._updating or not self.current_widget: return
        self.current_widget.text = self.ent_text.get()
        self.on_property_changed()

    def _on_placeholder_change(self) -> None:
        if self._updating or not self.current_widget: return
        self.current_widget.placeholder = self.ent_placeholder.get()
        self.on_property_changed()

    def _on_group_change(self) -> None:
        if self._updating or not self.current_widget: return
        self.current_widget.group = self.ent_group.get().strip()
        self.on_property_changed()

    def _on_items_change(self) -> None:
        if self._updating or not self.current_widget: return
        raw = self.ent_items.get()
        parts = [p.strip() for p in raw.split(",") if p.strip()]
        if self.current_widget.widget_type == WidgetType.DATAGRID:
            self.current_widget.columns = parts
        elif self.current_widget.widget_type in (WidgetType.LISTBOX, WidgetType.TABS):
            self.current_widget.items = parts
        elif self.current_widget.widget_type == WidgetType.DROPDOWN:
            self.current_widget.options = parts
        self.on_property_changed()

    def _on_range_change(self) -> None:
        if self._updating or not self.current_widget: return
        try:
            self.current_widget.min_val = float(self.ent_min.get())
            self.current_widget.max_val = float(self.ent_max.get())
            self.current_widget.step = float(self.ent_step.get())
            self.on_property_changed()
        except ValueError:
            pass

    def _on_bg_change(self) -> None:
        if self._updating or not self.current_widget: return
        col = self.ent_bg.get().strip()
        if col.startswith("#") and len(col) in (4, 7):
            self.current_widget.bg_color = col
            self.on_property_changed()

    def _apply_color_chip(self, hex_color: str) -> None:
        if not self.current_widget: return
        self.current_widget.bg_color = hex_color
        self._set_text(self.ent_bg, hex_color)
        self.on_property_changed()

    def _on_text_color_change(self) -> None:
        if self._updating or not self.current_widget: return
        col = self.ent_text_color.get().strip()
        if col.startswith("#"):
            self.current_widget.text_color = col
            self.on_property_changed()

    def _on_radius_change(self) -> None:
        if self._updating or not self.current_widget: return
        try:
            self.current_widget.corner_radius = max(0, int(self.ent_radius.get()))
            self.on_property_changed()
        except ValueError:
            pass

    def _on_font_size_change(self) -> None:
        if self._updating or not self.current_widget: return
        try:
            self.current_widget.font_size = max(6, int(self.ent_fsize.get()))
            self.on_property_changed()
        except ValueError:
            pass

    def _on_event_change(self) -> None:
        if self._updating or not self.current_widget: return
        self.current_widget.event_handler = self.ent_event.get().strip()
        self.on_property_changed()

    def _on_value_change(self) -> None:
        if self._updating or not self.current_widget: return
        val_str = self.ent_value.get().strip()
        if val_str.lower() in ("true", "false"):
            self.current_widget.value = (val_str.lower() == "true")
        else:
            try:
                self.current_widget.value = float(val_str)
            except ValueError:
                self.current_widget.value = val_str
        self.on_property_changed()
