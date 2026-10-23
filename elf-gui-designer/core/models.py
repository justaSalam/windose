"""
Core data models for ELF GUI Designer.
Manages widgets, properties, project configuration, and undo/redo history.
"""

import json
import uuid
from typing import Dict, List, Optional, Any, Tuple


class WidgetType:
    BUTTON = "button"
    LABEL = "label"
    ENTRY = "entry"
    TEXTAREA = "textarea"
    SLIDER = "slider"
    PROGRESSBAR = "progressbar"
    CHECKBOX = "checkbox"
    SWITCH = "switch"
    RADIO = "radio"
    DROPDOWN = "dropdown"
    LISTBOX = "listbox"
    DATAGRID = "datagrid"
    NUMERIC = "numeric"
    SEPARATOR = "separator"
    TABS = "tabs"
    PANEL = "panel"
    IMAGE = "image"
    CANVAS = "canvas"
    COLORPICKER = "colorpicker"
    STATUSBAR = "statusbar"

    ALL_TYPES = [
        BUTTON, LABEL, ENTRY, TEXTAREA, SLIDER,
        PROGRESSBAR, CHECKBOX, SWITCH, RADIO, DROPDOWN,
        LISTBOX, DATAGRID, NUMERIC, SEPARATOR, TABS,
        PANEL, IMAGE, CANVAS, COLORPICKER, STATUSBAR
    ]


