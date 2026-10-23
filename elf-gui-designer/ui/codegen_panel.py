"""
Code Generator Panel for Windose Designer / ELF Studio.
Provides syntax-highlighted editor with IntelliSense autocomplete, quick C snippets,
and multi-target export (Windose ELF C, LVGL, Raylib, Framebuffer, GTK, Cosmos, JSON).
"""

import os
import tkinter as tk
from tkinter import filedialog, messagebox
import customtkinter as ctk
from typing import Callable, Optional
from core.models import Project
from core.elf_builder import ElfBuilder
from ui.code_editor import CodeEditor
from codegen import (
    generate_windose_c,
    generate_lvgl_c,
    generate_raylib_c,
    generate_framebuffer_c,
    generate_gtk_c,
    generate_cosmos_cs,
    generate_json_schema
)


class CodeGenPanel(ctk.CTkFrame):
    """IDE-grade Code Generator & Editor View with IntelliSense and Snippets."""

    def __init__(self, master, project: Project, on_request_elf_build: Optional[Callable[[], None]] = None, **kwargs):
        super().__init__(master, fg_color="#101824", corner_radius=0, **kwargs)
        self.project = project
        self.on_request_elf_build = on_request_elf_build

        self._build_ui()
        self.refresh_code()

    def _build_ui(self) -> None:
        # Top toolbar
        bar = ctk.CTkFrame(self, fg_color="#182535", height=40, corner_radius=0)
        bar.pack(fill="x", side="top")

        lbl = ctk.CTkLabel(
            bar, text="⚡ C99 / ELF CODE STUDIO",
            font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold"),
            text_color="#93c5fd"
        )
        lbl.pack(side="left", padx=14)

        # Target Selector
        self.target_tabs = ctk.CTkSegmentedButton(
            bar,
            values=[
                "Windose ELF C (kernel.h)",
                "LVGL C (Embedded)",
                "Raylib C (Linux ELF)",
                "Standalone Framebuffer",
                "GTK 3 C (Desktop)",
                "Cosmos C#",
                "UI JSON"
            ],
            command=self._on_tab_changed,
            selected_color="#3464a8",
            selected_hover_color="#2563eb",
            unselected_color="#22364c",
            font=ctk.CTkFont(family="Segoe UI", size=10, weight="bold")
        )
        self.target_tabs.set("Windose ELF C (kernel.h)")
        self.target_tabs.pack(side="left", padx=10, pady=5)

        # Right buttons
        if self.on_request_elf_build:
            b_build = ctk.CTkButton(
                bar, text="🔨 Build ELF Binary", fg_color="#059669", hover_color="#047857",
                height=26, font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
                command=self.on_request_elf_build
            )
            b_build.pack(side="right", padx=(4, 12), pady=5)

        b_save_windose = ctk.CTkButton(
            bar, text="🚀 Save to Windose src/", fg_color="#0284c7", hover_color="#0369a1",
            height=26, font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            command=self._save_to_windose_src
        )
        b_save_windose.pack(side="right", padx=4, pady=5)

        b_export = ctk.CTkButton(
            bar, text="💾 Export File...", fg_color="#22364c", hover_color="#3464a8",
            height=26, font=ctk.CTkFont(family="Segoe UI", size=11), command=self._export_file
        )
        b_export.pack(side="right", padx=4, pady=5)

        b_copy = ctk.CTkButton(
            bar, text="📋 Copy", fg_color="#22364c", hover_color="#3464a8",
            height=26, width=65, font=ctk.CTkFont(family="Segoe UI", size=11), command=self._copy_code
        )
        b_copy.pack(side="right", padx=4, pady=5)

        # Snippet Quick-Action Ribbon
        snippet_bar = ctk.CTkFrame(self, fg_color="#131e2b", height=30, corner_radius=0)
        snippet_bar.pack(fill="x", side="top")

        ctk.CTkLabel(
            snippet_bar, text="✨ Quick Snippets:",
            font=ctk.CTkFont(family="Segoe UI", size=10, weight="bold"),
            text_color="#64748b"
        ).pack(side="left", padx=(12, 6))

        snippets = [
            ("+ MsgBox", 'k_msgbox("Notice", "Operation completed successfully!", "OK", 2);'),
            ("+ Read Input", 'char input_buf[256];\nk_get_text(h_entry, input_buf, sizeof(input_buf));'),
            ("+ Set Text", 'k_set_text(h_label, "New Status Text");'),
            ("+ Progress Bar", 'k_progress_set(h_progressbar, 75);'),
            ("+ Write Log", 'k_write_file("/dev/log", "Event dispatched\\n", 0);'),
            ("+ Launch App", 'k_launch_app("Calculator", "");'),
            ("+ Send Notification", 'k_notify_send("Windose Alert", "Task completed");'),
        ]

        for s_title, s_code in snippets:
            btn = ctk.CTkButton(
                snippet_bar, text=s_title, height=22, width=80,
                fg_color="#1c2a3a", hover_color="#3464a8",
                font=ctk.CTkFont(family="Segoe UI", size=9),
                command=lambda c=s_code: self._insert_snippet(c)
            )
            btn.pack(side="left", padx=2, pady=3)

        # Code Editor Component with Syntax Highlighting & IntelliSense
        self.editor = CodeEditor(self, self.project)
        self.editor.pack(fill="both", expand=True)

    def _on_tab_changed(self, choice: str) -> None:
        self.refresh_code()

    def get_current_code(self) -> str:
        tab = self.target_tabs.get()
        if "Windose" in tab:
            return generate_windose_c(self.project)
        elif "LVGL" in tab:
            return generate_lvgl_c(self.project)
        elif "Raylib" in tab:
            return generate_raylib_c(self.project)
        elif "Standalone Framebuffer" in tab:
            return generate_framebuffer_c(self.project)
        elif "GTK" in tab:
            return generate_gtk_c(self.project)
        elif "Cosmos" in tab:
            return generate_cosmos_cs(self.project)
        elif "JSON" in tab:
            return generate_json_schema(self.project)
        return ""

    def refresh_code(self) -> None:
        code = self.get_current_code()
        self.editor.set_code(code)

    def jump_to_handler(self, handler_name: str) -> bool:
        """Navigates editor cursor to the given function callback."""
        self.target_tabs.set("Windose ELF C (kernel.h)")
        self.refresh_code()
        return self.editor.jump_to_handler(handler_name)

    def _insert_snippet(self, snippet_code: str) -> None:
        self.editor.insert_snippet(snippet_code)

    def _copy_code(self) -> None:
        code = self.editor.get_code()
        self.clipboard_clear()
        self.clipboard_append(code)
        messagebox.showinfo("Copied", "Generated C code copied to clipboard!")

    def _export_file(self) -> None:
        tab = self.target_tabs.get()
        ext = ".c"
        initial_name = f"{self.project.name.lower()}_gui.c"

        if "Cosmos" in tab:
            ext = ".cs"
            initial_name = f"{self.project.name}Screen.cs"
        elif "JSON" in tab:
            ext = ".json"
            initial_name = f"{self.project.name.lower()}_layout.json"

        filepath = filedialog.asksaveasfilename(
            defaultextension=ext,
            initialfile=initial_name,
            filetypes=[("Source Files", f"*{ext}"), ("All Files", "*.*")]
        )
        if filepath:
            code = self.editor.get_code()
            with open(filepath, "w", encoding="utf-8") as f:
                f.write(code)
            messagebox.showinfo("Export Successful", f"Saved code to:\n{filepath}")

    def _save_to_windose_src(self) -> None:
        """Saves current C code directly to System/ELF/programs/src/<clean_name>.c."""
        bat = ElfBuilder.find_windose_build_bat()
        if not bat:
            messagebox.showwarning(
                "Not Found",
                "Windose build.bat could not be located.\nExpected at System/ELF/programs/build.bat."
            )
            return

        prog_dir = os.path.dirname(bat)
        src_dir = os.path.join(prog_dir, "src")
        os.makedirs(src_dir, exist_ok=True)

        clean_name = "".join(c if c.isalnum() or c == "_" else "_" for c in self.project.name.lower().strip())
        if not clean_name:
            clean_name = "app"

        src_file = os.path.join(src_dir, f"{clean_name}.c")
        code = self.editor.get_code()
        try:
            with open(src_file, "w", encoding="utf-8") as f:
                f.write(code)

            ans = messagebox.askyesno(
                "Saved to Windose OS",
                f"Source code saved successfully to:\n{src_file}\n\nWould you like to open the ELF Binary Suite now to compile it into {clean_name}.elf?"
            )
            if ans and self.on_request_elf_build:
                self.on_request_elf_build()
        except Exception as ex:
            messagebox.showerror("Save Error", f"Failed to save source file:\n{ex}")
