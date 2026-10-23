"""
Live Runtime Console & Simulator Event Terminal for Windose Designer.
Displays real-time user interaction logs, system call emulation, and allows
injecting commands and mock dialogs directly into the running simulation.
"""

import time
import tkinter as tk
from tkinter import messagebox
import customtkinter as ctk
from typing import Callable, Optional


class LiveConsolePanel(ctk.CTkFrame):
    """Collapsible bottom dock providing an interactive event log & terminal."""

    def __init__(
        self,
        master,
        on_command: Optional[Callable[[str], None]] = None,
        on_close: Optional[Callable[[], None]] = None,
        **kwargs
    ):
        super().__init__(master, fg_color="#0e1726", corner_radius=0, height=180, **kwargs)
        self.on_command = on_command
        self.on_close = on_close
        self.event_count = 0

        self._build_ui()

    def _build_ui(self) -> None:
        # Header Toolbar
        self.header = ctk.CTkFrame(self, fg_color="#131e2f", height=32, corner_radius=0)
        self.header.pack(fill="x", side="top")

        lbl_title = ctk.CTkLabel(
            self.header,
            text="🟢 RUNTIME CONSOLE & SIMULATOR LOGS",
            font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            text_color="#34d399"
        )
        lbl_title.pack(side="left", padx=12)

        self.lbl_badge = ctk.CTkLabel(
            self.header,
            text="0 Events",
            font=ctk.CTkFont(family="Segoe UI", size=10),
            text_color="#94a3b8"
        )
        self.lbl_badge.pack(side="left", padx=4)

        # Right Action Buttons
        if self.on_close:
            btn_close = ctk.CTkButton(
                self.header,
                text="✕",
                width=24,
                height=22,
                fg_color="transparent",
                hover_color="#ef4444",
                font=ctk.CTkFont(size=11, weight="bold"),
                command=self.on_close
            )
            btn_close.pack(side="right", padx=6)

        btn_copy = ctk.CTkButton(
            self.header,
            text="📋 Copy",
            width=54,
            height=22,
            fg_color="#1e2c3c",
            hover_color="#3464a8",
            font=ctk.CTkFont(size=10),
            command=self.copy_logs
        )
        btn_copy.pack(side="right", padx=3)

        btn_clear = ctk.CTkButton(
            self.header,
            text="🧹 Clear",
            width=54,
            height=22,
            fg_color="#1e2c3c",
            hover_color="#3464a8",
            font=ctk.CTkFont(size=10),
            command=self.clear_logs
        )
        btn_clear.pack(side="right", padx=3)

        # Main Log Output (Tk Text widget for maximum speed and rich coloring)
        log_container = ctk.CTkFrame(self, fg_color="#0a0f18", corner_radius=0)
        log_container.pack(fill="both", expand=True)

        self.text_log = tk.Text(
            log_container,
            bg="#0a0f18",
            fg="#e2e8f0",
            insertbackground="#38bdf8",
            font=("Consolas", 10),
            wrap="none",
            borderwidth=0,
            highlightthickness=0,
            padx=8,
            pady=4
        )
        v_scroll = tk.Scrollbar(log_container, orient="vertical", command=self.text_log.yview)
        self.text_log.configure(yscrollcommand=v_scroll.set)

        v_scroll.pack(side="right", fill="y")
        self.text_log.pack(side="left", fill="both", expand=True)

        # Style Tags
        self.text_log.tag_config("time", foreground="#64748b")
        self.text_log.tag_config("source", foreground="#38bdf8", font=("Consolas", 10, "bold"))
        self.text_log.tag_config("event", foreground="#34d399")
        self.text_log.tag_config("value", foreground="#a78bfa")
        self.text_log.tag_config("api", foreground="#fbbf24", font=("Consolas", 10, "bold"))
        self.text_log.tag_config("error", foreground="#f43f5e", font=("Consolas", 10, "bold"))
        self.text_log.tag_config("info", foreground="#94a3b8")

        # Interactive Command Input Bar
        cmd_bar = ctk.CTkFrame(self, fg_color="#111827", height=28, corner_radius=0)
        cmd_bar.pack(fill="x", side="bottom")

        lbl_prompt = ctk.CTkLabel(
            cmd_bar,
            text=" >",
            font=ctk.CTkFont(family="Consolas", size=11, weight="bold"),
            text_color="#38bdf8"
        )
        lbl_prompt.pack(side="left", padx=(8, 4))

        self.entry_cmd = ctk.CTkEntry(
            cmd_bar,
            placeholder_text="Enter test command (e.g. 'click btn_submit', 'msgbox Title Hello', 'help')...",
            height=24,
            fg_color="#182234",
            border_width=0,
            font=ctk.CTkFont(family="Consolas", size=10)
        )
        self.entry_cmd.pack(side="left", fill="x", expand=True, padx=(0, 6), pady=2)
        self.entry_cmd.bind("<Return>", self._on_cmd_enter)

        # Initial Welcome Message
        self.log("Simulator", "Windose Live Runtime Console initialized. Ready for events.", level="info")

    def log(self, source: str, message: str, level: str = "event") -> None:
        """Appends a timestamped, colorized entry to the console log."""
        self.event_count += 1
        self.lbl_badge.configure(text=f"{self.event_count} Events")

        timestamp = time.strftime("%H:%M:%S")

        self.text_log.config(state="normal")
        self.text_log.insert("end", f"[{timestamp}] ", "time")
        self.text_log.insert("end", f"[{source}] ", "source")

        tag = "info"
        if "k_" in message or "WM_" in message or "Spawned" in message or "API" in message:
            tag = "api"
        elif "Triggered" in message or "CLICKED" in message or "clicked" in message:
            tag = "event"
        elif "State:" in message or "Option changed" in message or "Radio checked" in message or "Committed" in message:
            tag = "value"
        elif level == "error" or "Error" in message or "error" in message:
            tag = "error"

        self.text_log.insert("end", f"{message}\n", tag)
        self.text_log.see("end")
        self.text_log.config(state="disabled")

    def clear_logs(self) -> None:
        """Clears all text in the console."""
        self.event_count = 0
        self.lbl_badge.configure(text="0 Events")
        self.text_log.config(state="normal")
        self.text_log.delete("1.0", "end")
        self.text_log.config(state="disabled")

    def copy_logs(self) -> None:
        """Copies all console text to the system clipboard."""
        text = self.text_log.get("1.0", "end").strip()
        if text:
            self.clipboard_clear()
            self.clipboard_append(text)
            self.log("Console", "All console logs copied to clipboard.", level="info")

    def _on_cmd_enter(self, event) -> None:
        cmd_text = self.entry_cmd.get().strip()
        if not cmd_text:
            return
        self.entry_cmd.delete(0, "end")
        self.log("CLI", f"> {cmd_text}", level="info")

        if cmd_text.lower() == "clear":
            self.clear_logs()
            return
        elif cmd_text.lower() == "help":
            self.log("Help", "Available commands:", level="info")
            self.log("Help", "  click <name>           - Simulate clicking a button or toggle", level="info")
            self.log("Help", "  msgbox <title> <msg>   - Trigger an Aero Glass message box modal", level="info")
            self.log("Help", "  set <name> <value>     - Update the value of an input widget", level="info")
            self.log("Help", "  clear                  - Clear console output", level="info")
            return

        if self.on_command:
            self.on_command(cmd_text)
