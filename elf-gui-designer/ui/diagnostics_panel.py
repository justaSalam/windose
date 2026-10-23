"""
Diagnostics, Linting & Problems Panel for Windose Designer.
Displays real-time layout validation errors, identifier clashes, and boundary warnings.
Provides 1-click 'Jump to Widget' and 'Auto-Fix All Issues'.
"""

import customtkinter as ctk
from typing import Callable, Optional, List
from core.models import Project
from core.validator import ProjectValidator, DiagnosticItem, DiagnosticSeverity


class DiagnosticsPanel(ctk.CTkFrame):
    """Bottom drawer or tabbed panel displaying design errors, warnings, and fixes."""

    def __init__(
        self,
        master,
        project: Project,
        on_select_widget: Callable[[str], None],
        on_project_modified: Callable[[], None],
        **kwargs
    ):
        super().__init__(master, fg_color="#101824", corner_radius=0, **kwargs)
        self.project = project
        self.on_select_widget = on_select_widget
        self.on_project_modified = on_project_modified
        self.diagnostics: List[DiagnosticItem] = []

        self._build_ui()
        self.refresh_diagnostics()

    def _build_ui(self) -> None:
        # Top toolbar
        self.top_bar = ctk.CTkFrame(self, fg_color="#182535", height=34, corner_radius=0)
        self.top_bar.pack(fill="x", side="top")

        self.lbl_title = ctk.CTkLabel(
            self.top_bar,
            text="🩺 DESIGN DIAGNOSTICS & LINTING",
            font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            text_color="#93c5fd"
        )
        self.lbl_title.pack(side="left", padx=12)

        self.lbl_summary = ctk.CTkLabel(
            self.top_bar,
            text="0 Errors, 0 Warnings",
            font=ctk.CTkFont(family="Segoe UI", size=10),
            text_color="#64748b"
        )
        self.lbl_summary.pack(side="left", padx=8)

        # Right action buttons
        self.btn_autofix = ctk.CTkButton(
            self.top_bar,
            text="⚡ Auto-Fix All Issues",
            fg_color="#059669",
            hover_color="#047857",
            height=24,
            font=ctk.CTkFont(family="Segoe UI", size=10, weight="bold"),
            command=self._on_autofix_clicked
        )
        self.btn_autofix.pack(side="right", padx=10, pady=4)

        b_refresh = ctk.CTkButton(
            self.top_bar,
            text="⟳ Check",
            fg_color="#22364c",
            hover_color="#3464a8",
            width=60,
            height=24,
            font=ctk.CTkFont(family="Segoe UI", size=10),
            command=self.refresh_diagnostics
        )
        b_refresh.pack(side="right", padx=2, pady=4)

        # Scrollable items list
        self.items_scroll = ctk.CTkScrollableFrame(self, fg_color="#0e1722", corner_radius=0)
        self.items_scroll.pack(fill="both", expand=True)

    def refresh_diagnostics(self) -> List[DiagnosticItem]:
        """Runs the validator on the current project and renders all diagnostic rows."""
        self.diagnostics = ProjectValidator.validate_project(self.project)

        # Clear existing rows
        for child in self.items_scroll.winfo_children():
            child.destroy()

        err_count = sum(1 for d in self.diagnostics if d.severity == DiagnosticSeverity.ERROR)
        warn_count = sum(1 for d in self.diagnostics if d.severity == DiagnosticSeverity.WARNING)

        if err_count > 0:
            self.lbl_summary.configure(text=f"{err_count} Error(s), {warn_count} Warning(s)", text_color="#f87171")
        elif warn_count > 0:
            self.lbl_summary.configure(text=f"0 Errors, {warn_count} Warning(s)", text_color="#fbbf24")
        else:
            self.lbl_summary.configure(text="✓ No issues detected (Clean)", text_color="#34d399")

        if not self.diagnostics:
            empty_lbl = ctk.CTkLabel(
                self.items_scroll,
                text="✓ No design errors or layout warnings detected.\nYour Windose UI layout and C identifiers are clean and ready to build.",
                font=ctk.CTkFont(family="Segoe UI", size=11),
                text_color="#64748b"
            )
            empty_lbl.pack(pady=24)
            self.btn_autofix.configure(state="disabled")
            return self.diagnostics

        self.btn_autofix.configure(state="normal")
        for diag in self.diagnostics:
            self._add_diagnostic_row(diag)

        return self.diagnostics

    def _add_diagnostic_row(self, diag: DiagnosticItem) -> None:
        row = ctk.CTkFrame(self.items_scroll, fg_color="#142130", height=32, corner_radius=4)
        row.pack(fill="x", pady=2, padx=4)

        # Severity icon & color
        if diag.severity == DiagnosticSeverity.ERROR:
            badge_text = "❌ ERROR"
            badge_color = "#ef4444"
        elif diag.severity == DiagnosticSeverity.WARNING:
            badge_text = "⚠️ WARN"
            badge_color = "#f59e0b"
        else:
            badge_text = "ℹ️ INFO"
            badge_color = "#38bdf8"

        ctk.CTkLabel(
            row, text=badge_text,
            font=ctk.CTkFont(family="Segoe UI", size=9, weight="bold"),
            text_color=badge_color, width=65, anchor="w"
        ).pack(side="left", padx=(8, 4))

        if diag.widget_name:
            ctk.CTkLabel(
                row, text=f"[{diag.widget_name}]",
                font=ctk.CTkFont(family="Consolas", size=10, weight="bold"),
                text_color="#93c5fd", anchor="w"
            ).pack(side="left", padx=4)

        # Message
        ctk.CTkLabel(
            row, text=diag.message,
            font=ctk.CTkFont(family="Segoe UI", size=10),
            text_color="#e2e8f0", anchor="w"
        ).pack(side="left", padx=6, fill="x", expand=True)

        # Jump button
        if diag.widget_id:
            b_jump = ctk.CTkButton(
                row, text="🔍 Inspect", width=65, height=22,
                fg_color="#22364c", hover_color="#3464a8",
                font=ctk.CTkFont(family="Segoe UI", size=9),
                command=lambda wid=diag.widget_id: self.on_select_widget(wid)
            )
            b_jump.pack(side="right", padx=6, pady=4)

    def _on_autofix_clicked(self) -> None:
        fixes = ProjectValidator.auto_fix_all(self.project)
        if fixes > 0:
            self.project.push_undo()
            self.on_project_modified()
            self.refresh_diagnostics()
