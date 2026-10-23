"""
Design Linter and Error Detection Engine for Windose Designer / ELF Studio.
Analyzes GUI layout and properties in real-time to detect overlapping controls,
out-of-bounds geometry, identifier collisions, invalid C syntax, and range anomalies.
Includes 1-click Auto-Fix capabilities.
"""

import re
from typing import List, Dict, Any, Optional, Tuple
from core.models import Project, Widget, WidgetType


class DiagnosticSeverity:
    ERROR = "error"       # Prevents compilation or causes fatal runtime crash
    WARNING = "warning"   # Layout defect or probable bug
    INFO = "info"         # Best practice or optimization suggestion


class DiagnosticItem:
    """Represents a single detected issue or linting notice."""

    def __init__(
        self,
        severity: str,
        message: str,
        widget_id: Optional[str] = None,
        widget_name: str = "",
        field: str = "",
        fix_action: Optional[str] = None
    ):
        self.severity = severity
        self.message = message
        self.widget_id = widget_id
        self.widget_name = widget_name
        self.field = field
        self.fix_action = fix_action

    def to_dict(self) -> Dict[str, Any]:
        return {
            "severity": self.severity,
            "message": self.message,
            "widget_id": self.widget_id,
            "widget_name": self.widget_name,
            "field": self.field,
            "fix_action": self.fix_action
        }

    def __repr__(self) -> str:
        prefix = "❌" if self.severity == DiagnosticSeverity.ERROR else ("⚠️" if self.severity == DiagnosticSeverity.WARNING else "ℹ️")
        w_info = f" [{self.widget_name}]" if self.widget_name else ""
        return f"{prefix}{w_info}: {self.message}"


# C / C# Keywords that cannot be used as widget identifiers
RESERVED_C_KEYWORDS = {
    "auto", "break", "case", "char", "const", "continue", "default", "do",
    "double", "else", "enum", "extern", "float", "for", "goto", "if",
    "int", "long", "register", "return", "short", "signed", "sizeof", "static",
    "struct", "switch", "typedef", "union", "unsigned", "void", "volatile", "while",
    "handle_t", "NULL", "true", "false", "bool", "inline", "restrict",
    # C# keywords
    "class", "namespace", "public", "private", "protected", "internal", "virtual",
    "override", "abstract", "sealed", "string", "object", "decimal", "var", "new"
}

# Hex color regex: #RGB, #RRGGBB, #AARRGGBB
HEX_COLOR_REGEX = re.compile(r"^#([A-Fa-f0-9]{3}|[A-Fa-f0-9]{6}|[A-Fa-f0-9]{8})$")
IDENTIFIER_REGEX = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")


