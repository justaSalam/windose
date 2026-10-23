"""
Interactive WYSIWYG Canvas for Windose Designer / ELF Studio.
Implements authentic Windows 7 Aero Glass styling matching Windose OS:
- Aero Glass titlebar with gloss sheen reflection & curved gloss bands
- Windows 7 caption buttons: minimize, maximize, and crimson close button
- Signature Windows 7 Harmony Blue desktop canvas backdrop (#3a6ea5)
- Soft multi-layered Gaussian window drop shadow
- Windows 7 Aero native control rendering (gradients, etched separators, Segoe UI typography)
- Interactive simulation mode with live event dispatches
"""

import math
import tkinter as tk
from typing import Optional, Callable, Dict, Any, List, Tuple
from core.models import Project, Widget, WidgetType
from core.theme import WINDOSE_AERO


class CanvasView(tk.Canvas):
    """Custom canvas that renders the GUI layout inside a genuine Windose Aero window mockup."""

    HANDLE_SIZE = 8
    HANDLE_NAMES = ["nw", "n", "ne", "e", "se", "s", "sw", "w"]

    def __init__(self, master, project: Project, on_widget_selected: Optional[Callable[[Optional[str]], None]] = None,
                 on_widget_modified: Optional[Callable[[], None]] = None,
                 on_sim_event: Optional[Callable[[str, str], None]] = None,
                 on_widget_double_click: Optional[Callable[[str], None]] = None, **kwargs):
        super().__init__(
            master,
            bg=WINDOSE_AERO["desktop_bg"],  # Signature Windows 7 Desktop Blue (#3a6ea5)
            highlightthickness=0,
            cursor="arrow",
            **kwargs
        )
        self.project = project
        self.on_widget_selected = on_widget_selected
        self.on_widget_modified = on_widget_modified
        self.on_sim_event = on_sim_event
        self.on_widget_double_click = on_widget_double_click

        self.simulation_mode = False
        self.zoom = 1.0
        # Breathing room for Aero titlebar & drop shadows
        self.offset_x = 70
        self.offset_y = 75

        # Interaction tracking
        self._drag_mode = None  # None, "move", "resize", "pan", "marquee"
        self._active_handle = None
        self._drag_start_x = 0
        self._drag_start_y = 0
        self._initial_bounds = {}  # {wid: (x, y, w, h)}
        self._active_input_wid = None
        self._active_input_cursor = 0
        self._current_cursor = "arrow"
        self._redraw_scheduled = False

        # Marquee selection & Pan tracking
        self._marquee_start: Optional[Tuple[int, int, int, int]] = None
        self._marquee_box: Optional[Tuple[int, int, int, int]] = None
        self._pan_start: Optional[Tuple[int, int]] = None
        self._pan_moved: bool = False

        # Simulation Mock Dialog Modal state
        self._sim_dialog: Optional[Dict[str, Any]] = None
        self._sim_dialog_close_rect = (0, 0, 0, 0)
        self._sim_dialog_btn_rects: List[Tuple[str, Tuple[int, int, int, int]]] = []

        # Caption button geometry cache for hover & hit-testing
        self._close_rect = (0, 0, 0, 0)
        self._max_rect = (0, 0, 0, 0)
        self._min_rect = (0, 0, 0, 0)
        self._hovered_caption_btn = None  # None, "close", "max", "min"

        # Alignment guide lines
        self._guidelines: List[Tuple[int, int, int, int]] = []

        # Bind events
        self.bind("<Button-1>", self._on_mouse_down)
        self.bind("<Double-Button-1>", self._on_mouse_double_click)
        self.bind("<B1-Motion>", self._on_mouse_drag)
        self.bind("<ButtonRelease-1>", self._on_mouse_up)
        self.bind("<Motion>", self._on_mouse_hover)
        self.bind("<KeyPress>", self._on_key_press)
        self.bind("<MouseWheel>", self._on_mouse_wheel)

        # Right-Click & Middle-Click navigation & context menu
        self.bind("<Button-3>", self._on_right_mouse_down)
        self.bind("<B3-Motion>", self._on_right_mouse_drag)
        self.bind("<ButtonRelease-3>", self._on_right_mouse_up)
        self.bind("<Button-2>", self._on_middle_mouse_down)
        self.bind("<B2-Motion>", self._on_middle_mouse_drag)
        self.bind("<ButtonRelease-2>", self._on_middle_mouse_up)

        self.redraw()

    def set_simulation_mode(self, enabled: bool) -> None:
        self.simulation_mode = enabled
        self._active_input_wid = None
        self.redraw()

    def set_zoom(self, zoom: float) -> None:
        self.zoom = max(0.25, min(3.0, zoom))
        self.redraw()

    def zoom_in(self) -> None:
        self.set_zoom(self.zoom + 0.15)

    def zoom_out(self) -> None:
        self.set_zoom(self.zoom - 0.15)

    def zoom_reset(self) -> None:
        self.set_zoom(1.0)

    def world_to_screen(self, wx: int, wy: int) -> Tuple[int, int]:
        sx = int(self.offset_x + wx * self.zoom)
        sy = int(self.offset_y + wy * self.zoom)
        return sx, sy

    def screen_to_world(self, sx: int, sy: int) -> Tuple[int, int]:
        wx = int((sx - self.offset_x) / self.zoom)
        wy = int((sy - self.offset_y) / self.zoom)
        return wx, wy

    # -------------------------------------------------------------
    # High-Performance Drawing Pipeline
    # -------------------------------------------------------------
    def request_redraw(self) -> None:
        """Batches redraws to display refresh cycle to eliminate event queue backlog."""
        if not self._redraw_scheduled:
            self._redraw_scheduled = True
            self.after_idle(self._flush_redraw)

    def _flush_redraw(self) -> None:
        self._redraw_scheduled = False
        self.redraw()

    def redraw(self) -> None:
        self.delete("all")
        self._draw_canvas_viewport()
        self._draw_widgets()
        if not self.simulation_mode:
            self._draw_selection_and_handles()
            self._draw_guidelines()
            if self._marquee_box:
                self._draw_marquee_box()
        else:
            if self._sim_dialog:
                self._draw_sim_dialog()

    # -------------------------------------------------------------
    # Authentic Windows 7 Aero Window Frame & Bezel
    # -------------------------------------------------------------
    def _draw_canvas_viewport(self) -> None:
        """Renders the authentic Windows 7 Aero Glass window frame around the designed client area."""
        # Client area world coordinates (0, 0) to (canvas_width, canvas_height)
        x1, y1 = self.world_to_screen(0, 0)
        x2, y2 = self.world_to_screen(self.project.canvas_width, self.project.canvas_height)

        title_h = max(24, int(28 * self.zoom))
        border_w = max(3, int(4 * self.zoom))

        # Overall window outer frame bounds
        fx1 = x1 - border_w
        fy1 = y1 - title_h
        fx2 = x2 + border_w
        fy2 = y2 + border_w

        # 1. Desktop background signature watermark (Windose 7 Edition)
        self.create_text(
            18, 16,
            text="🪐 WINDOSE 7 AERO DESKTOP  |  ELF RUNTIME KERNEL",
            anchor="nw",
            fill="#5786be",
            font=("Segoe UI", 9, "bold")
        )

        # 2. Window Drop Shadow (Multi-layered soft shadow onto the desktop)
        shadow_layers = [
            (fx1 + 10, fy1 + 10, fx2 + 10, fy2 + 10, "#1a324d"),
            (fx1 + 6, fy1 + 6, fx2 + 6, fy2 + 6, "#224164"),
            (fx1 + 3, fy1 + 3, fx2 + 3, fy2 + 3, "#2b507b"),
        ]
        for sx1, sy1, sx2, sy2, scolor in shadow_layers:
            self.create_rectangle(sx1, sy1, sx2, sy2, fill=scolor, outline="", tags="viewport_shadow")

        # 3. Outer Aero Frame (Glass border on left, right, bottom)
        # Left glass band
        self.create_rectangle(fx1, y1, x1, y2, fill=WINDOSE_AERO["title_active"], outline="", tags="viewport")
        # Right glass band
        self.create_rectangle(x2, y1, fx2, y2, fill=WINDOSE_AERO["title_active"], outline="", tags="viewport")
        # Bottom glass band
        self.create_rectangle(fx1, y2, fx2, fy2, fill=WINDOSE_AERO["title_active"], outline="", tags="viewport")

        # 4. Aero Glass Titlebar
        # Main background gradient (deep glass blue)
        mid_title = fy1 + title_h // 2
        self.create_rectangle(fx1, fy1, fx2, mid_title, fill="#3464a8", outline="", tags="viewport")
        self.create_rectangle(fx1, mid_title, fx2, y1, fill="#23497e", outline="", tags="viewport")

        # Windows 7 Curved Gloss Sheen Reflection (top half highlight)
        gloss_h = max(4, int(title_h * 0.45))
        self.create_rectangle(fx1 + 1, fy1 + 1, fx2 - 1, fy1 + gloss_h, fill="#4e84cb", outline="", tags="viewport")
        # 1px top bright gloss sheen line
        self.create_line(fx1 + 2, fy1 + 1, fx2 - 2, fy1 + 1, fill="#9bc3f7", width=1, tags="viewport")

        # 5. Window Outer Bezel Outline
        self.create_rectangle(
            fx1, fy1, fx2, fy2,
            outline=WINDOSE_AERO["window_border"],
            width=1,
            tags="viewport"
        )

        # 6. Windows 7 Caption Buttons (Top-Right)
        close_w = max(38, int(47 * self.zoom * 0.88))
        norm_w = max(24, int(29 * self.zoom * 0.88))
        btn_h = title_h - 2

        # Close button (Crimson Red Glow)
        cb_x2 = fx2 - 1
        cb_x1 = cb_x2 - close_w
        cb_y1 = fy1 + 1
        cb_y2 = cb_y1 + btn_h
        self._close_rect = (cb_x1, cb_y1, cb_x2, cb_y2)

        is_close_hov = (self._hovered_caption_btn == "close")
        c_top = WINDOSE_AERO["caption_close_hover"] if is_close_hov else WINDOSE_AERO["caption_close"]
        c_bot = "#960e18" if is_close_hov else "#88131b"
        c_mid = (cb_y1 + cb_y2) // 2

        self.create_rectangle(cb_x1, cb_y1, cb_x2, c_mid, fill=c_top, outline="", tags="viewport_btn")
        self.create_rectangle(cb_x1, c_mid, cb_x2, cb_y2, fill=c_bot, outline="", tags="viewport_btn")
        self.create_line(cb_x1 + 1, cb_y1 + 1, cb_x2 - 1, cb_y1 + 1, fill="#fca5a5", width=1, tags="viewport_btn")
        self.create_rectangle(cb_x1, cb_y1, cb_x2, cb_y2, outline="#780b13", width=1, tags="viewport_btn")
        self.create_text(
            (cb_x1 + cb_x2) // 2, (cb_y1 + cb_y2) // 2,
            text="✕", fill="#ffffff",
            font=("Segoe UI", max(8, int(9 * self.zoom)), "bold"),
            tags="viewport_btn"
        )

        # Maximize button
        mb_x2 = cb_x1 - 1
        mb_x1 = mb_x2 - norm_w
        mb_y1 = fy1 + 1
        mb_y2 = mb_y1 + btn_h
        self._max_rect = (mb_x1, mb_y1, mb_x2, mb_y2)

        is_max_hov = (self._hovered_caption_btn == "max")
        m_bg = "#487bb8" if is_max_hov else "#2b5388"
        self.create_rectangle(mb_x1, mb_y1, mb_x2, mb_y2, fill=m_bg, outline="#1d3d66", width=1, tags="viewport_btn")
        self.create_text(
            (mb_x1 + mb_x2) // 2, (mb_y1 + mb_y2) // 2,
            text="◻", fill="#ffffff",
            font=("Segoe UI", max(8, int(9 * self.zoom))),
            tags="viewport_btn"
        )

        # Minimize button
        nb_x2 = mb_x1 - 1
        nb_x1 = nb_x2 - norm_w
        nb_y1 = fy1 + 1
        nb_y2 = nb_y1 + btn_h
        self._min_rect = (nb_x1, nb_y1, nb_x2, nb_y2)

        is_min_hov = (self._hovered_caption_btn == "min")
        n_bg = "#487bb8" if is_min_hov else "#2b5388"
        self.create_rectangle(nb_x1, nb_y1, nb_x2, nb_y2, fill=n_bg, outline="#1d3d66", width=1, tags="viewport_btn")
        self.create_text(
            (nb_x1 + nb_x2) // 2, (nb_y1 + nb_y2) // 2 - 1,
            text="—", fill="#ffffff",
            font=("Segoe UI", max(8, int(9 * self.zoom)), "bold"),
            tags="viewport_btn"
        )

        # 7. Window Title & App Icon
        icon_x = fx1 + 12
        icon_y = (fy1 + y1) // 2
        self.create_text(
            icon_x, icon_y,
            text="🪐", font=("Segoe UI", max(10, int(13 * self.zoom))),
            tags="viewport_title"
        )
        title_tx = icon_x + 16
        # Drop shadow for title text (authentic Win7 glass typography)
        self.create_text(
            title_tx + 1, icon_y + 1,
            text=f"{self.project.name} - Windose OS",
            anchor="w",
            fill="#12253d",
            font=("Segoe UI", max(8, int(10 * self.zoom)), "bold"),
            tags="viewport_title"
        )
        self.create_text(
            title_tx, icon_y,
            text=f"{self.project.name} - Windose OS",
            anchor="w",
            fill="#ffffff",
            font=("Segoe UI", max(8, int(10 * self.zoom)), "bold"),
            tags="viewport_title"
        )

        # 8. Client Area Bezel & Background
        # In Windose System/GUI/Window API/Window.cs:
        # DrawRectangle(Color.FromArgb(180, 195, 215), client.X - 1, client.Y - 1, client.Width + 2, client.Height + 2)
        self.create_rectangle(
            x1 - 1, y1 - 1, x2 + 1, y2 + 1,
            outline="#b4c3d7", fill="", width=1,
            tags="viewport"
        )
        # Client area surface
        self.create_rectangle(
            x1, y1, x2, y2,
            fill=self.project.bg_color or WINDOSE_AERO["window_bg"],
            outline="",
            tags="viewport"
        )

        # 9. Clean Grid Lines (Only visible inside client area when snap_to_grid is enabled)
        if self.project.snap_to_grid and not self.simulation_mode:
            grid_step = int(self.project.grid_size * 2 * self.zoom)
            if grid_step >= 16:
                for gx in range(x1 + grid_step, x2, grid_step):
                    self.create_line(gx, y1, gx, y2, fill="#e2e8f0", width=1, tags="viewport_grid")
                for gy in range(y1 + grid_step, y2, grid_step):
                    self.create_line(x1, gy, x2, gy, fill="#e2e8f0", width=1, tags="viewport_grid")

        # 10. Designer Mode Badge
        mode_tag = "SIMULATION RUNNER" if self.simulation_mode else "DESIGN VIEW"
        tag_color = "#34d399" if self.simulation_mode else "#93c5fd"
        self.create_text(
            fx1, fy1 - 14,
            text=f"● [{mode_tag}] {self.project.canvas_width} x {self.project.canvas_height} px | Grid: {self.project.grid_size}px | Zoom: {int(self.zoom*100)}%",
            anchor="w",
            fill=tag_color,
            font=("Segoe UI", 9, "bold")
        )

    # -------------------------------------------------------------
    # Windows 7 Aero Native Control Drawing Pipeline
    # -------------------------------------------------------------
    def _draw_widgets(self) -> None:
        for w in self.project.widgets:
            if not w.visible:
                continue
            x1, y1 = self.world_to_screen(w.x, w.y)
            x2, y2 = self.world_to_screen(w.x + w.width, w.y + w.height)

            if w.widget_type == WidgetType.PANEL:
                self._draw_panel(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.BUTTON:
                self._draw_button(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.LABEL:
                self._draw_label(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.ENTRY:
                self._draw_entry(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.TEXTAREA:
                self._draw_textarea(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.SLIDER:
                self._draw_slider(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.PROGRESSBAR:
                self._draw_progressbar(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.CHECKBOX:
                self._draw_checkbox(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.SWITCH:
                self._draw_switch(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.RADIO:
                self._draw_radio(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.DROPDOWN:
                self._draw_dropdown(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.LISTBOX:
                self._draw_listbox(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.DATAGRID:
                self._draw_datagrid(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.NUMERIC:
                self._draw_numeric(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.SEPARATOR:
                self._draw_separator(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.TABS:
                self._draw_tabs(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.IMAGE:
                self._draw_image(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.CANVAS:
                self._draw_canvas_widget(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.COLORPICKER:
                self._draw_colorpicker(w, x1, y1, x2, y2)
            elif w.widget_type == WidgetType.STATUSBAR:
                self._draw_statusbar(w, x1, y1, x2, y2)
            else:
                self._draw_generic_box(w, x1, y1, x2, y2)

            # Visual error indicator on canvas if widget is out of bounds
            if not self.simulation_mode:
                is_out = (w.x < 0 or w.y < 0 or (w.x + w.width) > self.project.canvas_width or (w.y + w.height) > self.project.canvas_height)
                if is_out:
                    self.create_text(
                        x2 - 8, y1 + 8, text="⚠️",
                        font=("Segoe UI", max(7, int(9 * self.zoom))),
                        tags=(f"w_{w.id}", "widget", "alert")
                    )

    def _on_mouse_double_click(self, event) -> None:
        """Double clicking a widget in design mode jumps directly to its C code callback."""
        if self.simulation_mode:
            return
        wx, wy = self.screen_to_world(event.x, event.y)
        hit_w = self._find_widget_at(wx, wy)
        if hit_w and self.on_widget_double_click:
            self.on_widget_double_click(hit_w.id)

    def _draw_button(self, w: Widget, x1, y1, x2, y2) -> None:
        """Draws Windows 7 Aero Push Button: vertical gradient (#f6f6f6->#e8e8e8), outline #acacac, top gloss line."""
        is_default_bg = (not w.bg_color or w.bg_color.lower() in ("#f0f0f0", "#ffffff", "default"))
        mid_y = (y1 + y2) // 2

        if is_default_bg:
            # Native Win7 Aero button gradient
            self.create_rectangle(x1, y1, x2, mid_y, fill="#f6f6f6", outline="", tags=(f"w_{w.id}", "widget"))
            self.create_rectangle(x1, mid_y, x2, y2, fill="#e8e8e8", outline="", tags=(f"w_{w.id}", "widget"))
            # Top 1px horizontal white highlight sheen (System/GUI/Components/Button.cs line 127)
            if (x2 - x1) > 4 and (y2 - y1) > 2:
                self.create_line(x1 + 2, y1 + 1, x2 - 2, y1 + 1, fill="#ffffff", width=1, tags=(f"w_{w.id}", "widget"))
            border_col = w.border_color or WINDOSE_AERO["control_border"]
        else:
            # Custom styled button with top gloss
            self.create_rectangle(x1, y1, x2, y2, fill=w.bg_color, outline="", tags=(f"w_{w.id}", "widget"))
            if (x2 - x1) > 4 and (y2 - y1) > 2:
                self.create_line(x1 + 2, y1 + 1, x2 - 2, y1 + 1, fill="#ffffff", width=1, tags=(f"w_{w.id}", "widget"))
            border_col = w.border_color or WINDOSE_AERO["control_border"]

        # Outer rounded outline
        self.create_rectangle(
            x1, y1, x2, y2,
            outline=border_col,
            width=max(1, int(w.border_width * self.zoom)),
            tags=(f"w_{w.id}", "widget")
        )

        fsize = max(8, int(w.font_size * self.zoom))
        text_col = w.text_color or (WINDOSE_AERO["text_dark"] if is_default_bg else "#ffffff")
        self.create_text(
            (x1 + x2) // 2, (y1 + y2) // 2,
            text=w.text,
            fill=text_col,
            font=("Segoe UI", fsize, w.font_weight),
            tags=(f"w_{w.id}", "widget")
        )

    def _draw_entry(self, w: Widget, x1, y1, x2, y2) -> None:
        """Draws Windows 7 Text Entry: pure white, #7f9db9 border, subtle top inset shadow."""
        bg = w.bg_color or WINDOSE_AERO["input_bg"]
        is_active = (self.simulation_mode and self._active_input_wid == w.id)
        border_col = WINDOSE_AERO["highlight"] if is_active else (w.border_color or WINDOSE_AERO["input_border"])

        # Main background
        self.create_rectangle(
            x1, y1, x2, y2,
            fill=bg,
            outline=border_col,
            width=2 if is_active else 1,
            tags=(f"w_{w.id}", "widget")
        )
        # Inner top subtle inset shadow line
        self.create_line(x1 + 1, y1 + 1, x2 - 1, y1 + 1, fill=WINDOSE_AERO["input_shadow"], width=1, tags=(f"w_{w.id}", "widget"))

        fsize = max(8, int(w.font_size * self.zoom))
        display_text = str(w.value) if (w.value is not None and self.simulation_mode) else (w.text or w.placeholder)
        color = (w.text_color or WINDOSE_AERO["text_dark"]) if (w.value or not w.placeholder) else WINDOSE_AERO["text_dim"]

        self.create_text(
            x1 + 8, (y1 + y2) // 2,
            text=display_text,
            anchor="w",
            fill=color,
            font=("Segoe UI", fsize),
            tags=(f"w_{w.id}", "widget")
        )
        # Simulation blinking cursor
        if is_active:
            cw = min(x2 - 10, x1 + 8 + len(str(w.value or "")) * int(7 * self.zoom))
            self.create_line(cw, y1 + 5, cw, y2 - 5, fill=WINDOSE_AERO["highlight_dark"], width=2, tags=(f"w_{w.id}", "widget"))

    def _draw_textarea(self, w: Widget, x1, y1, x2, y2) -> None:
        """Draws Windows 7 Multiline Text Editor with gutter, line numbers, and native scrollbar."""
        bg = w.bg_color or WINDOSE_AERO["input_bg"]
        self.create_rectangle(
            x1, y1, x2, y2,
            fill=bg,
            outline=w.border_color or WINDOSE_AERO["input_border"],
            width=max(1, int(w.border_width * self.zoom)),
            tags=(f"w_{w.id}", "widget")
        )
        # Line number gutter
        gw = int(26 * self.zoom)
        self.create_rectangle(x1, y1, x1 + gw, y2, fill="#f0f2f5", outline="", tags=(f"w_{w.id}", "widget"))
        self.create_line(x1 + gw, y1, x1 + gw, y2, fill="#d1d5db", width=1, tags=(f"w_{w.id}", "widget"))

        content = str(w.value) if (w.value is not None and self.simulation_mode) else w.text
        lines = content.split("\n")
        fsize = max(7, int(w.font_size * self.zoom * 0.9))
        line_h = max(12, int(16 * self.zoom))

        for i, line in enumerate(lines[:14]):
            ly = y1 + 6 + i * line_h
            if ly + line_h > y2:
                break
            # Gutter number
            self.create_text(x1 + gw - 5, ly, text=str(i + 1), anchor="e", fill="#64748b", font=("Consolas", fsize), tags=(f"w_{w.id}", "widget"))
            # Line text
            self.create_text(x1 + gw + 6, ly, text=line, anchor="nw", fill=w.text_color or WINDOSE_AERO["text_dark"], font=("Consolas", fsize), tags=(f"w_{w.id}", "widget"))

        # Windows 7 scrollbar on right
        sb_w = int(14 * self.zoom)
        self.create_rectangle(x2 - sb_w, y1, x2, y2, fill="#f1f1f1", outline="", tags=(f"w_{w.id}", "widget"))
        self.create_line(x2 - sb_w, y1, x2 - sb_w, y2, fill="#e5e7eb", width=1, tags=(f"w_{w.id}", "widget"))
        # Scrollbar thumb
        self.create_rectangle(x2 - sb_w + 2, y1 + 6, x2 - 2, y1 + 42, fill="#c1c1c1", outline="#a8a8a8", width=1, tags=(f"w_{w.id}", "widget"))

    def _draw_label(self, w: Widget, x1, y1, x2, y2) -> None:
        fsize = max(8, int(w.font_size * self.zoom))
        anchor = "w" if w.alignment == "left" else ("e" if w.alignment == "right" else "center")
        tx = x1 + 4 if w.alignment == "left" else (x2 - 4 if w.alignment == "right" else (x1 + x2) // 2)
        ty = (y1 + y2) // 2
        self.create_text(
            tx, ty,
            text=w.text,
            anchor=anchor,
            fill=w.text_color or WINDOSE_AERO["text_dark"],
            font=("Segoe UI", fsize, w.font_weight),
            tags=(f"w_{w.id}", "widget")
        )

    def _draw_slider(self, w: Widget, x1, y1, x2, y2) -> None:
        """Draws Windows 7 Slider: inset groove, Win7 blue active track, metallic thumb."""
        cy = (y1 + y2) // 2 + 4
        # Inset groove track
        self.create_line(x1 + 4, cy, x2 - 4, cy, fill="#d1d5db", width=int(5 * self.zoom), capstyle="round")
        val_range = max(1.0, w.max_val - w.min_val)
        current = float(w.value if w.value is not None else 50.0)
        norm = max(0.0, min(1.0, (current - w.min_val) / val_range))
        thumb_x = int(x1 + 4 + norm * ((x2 - 4) - (x1 + 4)))
        # Active progress track (Win7 blue)
        self.create_line(x1 + 4, cy, thumb_x, cy, fill=WINDOSE_AERO["highlight"], width=int(5 * self.zoom), capstyle="round")

        # Metallic Win7 thumb
        tw = int(6 * self.zoom)
        th = int(10 * self.zoom)
        self.create_rectangle(
            thumb_x - tw, cy - th, thumb_x + tw, cy + th,
            fill="#f8fafc", outline="#94a3b8", width=1,
            tags=(f"w_{w.id}", "widget")
        )
        self.create_line(thumb_x - 2, cy - 4, thumb_x - 2, cy + 4, fill="#cbd5e1", tags=(f"w_{w.id}", "widget"))
        self.create_line(thumb_x + 2, cy - 4, thumb_x + 2, cy + 4, fill="#cbd5e1", tags=(f"w_{w.id}", "widget"))

        self.create_text(
            x1 + 4, y1 + 2,
            text=f"{w.text}: {int(current)}",
            anchor="nw",
            fill=WINDOSE_AERO["text_muted"],
            font=("Segoe UI", max(7, int(9 * self.zoom))),
            tags=(f"w_{w.id}", "widget")
        )

    def _draw_progressbar(self, w: Widget, x1, y1, x2, y2) -> None:
        """Draws Windows 7 Green Gloss Progressbar."""
        self.create_rectangle(
            x1, y1, x2, y2,
            fill="#e2e8f0",
            outline=w.border_color or "#bcbfc4",
            width=1,
            tags=(f"w_{w.id}", "widget")
        )
        val = max(0.0, min(100.0, float(w.value if w.value is not None else 50.0)))
        fill_w = int((x2 - x1) * (val / 100.0))
        if fill_w > 0:
            mid_y = (y1 + y2) // 2
            # Gloss top half
            self.create_rectangle(x1 + 1, y1 + 1, x1 + fill_w, mid_y, fill="#34d399", outline="", tags=(f"w_{w.id}", "widget"))
            # Vibrant emerald bottom half
            self.create_rectangle(x1 + 1, mid_y, x1 + fill_w, y2 - 1, fill=WINDOSE_AERO["success_green_dark"], outline="", tags=(f"w_{w.id}", "widget"))
            # Gloss highlight divider
            self.create_line(x1 + 1, mid_y, x1 + fill_w, mid_y, fill="#6ee7b7", width=1, tags=(f"w_{w.id}", "widget"))

        self.create_text(
            (x1 + x2) // 2, (y1 + y2) // 2,
            text=f"{int(val)}%",
            fill="#ffffff" if val > 20 else WINDOSE_AERO["text_dark"],
            font=("Segoe UI", max(8, int(10 * self.zoom)), "bold"),
            tags=(f"w_{w.id}", "widget")
        )

    def _draw_checkbox(self, w: Widget, x1, y1, x2, y2) -> None:
        cy = (y1 + y2) // 2
        box_sz = int(14 * self.zoom)
        bx1, by1 = x1 + 4, cy - box_sz // 2
        bx2, by2 = bx1 + box_sz, by1 + box_sz
        is_chk = bool(w.value)
        # White box with Win7 border
        self.create_rectangle(
            bx1, by1, bx2, by2,
            fill="#ffffff",
            outline=WINDOSE_AERO["highlight"] if is_chk else WINDOSE_AERO["input_border"],
            width=2 if is_chk else 1,
            tags=(f"w_{w.id}", "widget")
        )
        if is_chk:
            self.create_text(
                (bx1 + bx2) // 2, (by1 + by2) // 2,
                text="✓", fill=WINDOSE_AERO["highlight_dark"],
                font=("Segoe UI", int(10 * self.zoom), "bold"),
                tags=(f"w_{w.id}", "widget")
            )
        self.create_text(
            bx2 + 8, cy,
            text=w.text,
            anchor="w",
            fill=w.text_color or WINDOSE_AERO["text_dark"],
            font=("Segoe UI", max(8, int(w.font_size * self.zoom))),
            tags=(f"w_{w.id}", "widget")
        )

    def _draw_switch(self, w: Widget, x1, y1, x2, y2) -> None:
        cy = (y1 + y2) // 2
        sw_w = int(38 * self.zoom)
        sw_h = int(18 * self.zoom)
        sx1, sy1 = x1 + 4, cy - sw_h // 2
        sx2, sy2 = sx1 + sw_w, sy1 + sw_h
        is_on = bool(w.value)
        self.create_rectangle(
            sx1, sy1, sx2, sy2,
            fill=WINDOSE_AERO["success_green"] if is_on else "#cbd5e1",
            outline="",
            tags=(f"w_{w.id}", "widget")
        )
        knob_r = sw_h // 2 - 2
        knob_x = sx2 - 2 - knob_r if is_on else sx1 + 2 + knob_r
        self.create_oval(
            knob_x - knob_r, cy - knob_r, knob_x + knob_r, cy + knob_r,
            fill="#ffffff", outline="#94a3b8", width=1,
            tags=(f"w_{w.id}", "widget")
        )
        self.create_text(
            sx2 + 8, cy,
            text=w.text,
            anchor="w",
            fill=w.text_color or WINDOSE_AERO["text_dark"],
            font=("Segoe UI", max(8, int(w.font_size * self.zoom))),
            tags=(f"w_{w.id}", "widget")
        )

    def _draw_radio(self, w: Widget, x1, y1, x2, y2) -> None:
        cy = (y1 + y2) // 2
        r_outer = int(7 * self.zoom)
        cx = x1 + 6 + r_outer
        is_sel = bool(w.value)
        # White circle with border
        self.create_oval(
            cx - r_outer, cy - r_outer, cx + r_outer, cy + r_outer,
            fill="#ffffff",
            outline=WINDOSE_AERO["highlight"] if is_sel else WINDOSE_AERO["input_border"],
            width=2 if is_sel else 1,
            tags=(f"w_{w.id}", "widget")
        )
        if is_sel:
            r_inner = max(3, r_outer - 4)
            self.create_oval(
                cx - r_inner, cy - r_inner, cx + r_inner, cy + r_inner,
                fill=WINDOSE_AERO["highlight_dark"], outline="",
                tags=(f"w_{w.id}", "widget")
            )
        self.create_text(
            cx + r_outer + 8, cy,
            text=w.text,
            anchor="w",
            fill=w.text_color or WINDOSE_AERO["text_dark"],
            font=("Segoe UI", max(8, int(w.font_size * self.zoom))),
            tags=(f"w_{w.id}", "widget")
        )

    def _draw_dropdown(self, w: Widget, x1, y1, x2, y2) -> None:
        """Draws Windows 7 Combobox with white text entry and gradient split button."""
        # Main text box
        self.create_rectangle(
            x1, y1, x2, y2,
            fill=w.bg_color or WINDOSE_AERO["input_bg"],
            outline=w.border_color or WINDOSE_AERO["input_border"],
            width=1,
            tags=(f"w_{w.id}", "widget")
        )
        btn_w = max(18, int(22 * self.zoom))
        bx1 = x2 - btn_w
        # Right split button with Aero gradient
        mid_y = (y1 + y2) // 2
        self.create_rectangle(bx1, y1, x2, mid_y, fill="#f6f6f6", outline="", tags=(f"w_{w.id}", "widget"))
        self.create_rectangle(bx1, mid_y, x2, y2, fill="#e8e8e8", outline="", tags=(f"w_{w.id}", "widget"))
        self.create_line(bx1, y1, bx1, y2, fill=WINDOSE_AERO["control_border"], width=1, tags=(f"w_{w.id}", "widget"))
        self.create_text(
            (bx1 + x2) // 2, mid_y,
            text="▼", fill=WINDOSE_AERO["text_muted"],
            font=("Segoe UI", max(6, int(7 * self.zoom))),
            tags=(f"w_{w.id}", "widget")
        )

        sel_text = str(w.value) if w.value else (w.options[0] if w.options else "Select Option")
        self.create_text(
            x1 + 8, mid_y,
            text=sel_text,
            anchor="w",
            fill=w.text_color or WINDOSE_AERO["text_dark"],
            font=("Segoe UI", max(8, int(w.font_size * self.zoom))),
            tags=(f"w_{w.id}", "widget")
        )

    def _draw_listbox(self, w: Widget, x1, y1, x2, y2) -> None:
        """Draws Windows 7 Listbox with gradient header, alternating rows, and blue selection."""
        self.create_rectangle(
            x1, y1, x2, y2,
            fill=w.bg_color or "#ffffff",
            outline=w.border_color or WINDOSE_AERO["input_border"],
            width=1,
            tags=(f"w_{w.id}", "widget")
        )
        # Header bar with gradient
        hh = max(18, int(22 * self.zoom))
        mid_h = y1 + hh // 2
        self.create_rectangle(x1, y1, x2, mid_h, fill="#f0f2f5", outline="", tags=(f"w_{w.id}", "widget"))
        self.create_rectangle(x1, mid_h, x2, y1 + hh, fill="#e4e7eb", outline="", tags=(f"w_{w.id}", "widget"))
        self.create_line(x1, y1 + hh, x2, y1 + hh, fill="#d1d5db", width=1, tags=(f"w_{w.id}", "widget"))

        hdr_text = w.text or "List Items"
        self.create_text(x1 + 8, mid_h, text=hdr_text, anchor="w", fill="#475569", font=("Segoe UI", max(7, int(9 * self.zoom)), "bold"), tags=(f"w_{w.id}", "widget"))

        # Items
        items = w.items or ["Item 1", "Item 2", "Item 3", "Item 4"]
        row_h = max(16, int(20 * self.zoom))
        for i, item in enumerate(items[:10]):
            ry1 = y1 + hh + i * row_h
            ry2 = ry1 + row_h
            if ry2 > y2:
                break
            is_cur = (w.value == item)
            row_bg = WINDOSE_AERO["highlight"] if is_cur else ("#f8fafc" if i % 2 == 1 else "#ffffff")
            self.create_rectangle(x1 + 1, ry1, x2 - 1, ry2, fill=row_bg, outline="", tags=(f"w_{w.id}", "widget"))
            text_c = "#ffffff" if is_cur else WINDOSE_AERO["text_dark"]
            self.create_text(x1 + 10, (ry1 + ry2) // 2, text=item, anchor="w", fill=text_c, font=("Segoe UI", max(7, int(9 * self.zoom))), tags=(f"w_{w.id}", "widget"))

    def _draw_datagrid(self, w: Widget, x1, y1, x2, y2) -> None:
        """Draws Windows 7 DataGrid / Explorer Table."""
        self.create_rectangle(
            x1, y1, x2, y2,
            fill=w.bg_color or "#ffffff",
            outline=w.border_color or "#bcbfc4",
            width=1,
            tags=(f"w_{w.id}", "widget")
        )
        cols = w.columns or ["ID", "Name", "Status", "Value"]
        col_w = max(30, (x2 - x1) // max(1, len(cols)))
        hh = max(18, int(24 * self.zoom))

        # Header row gradient
        mid_h = y1 + hh // 2
        self.create_rectangle(x1, y1, x2, mid_h, fill="#f0f2f5", outline="", tags=(f"w_{w.id}", "widget"))
        self.create_rectangle(x1, mid_h, x2, y1 + hh, fill="#e2e6eb", outline="", tags=(f"w_{w.id}", "widget"))
        self.create_line(x1, y1 + hh, x2, y1 + hh, fill="#d1d5db", width=1, tags=(f"w_{w.id}", "widget"))

        for cidx, col in enumerate(cols):
            cx1 = x1 + cidx * col_w
            self.create_text(cx1 + 8, mid_h, text=col, anchor="w", fill="#334155", font=("Segoe UI", max(7, int(9 * self.zoom)), "bold"), tags=(f"w_{w.id}", "widget"))
            if cidx > 0:
                self.create_line(cx1, y1, cx1, y2, fill="#e5e7eb", width=1, tags=(f"w_{w.id}", "widget"))

        # Sample rows
        row_h = max(16, int(20 * self.zoom))
        for r in range(8):
            ry1 = y1 + hh + r * row_h
            ry2 = ry1 + row_h
            if ry2 > y2:
                break
            # Row 0 highlighted in Explorer light blue (#e5f3ff)
            is_sel = (r == 0)
            bg = WINDOSE_AERO["control_highlight"] if is_sel else ("#f9fafb" if r % 2 == 1 else "#ffffff")
            self.create_rectangle(x1 + 1, ry1, x2 - 1, ry2, fill=bg, outline="", tags=(f"w_{w.id}", "widget"))
            if is_sel:
                self.create_rectangle(x1 + 1, ry1, x2 - 1, ry2, outline=WINDOSE_AERO["highlight"], width=1, tags=(f"w_{w.id}", "widget"))
            self.create_line(x1, ry2, x2, ry2, fill="#e5e7eb", width=1, tags=(f"w_{w.id}", "widget"))
            for cidx, col in enumerate(cols):
                cx1 = x1 + cidx * col_w
                val = f"{col}_{r+1}" if cidx > 0 else f"#{100+r}"
                self.create_text(cx1 + 8, (ry1 + ry2) // 2, text=val, anchor="w", fill=WINDOSE_AERO["text_dark"], font=("Segoe UI", max(7, int(8 * self.zoom))), tags=(f"w_{w.id}", "widget"))

    def _draw_numeric(self, w: Widget, x1, y1, x2, y2) -> None:
        """Draws Windows 7 Numeric Spinner."""
        self.create_rectangle(
            x1, y1, x2, y2,
            fill=w.bg_color or "#ffffff",
            outline=w.border_color or WINDOSE_AERO["input_border"],
            width=1,
            tags=(f"w_{w.id}", "widget")
        )
        btn_w = max(16, int(20 * self.zoom))
        div_x = x2 - btn_w
        self.create_line(div_x, y1, div_x, y2, fill=WINDOSE_AERO["control_border"], width=1, tags=(f"w_{w.id}", "widget"))
        mid_y = (y1 + y2) // 2
        self.create_line(div_x, mid_y, x2, mid_y, fill=WINDOSE_AERO["control_border"], width=1, tags=(f"w_{w.id}", "widget"))

        # Chevrons
        self.create_text(div_x + btn_w // 2, (y1 + mid_y) // 2, text="▲", fill=WINDOSE_AERO["text_muted"], font=("Segoe UI", max(6, int(7 * self.zoom))), tags=(f"w_{w.id}", "widget"))
        self.create_text(div_x + btn_w // 2, (mid_y + y2) // 2, text="▼", fill=WINDOSE_AERO["text_muted"], font=("Segoe UI", max(6, int(7 * self.zoom))), tags=(f"w_{w.id}", "widget"))

        val_str = str(int(w.value) if isinstance(w.value, (int, float)) and float(w.value).is_integer() else w.value or 0)
        self.create_text(
            x1 + 8, mid_y,
            text=val_str,
            anchor="w",
            fill=w.text_color or WINDOSE_AERO["text_dark"],
            font=("Segoe UI", max(8, int(w.font_size * self.zoom))),
            tags=(f"w_{w.id}", "widget")
        )

    def _draw_separator(self, w: Widget, x1, y1, x2, y2) -> None:
        """Draws classic Windows 3D etched groove (top line #d1d5db, bottom line #ffffff)."""
        if w.orientation == "vertical":
            mid_x = (x1 + x2) // 2
            self.create_line(mid_x, y1, mid_x, y2, fill="#d1d5db", width=1, tags=(f"w_{w.id}", "widget"))
            self.create_line(mid_x + 1, y1, mid_x + 1, y2, fill="#ffffff", width=1, tags=(f"w_{w.id}", "widget"))
        else:
            mid_y = (y1 + y2) // 2
            self.create_line(x1, mid_y, x2, mid_y, fill="#d1d5db", width=1, tags=(f"w_{w.id}", "widget"))
            self.create_line(x1, mid_y + 1, x2, mid_y + 1, fill="#ffffff", width=1, tags=(f"w_{w.id}", "widget"))

    def _draw_tabs(self, w: Widget, x1, y1, x2, y2) -> None:
        """Draws Windows 7 Tab Strip: active tab connected seamlessly with body."""
        tab_h = max(20, int(24 * self.zoom))
        pages = w.items or ["General", "Security", "Advanced"]
        tab_w = max(45, (x2 - x1) // max(1, len(pages)))

        # Tab buttons
        for i, page in enumerate(pages):
            tx1 = x1 + i * tab_w
            tx2 = tx1 + tab_w
            is_active = (w.value == page or (not w.value and i == 0))
            bg = WINDOSE_AERO["window_bg"] if is_active else "#e2e6ea"
            self.create_rectangle(tx1, y1, tx2, y1 + tab_h, fill=bg, outline="#bcbfc4", width=1, tags=(f"w_{w.id}", "widget"))
            if is_active:
                # Top vibrant blue active accent stripe
                self.create_line(tx1, y1, tx2, y1, fill=WINDOSE_AERO["highlight"], width=3, tags=(f"w_{w.id}", "widget"))
                # Erase bottom border to blend into client body
                self.create_line(tx1 + 1, y1 + tab_h, tx2 - 1, y1 + tab_h, fill=bg, width=2, tags=(f"w_{w.id}", "widget"))
            self.create_text((tx1 + tx2) // 2, y1 + tab_h // 2, text=page, fill=WINDOSE_AERO["text_dark"] if is_active else WINDOSE_AERO["text_muted"], font=("Segoe UI", max(7, int(9 * self.zoom)), "bold" if is_active else "normal"), tags=(f"w_{w.id}", "widget"))

        # Body frame below tabs
        self.create_rectangle(
            x1, y1 + tab_h, x2, y2,
            fill=WINDOSE_AERO["window_bg"], outline="#bcbfc4", width=1,
            tags=(f"w_{w.id}", "widget")
        )
        self.create_text(
            (x1 + x2) // 2, (y1 + tab_h + y2) // 2,
            text=f"[{w.value or pages[0]} Properties Container]",
            fill=WINDOSE_AERO["text_muted"],
            font=("Segoe UI", max(8, int(10 * self.zoom))),
            tags=(f"w_{w.id}", "widget")
        )

    def _draw_panel(self, w: Widget, x1, y1, x2, y2) -> None:
        """Draws Windows Card / GroupBox container."""
        self.create_rectangle(
            x1, y1, x2, y2,
            fill=w.bg_color or "#ffffff",
            outline=w.border_color or "#d1d5db",
            width=max(1, int(w.border_width * self.zoom)),
            tags=(f"w_{w.id}", "widget")
        )

    def _draw_statusbar(self, w: Widget, x1, y1, x2, y2) -> None:
        """Draws Windows 7 Statusbar: #f0f0f0, top divider, sunken segment, and bottom-right resize gripper."""
        self.create_rectangle(
            x1, y1, x2, y2,
            fill=w.bg_color or WINDOSE_AERO["control_face"],
            outline="",
            tags=(f"w_{w.id}", "widget")
        )
        self.create_line(x1, y1, x2, y1, fill="#d1d5db", width=1, tags=(f"w_{w.id}", "widget"))

        # Sunken 3D segment box
        seg_x2 = min(x2 - 30, x1 + 320)
        self.create_rectangle(x1 + 4, y1 + 3, seg_x2, y2 - 3, fill="", outline="#b0b0b0", width=1, tags=(f"w_{w.id}", "widget"))
        self.create_line(x1 + 5, y2 - 2, seg_x2 + 1, y2 - 2, fill="#ffffff", width=1, tags=(f"w_{w.id}", "widget"))
        self.create_line(seg_x2 + 1, y1 + 4, seg_x2 + 1, y2 - 2, fill="#ffffff", width=1, tags=(f"w_{w.id}", "widget"))

        self.create_text(
            x1 + 10, (y1 + y2) // 2,
            text=w.text,
            anchor="w",
            fill=w.text_color or "#334155",
            font=("Segoe UI", max(7, int(w.font_size * self.zoom))),
            tags=(f"w_{w.id}", "widget")
        )

        # Bottom-right resize grip dots (classic Windows ⁝⁝ gripper)
        gx, gy = x2 - 12, y2 - 12
        for dot_r in range(3):
            for dot_c in range(dot_r + 1):
                px = gx + dot_r * 3
                py = gy + (2 - dot_c) * 3
                self.create_rectangle(px, py, px + 1, py + 1, fill="#888888", outline="", tags=(f"w_{w.id}", "widget"))
                self.create_rectangle(px + 1, py + 1, px + 2, py + 2, fill="#ffffff", outline="", tags=(f"w_{w.id}", "widget"))

    def _draw_image(self, w: Widget, x1, y1, x2, y2) -> None:
        self.create_rectangle(
            x1, y1, x2, y2,
            fill=w.bg_color or "#f8fafc",
            outline=w.border_color or "#d1d5db",
            width=max(1, int(w.border_width * self.zoom)),
            tags=(f"w_{w.id}", "widget")
        )
        self.create_text(
            (x1 + x2) // 2, (y1 + y2) // 2 - 8,
            text="🖼️",
            font=("Segoe UI", max(12, int(20 * self.zoom))),
            tags=(f"w_{w.id}", "widget")
        )
        self.create_text(
            (x1 + x2) // 2, (y1 + y2) // 2 + 14,
            text=w.text,
            fill=WINDOSE_AERO["text_muted"],
            font=("Segoe UI", max(7, int(8 * self.zoom))),
            tags=(f"w_{w.id}", "widget")
        )

    def _draw_canvas_widget(self, w: Widget, x1, y1, x2, y2) -> None:
        self.create_rectangle(
            x1, y1, x2, y2,
            fill=w.bg_color or "#ffffff",
            outline=w.border_color or WINDOSE_AERO["input_border"],
            width=1,
            tags=(f"w_{w.id}", "widget")
        )
        step = int(12 * self.zoom)
        for gx in range(x1 + step, x2, step):
            for gy in range(y1 + step, y2, step):
                self.create_rectangle(gx, gy, gx + 2, gy + 2, fill="#e2e8f0", outline="", tags=(f"w_{w.id}", "widget"))
        self.create_text(
            x1 + 6, y1 + 6,
            text=f"k_canvas: {w.width}x{w.height}",
            anchor="nw",
            fill=WINDOSE_AERO["highlight_dark"],
            font=("Consolas", max(7, int(8 * self.zoom))),
            tags=(f"w_{w.id}", "widget")
        )

    def _draw_colorpicker(self, w: Widget, x1, y1, x2, y2) -> None:
        # Button container
        self.create_rectangle(
            x1, y1, x2, y2,
            fill="#f6f6f6",
            outline=w.border_color or WINDOSE_AERO["control_border"],
            width=1,
            tags=(f"w_{w.id}", "widget")
        )
        sw_sz = max(10, y2 - y1 - 8)
        cur_col = str(w.value or w.bg_color or WINDOSE_AERO["title_active"])
        self.create_rectangle(
            x1 + 4, y1 + 4, x1 + 4 + sw_sz, y1 + 4 + sw_sz,
            fill=cur_col, outline="#acacac", width=1,
            tags=(f"w_{w.id}", "widget")
        )
        self.create_text(
            x1 + 10 + sw_sz, (y1 + y2) // 2,
            text=cur_col,
            anchor="w",
            fill=WINDOSE_AERO["text_dark"],
            font=("Consolas", max(7, int(9 * self.zoom))),
            tags=(f"w_{w.id}", "widget")
        )
        self.create_text(
            x2 - 10, (y1 + y2) // 2,
            text="▼",
            fill=WINDOSE_AERO["text_muted"],
            font=("Segoe UI", max(6, int(7 * self.zoom))),
            tags=(f"w_{w.id}", "widget")
        )

    def _draw_generic_box(self, w: Widget, x1, y1, x2, y2) -> None:
        self.create_rectangle(
            x1, y1, x2, y2,
            fill=w.bg_color or "#ffffff",
            outline=w.border_color or WINDOSE_AERO["control_border"],
            width=1,
            tags=(f"w_{w.id}", "widget")
        )
        self.create_text(
            (x1 + x2) // 2, (y1 + y2) // 2,
            text=w.text,
            fill=w.text_color or WINDOSE_AERO["text_dark"],
            tags=(f"w_{w.id}", "widget")
        )

    def _draw_selection_and_handles(self) -> None:
        """Draws bounding box and 8 resize handles for selected widgets."""
        for wid in self.project.selected_ids:
            w = self.project.get_widget(wid)
            if not w:
                continue
            x1, y1 = self.world_to_screen(w.x, w.y)
            x2, y2 = self.world_to_screen(w.x + w.width, w.y + w.height)

            self.create_rectangle(
                x1 - 2, y1 - 2, x2 + 2, y2 + 2,
                outline=WINDOSE_AERO["highlight"],
                width=2,
                dash=(4, 2),
                tags="selection"
            )

            self.create_text(
                x1, y1 - 10,
                text=f"{w.name} [{w.width}x{w.height} @ {w.x},{w.y}]",
                anchor="w",
                fill=WINDOSE_AERO["highlight_dark"],
                font=("Segoe UI", 8, "bold"),
                tags="selection"
            )

            if wid == self.project.selected_id:
                handles = self._get_handle_coords(x1, y1, x2, y2)
                for hname, (hx, hy) in handles.items():
                    hs = self.HANDLE_SIZE // 2
                    self.create_rectangle(
                        hx - hs, hy - hs, hx + hs, hy + hs,
                        fill=WINDOSE_AERO["highlight"], outline="#ffffff", width=1,
                        tags=("handle", f"handle_{hname}")
                    )

    def _get_handle_coords(self, x1, y1, x2, y2) -> Dict[str, Tuple[int, int]]:
        mx = (x1 + x2) // 2
        my = (y1 + y2) // 2
        return {
            "nw": (x1, y1),
            "n": (mx, y1),
            "ne": (x2, y1),
            "e": (x2, my),
            "se": (x2, y2),
            "s": (mx, y2),
            "sw": (x1, y2),
            "w": (x1, my)
        }

    def _draw_guidelines(self) -> None:
        for x1, y1, x2, y2 in self._guidelines:
            sx1, sy1 = self.world_to_screen(x1, y1)
            sx2, sy2 = self.world_to_screen(x2, y2)
            self.create_line(sx1, sy1, sx2, sy2, fill="#f43f5e", width=1, dash=(3, 3), tags="guide")

    def _draw_marquee_box(self) -> None:
        """Draws the translucent blue selection marquee rectangle when dragging across canvas."""
        if not self._marquee_box:
            return
        sx1, sy1, sx2, sy2 = self._marquee_box
        x1, x2 = min(sx1, sx2), max(sx1, sx2)
        y1, y2 = min(sy1, sy2), max(sy1, sy2)
        self.create_rectangle(
            x1, y1, x2, y2,
            outline="#38bdf8",
            width=1,
            dash=(4, 2),
            fill="#0284c7",
            stipple="gray25",
            tags="marquee"
        )

    def _draw_sim_dialog(self) -> None:
        """Draws an authentic Windows 7 Aero Glass modal message box over the canvas in simulation mode."""
        if not self._sim_dialog:
            return
        cw = max(400, self.winfo_width())
        ch = max(300, self.winfo_height())

        # Dim backdrop
        self.create_rectangle(0, 0, cw, ch, fill="#000000", stipple="gray50", tags="sim_dialog")

        # Dialog Box Dimensions
        dw = 380
        dh = 180
        dx1 = max(20, (cw - dw) // 2)
        dy1 = max(20, (ch - dh) // 2)
        dx2 = dx1 + dw
        dy2 = dy1 + dh

        # Window Drop Shadow
        self.create_rectangle(dx1 + 4, dy1 + 4, dx2 + 4, dy2 + 4, fill="#050811", outline="", tags="sim_dialog")

        # Aero Glass Header
        self.create_rectangle(dx1, dy1, dx2, dy1 + 32, fill="#1e3a8a", outline="#3b82f6", width=1, tags="sim_dialog")
        self.create_text(
            dx1 + 12, dy1 + 16,
            text=f"🪟 {self._sim_dialog.get('title', 'Windose Dialog')}",
            anchor="w",
            fill="#ffffff",
            font=("Segoe UI", 9, "bold"),
            tags="sim_dialog"
        )

        # Close X Button
        self._sim_dialog_close_rect = (dx2 - 28, dy1 + 4, dx2 - 6, dy1 + 26)
        self.create_rectangle(*self._sim_dialog_close_rect, fill="#ef4444", outline="#b91c1c", tags="sim_dialog")
        self.create_text(dx2 - 17, dy1 + 15, text="✕", fill="#ffffff", font=("Segoe UI", 8, "bold"), tags="sim_dialog")

        # Client Area
        self.create_rectangle(dx1, dy1 + 32, dx2, dy2, fill="#f8fafc", outline="#3b82f6", width=1, tags="sim_dialog")

        # Dialog Icon
        mtype = self._sim_dialog.get("type", "info")
        icon_char = "ℹ️" if mtype == "info" else ("⚠️" if mtype == "warning" else ("❌" if mtype == "error" else "❓"))
        self.create_text(dx1 + 34, dy1 + 72, text=icon_char, font=("Segoe UI", 22), tags="sim_dialog")

        # Message Text
        msg = self._sim_dialog.get("text", "")
        self.create_text(
            dx1 + 68, dy1 + 72,
            text=msg,
            anchor="w",
            width=280,
            fill="#1e293b",
            font=("Segoe UI", 9),
            tags="sim_dialog"
        )

        # Bottom Button Bar
        self.create_rectangle(dx1, dy2 - 42, dx2, dy2, fill="#f1f5f9", outline="#e2e8f0", tags="sim_dialog")

        # Action Buttons
        buttons = self._sim_dialog.get("buttons", ["OK"])
        self._sim_dialog_btn_rects = []
        bx = dx2 - 14
        for b_name in reversed(buttons):
            bw = 75
            bh = 26
            b_rect = (bx - bw, dy2 - 34, bx, dy2 - 8)
            self._sim_dialog_btn_rects.append((b_name, b_rect))
            self.create_rectangle(*b_rect, fill="#3b82f6", outline="#2563eb", width=1, tags="sim_dialog")
            self.create_text(bx - bw // 2, dy2 - 21, text=b_name, fill="#ffffff", font=("Segoe UI", 9, "bold"), tags="sim_dialog")
            bx -= bw + 8

    def trigger_mock_msgbox(self, title: str, text: str, buttons: str = "OK", msg_type: str = "info") -> None:
        """Triggers an authentic Aero modal message box in simulation mode."""
        btn_list = [b.strip() for b in buttons.split("/") if b.strip()] or ["OK"]
        self._sim_dialog = {
            "title": title,
            "text": text,
            "buttons": btn_list,
            "type": msg_type
        }
        self.redraw()

    # -------------------------------------------------------------
    # Mouse & Keyboard Event Handlers
    # -------------------------------------------------------------
    def _find_widget_at(self, wx: int, wy: int) -> Optional[Widget]:
        """Finds top-most widget containing coordinate."""
        for w in reversed(self.project.widgets):
            if w.visible and w.contains(wx, wy):
                return w
        return None

    def _find_handle_at(self, sx: int, sy: int) -> Optional[str]:
        w = self.project.get_selected_widget()
        if not w:
            return None
        x1, y1 = self.world_to_screen(w.x, w.y)
        x2, y2 = self.world_to_screen(w.x + w.width, w.y + w.height)
        handles = self._get_handle_coords(x1, y1, x2, y2)
        hs = self.HANDLE_SIZE + 2
        for hname, (hx, hy) in handles.items():
            if abs(sx - hx) <= hs and abs(sy - hy) <= hs:
                return hname
        return None

    def _on_mouse_down(self, event) -> None:
        self.focus_set()
        sx, sy = event.x, event.y
        wx, wy = self.screen_to_world(sx, sy)

        # Simulation Mode Interactions
        if self.simulation_mode:
            # Check if interactive mock modal dialog is currently open
            if self._sim_dialog:
                cx1, cy1, cx2, cy2 = self._sim_dialog_close_rect
                if cx1 <= sx <= cx2 and cy1 <= sy <= cy2:
                    if self.on_sim_event:
                        self.on_sim_event("k_msgbox", f"Dialog '{self._sim_dialog.get('title')}' dismissed.")
                    self._sim_dialog = None
                    self.redraw()
                    return

                for b_name, (bx1, by1, bx2, by2) in self._sim_dialog_btn_rects:
                    if bx1 <= sx <= bx2 and by1 <= sy <= by2:
                        if self.on_sim_event:
                            self.on_sim_event("k_msgbox", f"Dialog '{self._sim_dialog.get('title')}' dismissed with '{b_name}'.")
                        self._sim_dialog = None
                        self.redraw()
                        return
                return  # Modal absorbs clicks outside buttons

            cb_x1, cb_y1, cb_x2, cb_y2 = self._close_rect
            if cb_x1 <= sx <= cb_x2 and cb_y1 <= sy <= cb_y2:
                if self.on_sim_event:
                    self.on_sim_event("Aero Window", "[WM_CLOSE] Simulation window closed.")
                self.set_simulation_mode(False)
                return

            mb_x1, mb_y1, mb_x2, mb_y2 = self._max_rect
            if mb_x1 <= sx <= mb_x2 and mb_y1 <= sy <= mb_y2:
                if self.on_sim_event:
                    self.on_sim_event("Aero Window", "[WM_MAXIMIZE] Window maximized / reset zoom.")
                self.zoom_reset()
                return

            nb_x1, nb_y1, nb_x2, nb_y2 = self._min_rect
            if nb_x1 <= sx <= nb_x2 and nb_y1 <= sy <= nb_y2:
                if self.on_sim_event:
                    self.on_sim_event("Aero Window", "[WM_MINIMIZE] Window minimized to taskbar.")
                return

            self._handle_simulation_click(wx, wy)
            return

        # Design Mode: Check resize handles
        handle = self._find_handle_at(sx, sy)
        if handle:
            self._drag_mode = "resize"
            self._active_handle = handle
            self._drag_start_x = wx
            self._drag_start_y = wy
            w = self.project.get_selected_widget()
            self._initial_bounds = {w.id: (w.x, w.y, w.width, w.height)}
            return

        # Design Mode: Check hit widget
        hit_widget = self._find_widget_at(wx, wy)
        if hit_widget:
            multi = (event.state & 0x0001) != 0 or (event.state & 0x0004) != 0
            self.project.select_widget(hit_widget.id, multi=multi)
            self._drag_mode = "move"
            self._drag_start_x = wx
            self._drag_start_y = wy
            self._initial_bounds = {
                wid: (w.x, w.y, w.width, w.height)
                for wid in self.project.selected_ids
                if (w := self.project.get_widget(wid))
            }
            if self.on_widget_selected:
                self.on_widget_selected(hit_widget.id)
            self.redraw()
            return

        # Design Mode: Empty Canvas Click -> Initiate Marquee Drag Selection
        multi = (event.state & 0x0001) != 0 or (event.state & 0x0004) != 0
        if not multi:
            self.project.select_widget(None)
            if self.on_widget_selected:
                self.on_widget_selected(None)

        self._drag_mode = "marquee"
        self._marquee_start = (sx, sy, wx, wy)
        self._marquee_box = (sx, sy, sx, sy)
        self.redraw()

    def _on_mouse_drag(self, event) -> None:
        sx, sy = event.x, event.y
        wx, wy = self.screen_to_world(sx, sy)

        if self.simulation_mode:
            hit_w = self._find_widget_at(wx, wy)
            if hit_w and hit_w.widget_type == WidgetType.SLIDER:
                self._update_slider_from_mouse(hit_w, wx)
            return

        if self._drag_mode == "marquee" and self._marquee_start:
            self._marquee_box = (self._marquee_start[0], self._marquee_start[1], sx, sy)
            self.request_redraw()
            return

        if self._drag_mode == "pan":
            dx = sx - self._drag_start_x
            dy = sy - self._drag_start_y
            self.offset_x += dx
            self.offset_y += dy
            self._drag_start_x = sx
            self._drag_start_y = sy
            self.request_redraw()
            return

        if self._drag_mode == "move":
            dx = wx - self._drag_start_x
            dy = wy - self._drag_start_y
            self._guidelines.clear()

            primary_w = self.project.get_selected_widget()
            for wid, (ix, iy, iw, ih) in self._initial_bounds.items():
                w = self.project.get_widget(wid)
                if not w:
                    continue
                nx = self.project.snap(ix + dx)
                ny = self.project.snap(iy + dy)
                nx = max(0, min(self.project.canvas_width - w.width, nx))
                ny = max(0, min(self.project.canvas_height - w.height, ny))
                w.x = nx
                w.y = ny

            if primary_w:
                self._compute_alignment_guides(primary_w)

            self.request_redraw()
            return

        if self._drag_mode == "resize":
            w = self.project.get_selected_widget()
            if not w or w.id not in self._initial_bounds:
                return
            ix, iy, iw, ih = self._initial_bounds[w.id]
            dx = wx - self._drag_start_x
            dy = wy - self._drag_start_y

            h = self._active_handle
            nx, ny, nw, nh = ix, iy, iw, ih

            if "e" in h:
                nw = max(16, self.project.snap(iw + dx))
            if "s" in h:
                nh = max(16, self.project.snap(ih + dy))
            if "w" in h:
                snapped_dx = self.project.snap(dx)
                if iw - snapped_dx >= 16:
                    nx = ix + snapped_dx
                    nw = iw - snapped_dx
            if "n" in h:
                snapped_dy = self.project.snap(dy)
                if ih - snapped_dy >= 16:
                    ny = iy + snapped_dy
                    nh = ih - snapped_dy

            w.x = nx
            w.y = ny
            w.width = nw
            w.height = nh
            self.request_redraw()
            return

    def _set_canvas_cursor(self, cursor_name: str) -> None:
        if self._current_cursor != cursor_name:
            self._current_cursor = cursor_name
            self.config(cursor=cursor_name)

    def _on_mouse_up(self, event) -> None:
        self._guidelines.clear()
        if self._drag_mode == "marquee":
            if self._marquee_box and self._marquee_start:
                sx1, sy1 = self._marquee_start[0], self._marquee_start[1]
                sx2, sy2 = event.x, event.y
                if abs(sx2 - sx1) > 4 or abs(sy2 - sy1) > 4:
                    wx1, wy1 = self.screen_to_world(min(sx1, sx2), min(sy1, sy2))
                    wx2, wy2 = self.screen_to_world(max(sx1, sx2), max(sy1, sy2))
                    multi = (event.state & 0x0001) != 0 or (event.state & 0x0004) != 0
                    self.project.select_in_rect(wx1, wy1, wx2, wy2, multi=multi)
                    if self.on_widget_selected:
                        self.on_widget_selected(self.project.selected_id)
            self._marquee_box = None
            self._marquee_start = None
            self._drag_mode = None
            self.redraw()
            return

        if self._drag_mode in ("move", "resize"):
            self.project.push_undo()
            if self.on_widget_modified:
                self.on_widget_modified()
        self._drag_mode = None
        self._active_handle = None
        self.redraw()

    # -------------------------------------------------------------
    # Right-Click & Middle-Click Canvas Pan & Aero Context Menu
    # -------------------------------------------------------------
    def _on_right_mouse_down(self, event) -> None:
        self.focus_set()
        self._pan_start = (event.x, event.y)
        self._pan_moved = False

    def _on_right_mouse_drag(self, event) -> None:
        if not self._pan_start:
            return
        dx = event.x - self._pan_start[0]
        dy = event.y - self._pan_start[1]
        if abs(dx) > 3 or abs(dy) > 3:
            self._pan_moved = True
            self.offset_x += dx
            self.offset_y += dy
            self._pan_start = (event.x, event.y)
            self.request_redraw()

    def _on_right_mouse_up(self, event) -> None:
        if not self._pan_moved and not self.simulation_mode:
            self._show_context_menu(event)
        self._pan_start = None
        self._pan_moved = False

    def _on_middle_mouse_down(self, event) -> None:
        self.focus_set()
        self._pan_start = (event.x, event.y)

    def _on_middle_mouse_drag(self, event) -> None:
        if not self._pan_start:
            return
        dx = event.x - self._pan_start[0]
        dy = event.y - self._pan_start[1]
        self.offset_x += dx
        self.offset_y += dy
        self._pan_start = (event.x, event.y)
        self.request_redraw()

    def _on_middle_mouse_up(self, event) -> None:
        self._pan_start = None

    def _show_context_menu(self, event) -> None:
        """Displays native Windows 7 Aero dark context menu for the clicked widget or canvas."""
        sx, sy = event.x, event.y
        wx, wy = self.screen_to_world(sx, sy)

        hit = self._find_widget_at(wx, wy)
        if hit:
            if hit.id not in self.project.selected_ids:
                self.project.select_widget(hit.id)
                if self.on_widget_selected:
                    self.on_widget_selected(hit.id)
                self.redraw()
        else:
            if not ((event.state & 0x0001) or (event.state & 0x0004)):
                self.project.select_widget(None)
                if self.on_widget_selected:
                    self.on_widget_selected(None)
                self.redraw()

        menu = tk.Menu(
            self,
            tearoff=0,
            bg="#16202c",
            fg="#f8fafc",
            activebackground="#3464a8",
            activeforeground="#ffffff",
            activeborderwidth=0,
            bd=1,
            relief="solid",
            font=("Segoe UI", 9)
        )

        sel = self.project.get_selected_widget()
        has_sel = (sel is not None)

        if has_sel:
            menu.add_command(
                label=f"⚡ Jump to C Callback ({sel.event_handler})",
                command=lambda: self.on_widget_double_click(sel) if self.on_widget_double_click else None
            )
            menu.add_separator()
            menu.add_command(label="📋 Copy (Ctrl+C)", command=lambda: self.project.copy_selected())
            menu.add_command(label="✂️ Cut (Ctrl+X)", command=self._action_cut)
            menu.add_command(label="📑 Duplicate (Ctrl+D)", command=self._action_duplicate)

        menu.add_command(label="📄 Paste (Ctrl+V)", command=self._action_paste)

        if has_sel:
            menu.add_separator()
            align_menu = tk.Menu(
                menu,
                tearoff=0,
                bg="#16202c",
                fg="#f8fafc",
                activebackground="#3464a8",
                activeforeground="#ffffff",
                font=("Segoe UI", 9)
            )
            align_menu.add_command(label="⇤ Align Left", command=lambda: self._align_action("left"))
            align_menu.add_command(label="↔ Center Horizontally", command=lambda: self._align_action("center_h"))
            align_menu.add_command(label="⇥ Align Right", command=lambda: self._align_action("right"))
            align_menu.add_separator()
            align_menu.add_command(label="⇡ Align Top", command=lambda: self._align_action("top"))
            align_menu.add_command(label="↕ Center Vertically", command=lambda: self._align_action("center_v"))
            align_menu.add_command(label="⇣ Align Bottom", command=lambda: self._align_action("bottom"))
            align_menu.add_separator()
            align_menu.add_command(label="↔ Distribute Horizontally", command=lambda: self._align_action("distribute_h"))
            align_menu.add_command(label="↕ Distribute Vertically", command=lambda: self._align_action("distribute_v"))
            align_menu.add_separator()
            align_menu.add_command(label="↔ Make Same Width", command=lambda: self._align_action("match_width"))
            align_menu.add_command(label="↕ Make Same Height", command=lambda: self._align_action("match_height"))
            align_menu.add_command(label="⊞ Make Same Size", command=lambda: self._align_action("match_size"))
            align_menu.add_separator()
            align_menu.add_command(label="⊕ Center in Window (H)", command=lambda: self._align_action("center_canvas_h"))
            align_menu.add_command(label="⊕ Center in Window (V)", command=lambda: self._align_action("center_canvas_v"))
            menu.add_cascade(label="📐 Align & Distribute", menu=align_menu)

            menu.add_separator()
            menu.add_command(label="⬆️ Bring to Front", command=lambda: self._reorder_action("top"))
            menu.add_command(label="⬇️ Send to Back", command=lambda: self._reorder_action("bottom"))
            menu.add_separator()
            menu.add_command(label="🗑️ Delete (Del)", command=self._action_delete)
        else:
            menu.add_separator()
            menu.add_command(label="🔍 Reset Zoom (100%)", command=self.zoom_reset)

        menu.tk_popup(event.x_root, event.y_root)

    def _action_cut(self) -> None:
        self.project.copy_selected()
        self.project.delete_selected()
        if self.on_widget_selected:
            self.on_widget_selected(None)
        if self.on_widget_modified:
            self.on_widget_modified()
        self.redraw()

    def _action_duplicate(self) -> None:
        new_w = self.project.duplicate_selected()
        if new_w:
            if self.on_widget_selected:
                self.on_widget_selected(new_w.id)
            if self.on_widget_modified:
                self.on_widget_modified()
            self.redraw()

    def _action_paste(self) -> None:
        new_widgets = self.project.paste_clipboard()
        if new_widgets:
            if self.on_widget_selected:
                self.on_widget_selected(new_widgets[-1].id)
            if self.on_widget_modified:
                self.on_widget_modified()
            self.redraw()

    def _action_delete(self) -> None:
        if self.project.delete_selected():
            if self.on_widget_selected:
                self.on_widget_selected(None)
            if self.on_widget_modified:
                self.on_widget_modified()
            self.redraw()

    def _align_action(self, mode: str) -> None:
        self.project.align_selected(mode)
        if self.on_widget_modified:
            self.on_widget_modified()
        self.redraw()

    def _reorder_action(self, action: str) -> None:
        if self.project.selected_id:
            self.project.reorder(self.project.selected_id, action)
            if self.on_widget_modified:
                self.on_widget_modified()
            self.redraw()

    def _on_mouse_hover(self, event) -> None:
        sx, sy = event.x, event.y
        # Check caption button hover
        cb_x1, cb_y1, cb_x2, cb_y2 = self._close_rect
        mb_x1, mb_y1, mb_x2, mb_y2 = self._max_rect
        nb_x1, nb_y1, nb_x2, nb_y2 = self._min_rect

        new_hover = None
        if cb_x1 <= sx <= cb_x2 and cb_y1 <= sy <= cb_y2:
            new_hover = "close"
        elif mb_x1 <= sx <= mb_x2 and mb_y1 <= sy <= mb_y2:
            new_hover = "max"
        elif nb_x1 <= sx <= nb_x2 and nb_y1 <= sy <= nb_y2:
            new_hover = "min"

        if new_hover != self._hovered_caption_btn:
            self._hovered_caption_btn = new_hover
            self.request_redraw()

        if self.simulation_mode:
            self._set_canvas_cursor("hand2")
            return

        handle = self._find_handle_at(sx, sy)
        if handle:
            cursors = {
                "nw": "size_nw_se", "se": "size_nw_se",
                "ne": "size_ne_sw", "sw": "size_ne_sw",
                "n": "size_ns", "s": "size_ns",
                "e": "size_we", "w": "size_we"
            }
            self._set_canvas_cursor(cursors.get(handle, "arrow"))
            return

        wx, wy = self.screen_to_world(sx, sy)
        w = self._find_widget_at(wx, wy)
        if w:
            self._set_canvas_cursor("fleur")
        else:
            self._set_canvas_cursor("arrow")

    def _compute_alignment_guides(self, cur_w: Widget) -> None:
        cx = cur_w.x + cur_w.width // 2
        cy = cur_w.y + cur_w.height // 2
        canvas_cx = self.project.canvas_width // 2
        canvas_cy = self.project.canvas_height // 2

        if abs(cx - canvas_cx) < 5:
            cur_w.x = canvas_cx - cur_w.width // 2
            self._guidelines.append((canvas_cx, 0, canvas_cx, self.project.canvas_height))
        if abs(cy - canvas_cy) < 5:
            cur_w.y = canvas_cy - cur_w.height // 2
            self._guidelines.append((0, canvas_cy, self.project.canvas_width, canvas_cy))

    # -------------------------------------------------------------
    # Simulation Mode Execution
    # -------------------------------------------------------------
    def _handle_simulation_click(self, wx: int, wy: int) -> None:
        w = self._find_widget_at(wx, wy)
        if not w or not w.enabled:
            self._active_input_wid = None
            self.redraw()
            return

        if w.widget_type == WidgetType.BUTTON:
            if self.on_sim_event:
                self.on_sim_event(w.name, f"Triggered event handler: {w.event_handler}()")
            self._flash_button(w)

            # Simulated response for common OS button actions
            bname = (w.name or "").lower()
            btext = (w.text or "").lower()
            hname = (w.event_handler or "").lower()
            if any(k in bname or k in btext or k in hname for k in ("save", "write")):
                self.trigger_mock_msgbox("Windose Storage", f"Data saved successfully to /mnt/disk0/{self.project.name.lower()}.dat", "OK", "info")
            elif any(k in bname or k in btext or k in hname for k in ("msg", "alert", "dialog", "notify", "help", "about", "info")):
                self.trigger_mock_msgbox("Information", f"Application dialog triggered by {w.name} ({w.event_handler})", "OK", "info")
            elif any(k in bname or k in btext or k in hname for k in ("launch", "exec", "run", "start")):
                self.trigger_mock_msgbox("Windose Process Manager", f"Spawned process: /mnt/Programs/{self.project.name.lower()}.elf (PID 142)", "OK", "info")
            elif any(k in bname or k in btext or k in hname for k in ("delete", "remove", "exit", "quit")):
                self.trigger_mock_msgbox("Confirm Action", f"Are you sure you want to proceed with {w.text}?", "Yes/No", "warning")

        elif w.widget_type == WidgetType.CHECKBOX:
            w.value = not bool(w.value)
            if self.on_sim_event:
                self.on_sim_event(w.name, f"State: {'CHECKED' if w.value else 'UNCHECKED'}")
            self.redraw()

        elif w.widget_type == WidgetType.SWITCH:
            w.value = not bool(w.value)
            if self.on_sim_event:
                self.on_sim_event(w.name, f"Power State: {'ON' if w.value else 'OFF'}")
            self.redraw()

        elif w.widget_type == WidgetType.RADIO:
            for other in self.project.widgets:
                if other.widget_type == WidgetType.RADIO and other.group == w.group:
                    other.value = False
            w.value = True
            if self.on_sim_event:
                self.on_sim_event(w.name, f"Radio checked: '{w.text}' in group [{w.group}]")
            self.redraw()

        elif w.widget_type == WidgetType.SLIDER:
            self._update_slider_from_mouse(w, wx)

        elif w.widget_type in (WidgetType.ENTRY, WidgetType.TEXTAREA):
            self._active_input_wid = w.id
            if self.on_sim_event:
                self.on_sim_event(w.name, f"Focused. Type characters on your keyboard.")
            self.redraw()

        elif w.widget_type == WidgetType.DROPDOWN:
            if w.options:
                idx = w.options.index(w.value) if w.value in w.options else 0
                next_idx = (idx + 1) % len(w.options)
                w.value = w.options[next_idx]
                if self.on_sim_event:
                    self.on_sim_event(w.name, f"Option changed to: '{w.value}'")
                self.redraw()

        elif w.widget_type == WidgetType.LISTBOX:
            items = w.items or ["Item 1", "Item 2", "Item 3", "Item 4"]
            hh = max(18, int(22 * self.zoom))
            row_h = max(16, int(20 * self.zoom))
            rel_y = (wy - w.y) - hh
            if rel_y >= 0:
                idx = min(len(items) - 1, max(0, int(rel_y // row_h)))
                w.value = items[idx]
                if self.on_sim_event:
                    self.on_sim_event(w.name, f"Selected row {idx}: '{w.value}'")
                self.redraw()

        elif w.widget_type == WidgetType.NUMERIC:
            mid_y = w.y + w.height // 2
            step = w.step or 1.0
            cur = float(w.value or 0.0)
            if wy < mid_y:
                w.value = min(w.max_val, cur + step)
            else:
                w.value = max(w.min_val, cur - step)
            if self.on_sim_event:
                self.on_sim_event(w.name, f"Numeric value updated: {w.value}")
            self.redraw()

        elif w.widget_type == WidgetType.TABS:
            pages = w.items or ["General", "Security", "Advanced"]
            tab_w = max(45, w.width // max(1, len(pages)))
            rel_x = wx - w.x
            idx = min(len(pages) - 1, max(0, int(rel_x // tab_w)))
            w.value = pages[idx]
            if self.on_sim_event:
                self.on_sim_event(w.name, f"Switched to tab page: '{w.value}'")
            self.redraw()

        elif w.widget_type == WidgetType.COLORPICKER:
            from ui.theme import PRESET_COLORS
            colors = [c[0] for c in PRESET_COLORS]
            cur = str(w.value or colors[0]).lower()
            idx = 0
            for i, c in enumerate(colors):
                if c.lower() == cur:
                    idx = (i + 1) % len(colors)
                    break
            w.value = colors[idx]
            w.bg_color = colors[idx]
            if self.on_sim_event:
                self.on_sim_event(w.name, f"Color changed to: {w.value}")
            self.redraw()

    def _update_slider_from_mouse(self, w: Widget, wx: int) -> None:
        norm = max(0.0, min(1.0, (wx - w.x) / max(1, w.width)))
        new_val = w.min_val + norm * (w.max_val - w.min_val)
        w.value = round(new_val, 1)
        if self.on_sim_event:
            self.on_sim_event(w.name, f"Value changed to {w.value}")
        self.request_redraw()

    def _flash_button(self, w: Widget) -> None:
        """Visual click feedback in Windose light blue pressed gradient."""
        orig = w.bg_color
        w.bg_color = WINDOSE_AERO["control_highlight"]
        self.redraw()
        self.after(100, lambda: self._reset_button(w, orig))

    def _reset_button(self, w: Widget, orig: str) -> None:
        w.bg_color = orig
        self.redraw()

    def _on_key_press(self, event) -> None:
        if self.simulation_mode:
            if self._active_input_wid:
                w = self.project.get_widget(self._active_input_wid)
                if w:
                    cur = str(w.value or "")
                    if event.keysym == "BackSpace":
                        w.value = cur[:-1]
                    elif event.keysym in ("Return", "KP_Enter"):
                        if w.widget_type == WidgetType.TEXTAREA:
                            w.value = cur + "\n"
                        else:
                            self._active_input_wid = None
                            if self.on_sim_event:
                                self.on_sim_event(w.name, f"Committed: '{w.value}'")
                    elif len(event.char) == 1 and event.char.isprintable():
                        w.value = cur + event.char
                    self.redraw()
            return

        # Design Mode Key Bindings
        # Arrow key nudging
        if event.keysym in ("Left", "Right", "Up", "Down"):
            step = self.project.grid_size if (event.state & 0x0001) else 1
            if event.keysym == "Left": self.project.nudge_selected(-step, 0)
            elif event.keysym == "Right": self.project.nudge_selected(step, 0)
            elif event.keysym == "Up": self.project.nudge_selected(0, -step)
            elif event.keysym == "Down": self.project.nudge_selected(0, step)
            if self.on_widget_modified:
                self.on_widget_modified()
            self.redraw()
            return

        if event.keysym in ("Delete", "BackSpace"):
            if self.project.delete_selected():
                if self.on_widget_selected:
                    self.on_widget_selected(None)
                if self.on_widget_modified:
                    self.on_widget_modified()
                self.redraw()

        elif (event.state & 0x0004) and event.keysym.lower() == "a":  # Ctrl+A Select All
            self.project.select_all()
            self.redraw()

        elif (event.state & 0x0004) and event.keysym.lower() == "c":  # Ctrl+C Copy
            self.project.copy_selected()

        elif (event.state & 0x0004) and event.keysym.lower() == "v":  # Ctrl+V Paste
            new_widgets = self.project.paste_clipboard()
            if new_widgets:
                if self.on_widget_selected:
                    self.on_widget_selected(new_widgets[-1].id)
                if self.on_widget_modified:
                    self.on_widget_modified()
                self.redraw()

        elif (event.state & 0x0004) and event.keysym.lower() == "d":  # Ctrl+D Duplicate
            new_w = self.project.duplicate_selected()
            if new_w:
                if self.on_widget_selected:
                    self.on_widget_selected(new_w.id)
                if self.on_widget_modified:
                    self.on_widget_modified()
                self.redraw()

        elif (event.state & 0x0004) and event.keysym.lower() == "z":  # Ctrl+Z Undo
            if self.project.undo():
                if self.on_widget_modified:
                    self.on_widget_modified()
                self.redraw()

        elif (event.state & 0x0004) and event.keysym.lower() == "y":  # Ctrl+Y Redo
            if self.project.redo():
                if self.on_widget_modified:
                    self.on_widget_modified()
                self.redraw()

    def _on_mouse_wheel(self, event) -> None:
        if event.state & 0x0004:  # Ctrl + Wheel = Zoom
            if event.delta > 0:
                self.zoom_in()
            else:
                self.zoom_out()
        else:
            self.offset_y += (event.delta // 4)
            self.redraw()