class Widget:
    """Represents a UI component in the design."""

    def __init__(
        self,
        widget_type: str,
        name: str = "",
        x: int = 20,
        y: int = 20,
        width: int = 120,
        height: int = 40,
        text: str = "",
        bg_color: str = "",
        text_color: str = "",
        border_color: str = "",
        border_width: int = 1,
        corner_radius: int = 3,
        font_size: int = 11,
        font_weight: str = "normal",
        alignment: str = "center",
        min_val: float = 0.0,
        max_val: float = 100.0,
        value: Any = None,
        options: Optional[List[str]] = None,
        group: str = "",
        step: float = 1.0,
        placeholder: str = "",
        columns: Optional[List[str]] = None,
        items: Optional[List[str]] = None,
        orientation: str = "horizontal",
        event_handler: str = "",
        visible: bool = True,
        enabled: bool = True,
        widget_id: Optional[str] = None
    ):
        self.id = widget_id or f"w_{uuid.uuid4().hex[:8]}"
        self.widget_type = widget_type
        self.name = name or f"{widget_type}_{self.id[:4]}"
        self.x = int(x)
        self.y = int(y)
        self.width = max(6, int(width))
        self.height = max(6, int(height))
        self.text = text or self._default_text_for_type(widget_type)
        self.bg_color = bg_color or self._default_bg_for_type(widget_type)
        self.text_color = text_color or self._default_text_color_for_type(widget_type)
        self.border_color = border_color or self._default_border_for_type(widget_type)
        self.border_width = int(border_width)
        self.corner_radius = int(corner_radius)
        self.font_size = int(font_size)
        self.font_weight = font_weight
        self.alignment = alignment
        self.min_val = float(min_val)
        self.max_val = float(max_val)
        self.value = value if value is not None else self._default_value_for_type(widget_type)
        self.options = options or (["Option 1", "Option 2", "Option 3"] if widget_type == WidgetType.DROPDOWN else [])
        self.group = group or ("group1" if widget_type == WidgetType.RADIO else "")
        self.step = float(step)
        self.placeholder = placeholder or ("Type here..." if widget_type in (WidgetType.ENTRY, WidgetType.TEXTAREA) else "")
        self.columns = columns or (["ID", "Name", "Status"] if widget_type == WidgetType.DATAGRID else (["Item"] if widget_type == WidgetType.LISTBOX else []))
        self.items = items or (["General", "Security", "Advanced"] if widget_type == WidgetType.TABS else (["Row 1", "Row 2", "Row 3"] if widget_type == WidgetType.LISTBOX else []))
        self.orientation = orientation
        self.event_handler = event_handler or f"on_{self.name}_event"
        self.visible = visible
        self.enabled = enabled

    @staticmethod
    def _default_text_for_type(wtype: str) -> str:
        defaults = {
            WidgetType.BUTTON: "Button",
            WidgetType.LABEL: "Text Label",
            WidgetType.ENTRY: "Input text...",
            WidgetType.TEXTAREA: "Multi-line text editor\nLine 2 text\nLine 3 ready.",
            WidgetType.SLIDER: "Slider",
            WidgetType.PROGRESSBAR: "Loading...",
            WidgetType.CHECKBOX: "Enable Option",
            WidgetType.SWITCH: "Active",
            WidgetType.RADIO: "Radio Option",
            WidgetType.DROPDOWN: "Select Option",
            WidgetType.LISTBOX: "List View",
            WidgetType.DATAGRID: "Data Table",
            WidgetType.NUMERIC: "0",
            WidgetType.SEPARATOR: "",
            WidgetType.TABS: "Tab Strip",
            WidgetType.PANEL: "Container Panel",
            WidgetType.IMAGE: "[ Graphic / Icon ]",
            WidgetType.CANVAS: "Drawing Canvas",
            WidgetType.COLORPICKER: "#3464a8",
            WidgetType.STATUSBAR: "Ready | Windose OS v10 Aero"
        }
        return defaults.get(wtype, "Widget")

    @staticmethod
    def _default_bg_for_type(wtype: str) -> str:
        defaults = {
            WidgetType.BUTTON: "#f0f0f0",
            WidgetType.LABEL: "transparent",
            WidgetType.ENTRY: "#ffffff",
            WidgetType.TEXTAREA: "#ffffff",
            WidgetType.SLIDER: "transparent",
            WidgetType.PROGRESSBAR: "#e2e8f0",
            WidgetType.CHECKBOX: "transparent",
            WidgetType.SWITCH: "transparent",
            WidgetType.RADIO: "transparent",
            WidgetType.DROPDOWN: "#ffffff",
            WidgetType.LISTBOX: "#ffffff",
            WidgetType.DATAGRID: "#ffffff",
            WidgetType.NUMERIC: "#ffffff",
            WidgetType.SEPARATOR: "#d1d5db",
            WidgetType.TABS: "#f5f6f7",
            WidgetType.PANEL: "#ffffff",
            WidgetType.IMAGE: "#f8fafc",
            WidgetType.CANVAS: "#ffffff",
            WidgetType.COLORPICKER: "#f0f0f0",
            WidgetType.STATUSBAR: "#f0f0f0"
        }
        return defaults.get(wtype, "#ffffff")

    @staticmethod
    def _default_text_color_for_type(wtype: str) -> str:
        defaults = {
            WidgetType.BUTTON: "#1e293b",
            WidgetType.LABEL: "#1e293b",
            WidgetType.ENTRY: "#1e293b",
            WidgetType.TEXTAREA: "#1e293b",
            WidgetType.SLIDER: "#475569",
            WidgetType.PROGRESSBAR: "#ffffff",
            WidgetType.CHECKBOX: "#1e293b",
            WidgetType.SWITCH: "#1e293b",
            WidgetType.RADIO: "#1e293b",
            WidgetType.DROPDOWN: "#1e293b",
            WidgetType.LISTBOX: "#1e293b",
            WidgetType.DATAGRID: "#1e293b",
            WidgetType.NUMERIC: "#1e293b",
            WidgetType.TABS: "#1e293b",
            WidgetType.PANEL: "#1e293b",
            WidgetType.STATUSBAR: "#334155",
            WidgetType.COLORPICKER: "#1e293b",
            WidgetType.CANVAS: "#38bdf8",
            WidgetType.IMAGE: "#64748b"
        }
        return defaults.get(wtype, "#1e293b")

    @staticmethod
    def _default_border_for_type(wtype: str) -> str:
        defaults = {
            WidgetType.BUTTON: "#acacac",
            WidgetType.LABEL: "",
            WidgetType.ENTRY: "#7f9db9",
            WidgetType.TEXTAREA: "#7f9db9",
            WidgetType.SLIDER: "#d1d5db",
            WidgetType.PROGRESSBAR: "#bcbfc4",
            WidgetType.CHECKBOX: "#7f9db9",
            WidgetType.SWITCH: "#7f9db9",
            WidgetType.RADIO: "#7f9db9",
            WidgetType.DROPDOWN: "#7f9db9",
            WidgetType.LISTBOX: "#7f9db9",
            WidgetType.DATAGRID: "#d1d5db",
            WidgetType.NUMERIC: "#7f9db9",
            WidgetType.SEPARATOR: "#d1d5db",
            WidgetType.TABS: "#d1d5db",
            WidgetType.PANEL: "#d1d5db",
            WidgetType.IMAGE: "#d1d5db",
            WidgetType.CANVAS: "#7f9db9",
            WidgetType.COLORPICKER: "#acacac",
            WidgetType.STATUSBAR: "#d1d5db"
        }
        return defaults.get(wtype, "#acacac")

    @staticmethod
    def _default_value_for_type(wtype: str) -> Any:
        if wtype in (WidgetType.CHECKBOX, WidgetType.SWITCH, WidgetType.RADIO):
            return False
        if wtype in (WidgetType.SLIDER, WidgetType.PROGRESSBAR):
            return 50.0
        if wtype == WidgetType.NUMERIC:
            return 0.0
        if wtype == WidgetType.DROPDOWN:
            return "Option 1"
        if wtype == WidgetType.TABS:
            return "Page 1"
        if wtype == WidgetType.COLORPICKER:
            return "#2563eb"
        return ""

    def get_bounds(self) -> Tuple[int, int, int, int]:
        """Returns (x1, y1, x2, y2)."""
        return self.x, self.y, self.x + self.width, self.y + self.height

    def contains(self, px: int, py: int) -> bool:
        return self.x <= px <= self.x + self.width and self.y <= py <= self.y + self.height

    def to_dict(self) -> Dict[str, Any]:
        return {
            "id": self.id,
            "widget_type": self.widget_type,
            "name": self.name,
            "x": self.x,
            "y": self.y,
            "width": self.width,
            "height": self.height,
            "text": self.text,
            "bg_color": self.bg_color,
            "text_color": self.text_color,
            "border_color": self.border_color,
            "border_width": self.border_width,
            "corner_radius": self.corner_radius,
            "font_size": self.font_size,
            "font_weight": self.font_weight,
            "alignment": self.alignment,
            "min_val": self.min_val,
            "max_val": self.max_val,
            "value": self.value,
            "options": self.options,
            "group": self.group,
            "step": self.step,
            "placeholder": self.placeholder,
            "columns": self.columns,
            "items": self.items,
            "orientation": self.orientation,
            "event_handler": self.event_handler,
            "visible": self.visible,
            "enabled": self.enabled
        }

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> "Widget":
        return cls(
            widget_type=data.get("widget_type", WidgetType.BUTTON),
            name=data.get("name", ""),
            x=data.get("x", 20),
            y=data.get("y", 20),
            width=data.get("width", 120),
            height=data.get("height", 40),
            text=data.get("text", ""),
            bg_color=data.get("bg_color", ""),
            text_color=data.get("text_color", ""),
            border_color=data.get("border_color", ""),
            border_width=data.get("border_width", 1),
            corner_radius=data.get("corner_radius", 3),
            font_size=data.get("font_size", 11),
            font_weight=data.get("font_weight", "normal"),
            alignment=data.get("alignment", "center"),
            min_val=data.get("min_val", 0.0),
            max_val=data.get("max_val", 100.0),
            value=data.get("value"),
            options=data.get("options"),
            group=data.get("group", ""),
            step=data.get("step", 1.0),
            placeholder=data.get("placeholder", ""),
            columns=data.get("columns"),
            items=data.get("items"),
            orientation=data.get("orientation", "horizontal"),
            event_handler=data.get("event_handler", ""),
            visible=data.get("visible", True),
            enabled=data.get("enabled", True),
            widget_id=data.get("id")
        )

    def clone(self, offset_x: int = 20, offset_y: int = 20) -> "Widget":
        data = self.to_dict()
        data["id"] = f"w_{uuid.uuid4().hex[:8]}"
        data["name"] = f"{self.widget_type}_{data['id'][:4]}"
        data["x"] += offset_x
        data["y"] += offset_y
        data["event_handler"] = f"on_{data['name']}_event"
        return Widget.from_dict(data)