class ProjectValidator:
    """Performs static analysis on a Project to detect errors and defects."""

    @staticmethod
    def validate_project(project: Project) -> List[DiagnosticItem]:
        diagnostics: List[DiagnosticItem] = []

        seen_names: Dict[str, str] = {}  # name -> widget_id
        radio_groups: Dict[str, List[Widget]] = {}

        # 1. Project level checks
        if project.canvas_width < 100 or project.canvas_height < 100:
            diagnostics.append(DiagnosticItem(
                severity=DiagnosticSeverity.ERROR,
                message=f"Canvas dimensions ({project.canvas_width}x{project.canvas_height}) are too small for a window.",
                field="canvas_size",
                fix_action="reset_resolution"
            ))

        if not IDENTIFIER_REGEX.match(project.name.replace(" ", "_")):
            diagnostics.append(DiagnosticItem(
                severity=DiagnosticSeverity.WARNING,
                message=f"Project name '{project.name}' contains special characters that may affect C output.",
                field="project_name",
                fix_action="sanitize_project_name"
            ))

        # 2. Per-widget checks
        for w in project.widgets:
            # A. Identifier Validation
            if not w.name or not w.name.strip():
                diagnostics.append(DiagnosticItem(
                    severity=DiagnosticSeverity.ERROR,
                    message="Widget identifier is empty. Every widget requires a valid C identifier.",
                    widget_id=w.id, widget_name=w.name, field="name", fix_action="assign_default_name"
                ))
            else:
                clean_name = w.name.strip()
                if clean_name.lower() in RESERVED_C_KEYWORDS:
                    diagnostics.append(DiagnosticItem(
                        severity=DiagnosticSeverity.ERROR,
                        message=f"Identifier '{clean_name}' is a reserved C/C# keyword. This causes a compiler error.",
                        widget_id=w.id, widget_name=clean_name, field="name", fix_action="prefix_identifier"
                    ))
                elif not IDENTIFIER_REGEX.match(clean_name):
                    diagnostics.append(DiagnosticItem(
                        severity=DiagnosticSeverity.ERROR,
                        message=f"Identifier '{clean_name}' has invalid characters (must start with letter/_ and contain only letters, numbers, _).",
                        widget_id=w.id, widget_name=clean_name, field="name", fix_action="sanitize_identifier"
                    ))
                elif clean_name in seen_names:
                    diagnostics.append(DiagnosticItem(
                        severity=DiagnosticSeverity.ERROR,
                        message=f"Duplicate identifier '{clean_name}'. Two components share the same variable name.",
                        widget_id=w.id, widget_name=clean_name, field="name", fix_action="deduplicate_identifier"
                    ))
                else:
                    seen_names[clean_name] = w.id

            # B. Window Boundary Checks
            if w.x < 0 or w.y < 0:
                diagnostics.append(DiagnosticItem(
                    severity=DiagnosticSeverity.WARNING,
                    message=f"Component '{w.name}' is positioned partially outside the window frame at ({w.x}, {w.y}).",
                    widget_id=w.id, widget_name=w.name, field="position", fix_action="clamp_to_window"
                ))
            elif (w.x + w.width) > project.canvas_width or (w.y + w.height) > project.canvas_height:
                overflow_x = max(0, (w.x + w.width) - project.canvas_width)
                overflow_y = max(0, (w.y + w.height) - project.canvas_height)
                diagnostics.append(DiagnosticItem(
                    severity=DiagnosticSeverity.WARNING,
                    message=f"Component '{w.name}' overflows window client area by ({overflow_x}px X, {overflow_y}px Y).",
                    widget_id=w.id, widget_name=w.name, field="position", fix_action="clamp_to_window"
                ))

            if w.width < 10 or w.height < 10:
                diagnostics.append(DiagnosticItem(
                    severity=DiagnosticSeverity.WARNING,
                    message=f"Component '{w.name}' size ({w.width}x{w.height}) is too small to display content.",
                    widget_id=w.id, widget_name=w.name, field="size", fix_action="reset_min_size"
                ))

            # C. Color Validity
            for col_field, col_val in [("bg_color", w.bg_color), ("text_color", w.text_color), ("border_color", w.border_color)]:
                if col_val and col_val not in ("transparent", "default") and not HEX_COLOR_REGEX.match(col_val):
                    diagnostics.append(DiagnosticItem(
                        severity=DiagnosticSeverity.WARNING,
                        message=f"Invalid color value '{col_val}' on '{w.name}'. Expected valid hex string (e.g. #3464a8).",
                        widget_id=w.id, widget_name=w.name, field=col_field, fix_action="fix_color_hex"
                    ))

            # D. Event Handler Check
            if w.event_handler and not IDENTIFIER_REGEX.match(w.event_handler.strip()):
                diagnostics.append(DiagnosticItem(
                    severity=DiagnosticSeverity.ERROR,
                    message=f"Event handler '{w.event_handler}' on '{w.name}' contains invalid C function characters.",
                    widget_id=w.id, widget_name=w.name, field="event_handler", fix_action="sanitize_event_handler"
                ))

            # E. Widget Type Specific Checks
            if w.widget_type == WidgetType.SLIDER:
                if w.min_val >= w.max_val:
                    diagnostics.append(DiagnosticItem(
                        severity=DiagnosticSeverity.ERROR,
                        message=f"Slider '{w.name}' minimum ({w.min_val}) must be less than maximum ({w.max_val}).",
                        widget_id=w.id, widget_name=w.name, field="range", fix_action="fix_slider_range"
                    ))
            elif w.widget_type == WidgetType.NUMERIC:
                if w.step <= 0:
                    diagnostics.append(DiagnosticItem(
                        severity=DiagnosticSeverity.WARNING,
                        message=f"Numeric spinner '{w.name}' step ({w.step}) must be greater than zero.",
                        widget_id=w.id, widget_name=w.name, field="step", fix_action="fix_numeric_step"
                    ))
            elif w.widget_type == WidgetType.RADIO:
                grp = w.group or "default_group"
                radio_groups.setdefault(grp, []).append(w)
            elif w.widget_type == WidgetType.DROPDOWN:
                if not w.options:
                    diagnostics.append(DiagnosticItem(
                        severity=DiagnosticSeverity.WARNING,
                        message=f"Dropdown '{w.name}' has no options defined.",
                        widget_id=w.id, widget_name=w.name, field="options", fix_action="add_default_options"
                    ))
            elif w.widget_type == WidgetType.TABS:
                if not w.items:
                    diagnostics.append(DiagnosticItem(
                        severity=DiagnosticSeverity.WARNING,
                        message=f"Tab strip '{w.name}' has no pages/tabs defined.",
                        widget_id=w.id, widget_name=w.name, field="items", fix_action="add_default_tabs"
                    ))

        # 3. Radio Group Checks
        for grp_name, radios in radio_groups.items():
            if len(radios) == 1:
                diagnostics.append(DiagnosticItem(
                    severity=DiagnosticSeverity.INFO,
                    message=f"Radio button '{radios[0].name}' is the only member of group '{grp_name}'. Radios usually work in sets of 2 or more.",
                    widget_id=radios[0].id, widget_name=radios[0].name, field="group"
                ))

        # 4. Severe Overlap Detection (Excluding panels/containers)
        for i in range(len(project.widgets)):
            w1 = project.widgets[i]
            if w1.widget_type == WidgetType.PANEL:
                continue
            for j in range(i + 1, len(project.widgets)):
                w2 = project.widgets[j]
                if w2.widget_type == WidgetType.PANEL:
                    continue
                # Check for near-total bounding box collision
                if (abs(w1.x - w2.x) < 4 and abs(w1.y - w2.y) < 4 and
                    abs(w1.width - w2.width) < 6 and abs(w1.height - w2.height) < 6):
                    diagnostics.append(DiagnosticItem(
                        severity=DiagnosticSeverity.WARNING,
                        message=f"Control '{w1.name}' and '{w2.name}' completely overlap at ({w1.x}, {w1.y}). One may be hidden.",
                        widget_id=w2.id, widget_name=w2.name, field="overlap", fix_action="nudge_widget"
                    ))

        return diagnostics

    @staticmethod
    def auto_fix_all(project: Project) -> int:
        """Automatically resolves detected issues across the project. Returns count of fixes applied."""
        fixes_applied = 0
        diagnostics = ProjectValidator.validate_project(project)

        seen_names = set()
        for w in project.widgets:
            # 1. Sanitize or rename empty/reserved/duplicate identifiers
            raw_name = w.name.strip() if w.name else f"{w.widget_type}"
            clean = re.sub(r"[^A-Za-z0-9_]", "_", raw_name)
            if not clean or clean[0].isdigit():
                clean = f"w_{clean}"

            if clean.lower() in RESERVED_C_KEYWORDS:
                clean = f"ui_{clean}"

            # Ensure uniqueness
            candidate = clean
            suffix = 1
            while candidate in seen_names:
                candidate = f"{clean}_{suffix}"
                suffix += 1

            if w.name != candidate:
                w.name = candidate
                w.event_handler = f"on_{candidate}_event"
                fixes_applied += 1
            seen_names.add(candidate)

            # 2. Clamp widgets to window boundaries
            orig_x, orig_y = w.x, w.y
            orig_w, orig_h = w.width, w.height

            w.x = max(0, min(project.canvas_width - 20, w.x))
            w.y = max(0, min(project.canvas_height - 20, w.y))
            w.width = max(16, min(project.canvas_width - w.x, w.width))
            w.height = max(16, min(project.canvas_height - w.y, w.height))

            if (w.x, w.y, w.width, w.height) != (orig_x, orig_y, orig_w, orig_h):
                fixes_applied += 1

            # 3. Fix colors if malformed
            if w.bg_color and not HEX_COLOR_REGEX.match(w.bg_color) and w.bg_color not in ("transparent", "default"):
                w.bg_color = w._default_bg_for_type(w.widget_type)
                fixes_applied += 1

            if w.text_color and not HEX_COLOR_REGEX.match(w.text_color):
                w.text_color = w._default_text_color_for_type(w.widget_type)
                fixes_applied += 1

            # 4. Fix slider ranges
            if w.widget_type == WidgetType.SLIDER and w.min_val >= w.max_val:
                w.min_val, w.max_val = 0.0, 100.0
                fixes_applied += 1

            if w.widget_type == WidgetType.NUMERIC and w.step <= 0:
                w.step = 1.0
                fixes_applied += 1

            if w.widget_type == WidgetType.DROPDOWN and not w.options:
                w.options = ["Option 1", "Option 2", "Option 3"]
                fixes_applied += 1

            if w.widget_type == WidgetType.TABS and not w.items:
                w.items = ["General", "Security", "Advanced"]
                fixes_applied += 1

        return fixes_applied
