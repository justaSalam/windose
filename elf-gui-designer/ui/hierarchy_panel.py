"""
Widget Hierarchy and Outline Panel for ELF Studio.
Displays widget layout tree, layer ordering, and quick selection.
"""

import tkinter as tk
from tkinter import ttk
import customtkinter as ctk
from typing import Callable, Optional
from core.models import Project, Widget


class HierarchyPanel(ctk.CTkFrame):
    """Widget Tree / Layers hierarchy sidebar."""

    def __init__(
        self,
        master,
        project: Project,
        on_widget_selected: Callable[[Optional[str]], None],
        on_reorder_widget: Callable[[str, str], None],
        **kwargs
    ):
        super().__init__(master, fg_color="#16202c", corner_radius=0, **kwargs)
        self.project = project
        self.on_widget_selected = on_widget_selected
        self.on_reorder_widget = on_reorder_widget

        self._build_ui()
        self.refresh_tree()

    def _build_ui(self) -> None:
        title_bar = ctk.CTkFrame(self, fg_color="transparent", height=32)
        title_bar.pack(fill="x", padx=12, pady=(12, 6))

        ctk.CTkLabel(
            title_bar, text="WIDGET HIERARCHY",
            font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            text_color="#93c5fd"
        ).pack(side="left")

        # Reorder buttons on top right
        btn_down = ctk.CTkButton(
            title_bar, text="▼", width=24, height=24, fg_color="#1e2c3c", hover_color="#3464a8",
            command=lambda: self._on_reorder("down")
        )
        btn_down.pack(side="right", padx=2)

        btn_up = ctk.CTkButton(
            title_bar, text="▲", width=24, height=24, fg_color="#1e2c3c", hover_color="#3464a8",
            command=lambda: self._on_reorder("up")
        )
        btn_up.pack(side="right", padx=2)

        # Tk Listbox with dark styling
        list_frame = ctk.CTkFrame(self, fg_color="#111923", corner_radius=6)
        list_frame.pack(fill="both", expand=True, padx=10, pady=(0, 10))

        self.listbox = tk.Listbox(
            list_frame,
            bg="#111923",
            fg="#e6edf3",
            selectbackground="#3464a8",
            selectforeground="#ffffff",
            bd=0,
            font=("Segoe UI", 10),
            activestyle="none",
            highlightthickness=0
        )
        scroll = tk.Scrollbar(list_frame, orient="vertical", command=self.listbox.yview)
        self.listbox.configure(yscrollcommand=scroll.set)

        scroll.pack(side="right", fill="y")
        self.listbox.pack(side="left", fill="both", expand=True)

        self.listbox.bind("<<ListboxSelect>>", self._on_item_selected)

    def refresh_tree(self) -> None:
        self.listbox.delete(0, "end")
        selected_idx = None
        for i, w in enumerate(self.project.widgets):
            display = f"[{w.widget_type.upper()[:4]}]  {w.name}  ({w.width}x{w.height})"
            self.listbox.insert("end", display)
            if w.id == self.project.selected_id:
                selected_idx = i

        if selected_idx is not None:
            self.listbox.selection_set(selected_idx)
            self.listbox.see(selected_idx)

    def set_selected_widget(self, widget_id: Optional[str]) -> None:
        """Fast selection update without rebuilding entire listbox."""
        self.listbox.selection_clear(0, "end")
        if not widget_id:
            return
        for i, w in enumerate(self.project.widgets):
            if w.id == widget_id:
                self.listbox.selection_set(i)
                self.listbox.see(i)
                break

    def _on_item_selected(self, event) -> None:
        sel = self.listbox.curselection()
        if sel:
            idx = sel[0]
            if 0 <= idx < len(self.project.widgets):
                w = self.project.widgets[idx]
                self.project.select_widget(w.id)
                self.on_widget_selected(w.id)

    def _on_reorder(self, action: str) -> None:
        if self.project.selected_id:
            self.on_reorder_widget(self.project.selected_id, action)
            self.refresh_tree()