class Project:
    """Manages the full UI design state, widgets list, canvas settings and undo/redo."""

    def __init__(
        self,
        name: str = "WindoseApp",
        canvas_width: int = 800,
        canvas_height: int = 500,
        bg_color: str = "#f5f6f7"
    ):
        self.name = name
        self.canvas_width = canvas_width
        self.canvas_height = canvas_height
        self.bg_color = bg_color
        self.grid_size = 8
        self.snap_to_grid = True
        self.widgets: List[Widget] = []
        self.selected_id: Optional[str] = None
        self.selected_ids: List[str] = []
        self.clipboard: List[Dict[str, Any]] = []

        # Undo / Redo stacks
        self._undo_stack: List[str] = []
        self._redo_stack: List[str] = []
        self._max_history = 50

    def add_widget(self, widget: Widget, record_history: bool = True) -> None:
        if record_history:
            self.push_undo()
        self.widgets.append(widget)
        self.select_widget(widget.id)

    def remove_widget(self, widget_id: str, record_history: bool = True) -> bool:
        for i, w in enumerate(self.widgets):
            if w.id == widget_id:
                if record_history:
                    self.push_undo()
                self.widgets.pop(i)
                if self.selected_id == widget_id:
                    self.selected_id = None
                if widget_id in self.selected_ids:
                    self.selected_ids.remove(widget_id)
                return True
        return False

    def get_widget(self, widget_id: Optional[str]) -> Optional[Widget]:
        if not widget_id:
            return None
        for w in self.widgets:
            if w.id == widget_id:
                return w
        return None

    def select_widget(self, widget_id: Optional[str], multi: bool = False) -> None:
        if not multi:
            self.selected_id = widget_id
            self.selected_ids = [widget_id] if widget_id else []
        else:
            if widget_id and widget_id not in self.selected_ids:
                self.selected_ids.append(widget_id)
                self.selected_id = widget_id
            elif widget_id and widget_id in self.selected_ids:
                self.selected_ids.remove(widget_id)
                self.selected_id = self.selected_ids[-1] if self.selected_ids else None

    def select_all(self) -> None:
        self.selected_ids = [w.id for w in self.widgets]
        self.selected_id = self.selected_ids[-1] if self.selected_ids else None

    def get_selected_widget(self) -> Optional[Widget]:
        return self.get_widget(self.selected_id)

    def get_selected_widgets(self) -> List[Widget]:
        return [w for w in self.widgets if w.id in self.selected_ids]

    def duplicate_selected(self) -> Optional[Widget]:
        cur = self.get_selected_widget()
        if not cur:
            return None
        self.push_undo()
        new_w = cur.clone(offset_x=self.grid_size * 2, offset_y=self.grid_size * 2)
        self.widgets.append(new_w)
        self.select_widget(new_w.id)
        return new_w

    def delete_selected(self) -> bool:
        if not self.selected_ids:
            return False
        self.push_undo()
        to_del = list(self.selected_ids)
        for wid in to_del:
            self.remove_widget(wid, record_history=False)
        self.selected_id = None
        self.selected_ids = []
        return True

    def copy_selected(self) -> None:
        self.clipboard = [w.to_dict() for w in self.get_selected_widgets()]

    def paste_clipboard(self) -> List[Widget]:
        if not self.clipboard:
            return []
        self.push_undo()
        new_widgets = []
        for d in self.clipboard:
            w = Widget.from_dict(d)
            cloned = w.clone(offset_x=self.grid_size * 2, offset_y=self.grid_size * 2)
            self.widgets.append(cloned)
            new_widgets.append(cloned)
        self.selected_ids = [w.id for w in new_widgets]
        self.selected_id = new_widgets[-1].id if new_widgets else None
        return new_widgets

    def nudge_selected(self, dx: int, dy: int) -> None:
        selected = self.get_selected_widgets()
        if not selected:
            return
        self.push_undo()
        for w in selected:
            w.x = max(0, w.x + dx)
            w.y = max(0, w.y + dy)

    def align_selected(self, mode: str) -> None:
        selected = self.get_selected_widgets()
        if not selected:
            return
        self.push_undo()

        if len(selected) == 1:
            # Align single widget relative to canvas
            w = selected[0]
            if mode == "left": w.x = 0
            elif mode == "right": w.x = max(0, self.canvas_width - w.width)
            elif mode == "top": w.y = 0
            elif mode == "bottom": w.y = max(0, self.canvas_height - w.height)
            elif mode in ("center_x", "center_h"): w.x = max(0, (self.canvas_width - w.width) // 2)
            elif mode in ("center_y", "center_v"): w.y = max(0, (self.canvas_height - w.height) // 2)
            return

        # Align multiple widgets relative to their bounding box
        min_x = min(w.x for w in selected)
        max_r = max(w.x + w.width for w in selected)
        min_y = min(w.y for w in selected)
        max_b = max(w.y + w.height for w in selected)
        mid_x = (min_x + max_r) // 2
        mid_y = (min_y + max_b) // 2

        if mode == "left":
            for w in selected: w.x = min_x
        elif mode == "right":
            for w in selected: w.x = max_r - w.width
        elif mode == "top":
            for w in selected: w.y = min_y
        elif mode == "bottom":
            for w in selected: w.y = max_b - w.height
        elif mode in ("center_x", "center_h"):
            for w in selected: w.x = mid_x - w.width // 2
        elif mode in ("center_y", "center_v"):
            for w in selected: w.y = mid_y - w.height // 2
        elif mode == "distribute_h":
            sorted_w = sorted(selected, key=lambda w: w.x)
            if len(sorted_w) > 2:
                total_w = sum(w.width for w in sorted_w)
                gap = (max_r - min_x - total_w) / (len(sorted_w) - 1)
                cur_x = float(min_x)
                for w in sorted_w:
                    w.x = int(round(cur_x))
                    cur_x += w.width + gap
        elif mode == "distribute_v":
            sorted_w = sorted(selected, key=lambda w: w.y)
            if len(sorted_w) > 2:
                total_h = sum(w.height for w in sorted_w)
                gap = (max_b - min_y - total_h) / (len(sorted_w) - 1)
                cur_y = float(min_y)
                for w in sorted_w:
                    w.y = int(round(cur_y))
                    cur_y += w.height + gap
        elif mode == "match_width":
            ref_w = selected[0].width
            for w in selected:
                w.width = ref_w
        elif mode == "match_height":
            ref_h = selected[0].height
            for w in selected:
                w.height = ref_h
        elif mode == "match_size":
            ref_w = selected[0].width
            ref_h = selected[0].height
            for w in selected:
                w.width = ref_w
                w.height = ref_h
        elif mode in ("center_canvas_h", "center_canvas_x"):
            for w in selected:
                w.x = max(0, (self.canvas_width - w.width) // 2)
        elif mode in ("center_canvas_v", "center_canvas_y"):
            for w in selected:
                w.y = max(0, (self.canvas_height - w.height) // 2)

    def select_in_rect(self, rx1: int, ry1: int, rx2: int, ry2: int, multi: bool = False) -> List[str]:
        """Selects all widgets intersecting or enclosed in world coordinates (rx1, ry1) to (rx2, ry2)."""
        min_x, max_x = min(rx1, rx2), max(rx1, rx2)
        min_y, max_y = min(ry1, ry2), max(ry1, ry2)

        hit_ids = []
        for w in self.widgets:
            wx2 = w.x + w.width
            wy2 = w.y + w.height
            # Check AABB intersection
            if not (wx2 < min_x or w.x > max_x or wy2 < min_y or w.y > max_y):
                hit_ids.append(w.id)

        if not multi:
            self.selected_ids.clear()
        for wid in hit_ids:
            if wid not in self.selected_ids:
                self.selected_ids.append(wid)
        self.selected_id = self.selected_ids[-1] if self.selected_ids else None
        return hit_ids

    def reorder(self, widget_id: str, action: str) -> None:
        """action: 'up', 'down', 'top', 'bottom'"""
        indices = [i for i, w in enumerate(self.widgets) if w.id == widget_id]
        if not indices:
            return
        idx = indices[0]
        self.push_undo()
        w = self.widgets.pop(idx)
        if action == "up" and idx < len(self.widgets):
            self.widgets.insert(idx + 1, w)
        elif action == "down" and idx > 0:
            self.widgets.insert(idx - 1, w)
        elif action == "top":
            self.widgets.append(w)
        elif action == "bottom":
            self.widgets.insert(0, w)
        else:
            self.widgets.insert(idx, w)

    def snap(self, val: int) -> int:
        if not self.snap_to_grid or self.grid_size <= 1:
            return val
        return round(val / self.grid_size) * self.grid_size

    def push_undo(self) -> None:
        state = self.to_json()
        self._undo_stack.append(state)
        if len(self._undo_stack) > self._max_history:
            self._undo_stack.pop(0)
        self._redo_stack.clear()

    def undo(self) -> bool:
        if not self._undo_stack:
            return False
        self._redo_stack.append(self.to_json())
        prev_state = self._undo_stack.pop()
        self.from_json(prev_state)
        return True

    def redo(self) -> bool:
        if not self._redo_stack:
            return False
        self._undo_stack.append(self.to_json())
        next_state = self._redo_stack.pop()
        self.from_json(next_state)
        return True

    def to_dict(self) -> Dict[str, Any]:
        return {
            "name": self.name,
            "canvas_width": self.canvas_width,
            "canvas_height": self.canvas_height,
            "bg_color": self.bg_color,
            "grid_size": self.grid_size,
            "snap_to_grid": self.snap_to_grid,
            "widgets": [w.to_dict() for w in self.widgets]
        }

    def to_json(self) -> str:
        return json.dumps(self.to_dict(), indent=2)

    def from_dict(self, data: Dict[str, Any]) -> None:
        self.name = data.get("name", "MyElfApp")
        self.canvas_width = data.get("canvas_width", 800)
        self.canvas_height = data.get("canvas_height", 480)
        self.bg_color = data.get("bg_color", "#0f172a")
        self.grid_size = data.get("grid_size", 8)
        self.snap_to_grid = data.get("snap_to_grid", True)
        self.widgets = [Widget.from_dict(wd) for wd in data.get("widgets", [])]
        self.selected_id = None
        self.selected_ids = []

    def from_json(self, json_str: str) -> None:
        data = json.loads(json_str)
        self.from_dict(data)

    def save_file(self, filepath: str) -> None:
        with open(filepath, "w", encoding="utf-8") as f:
            f.write(self.to_json())

    def load_file(self, filepath: str) -> None:
        with open(filepath, "r", encoding="utf-8") as f:
            self.from_json(f.read())
