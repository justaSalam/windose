"""
Syntax-Highlighting Code Editor with IntelliSense Autocomplete for Windose Designer.
Features:
- Real-time syntax coloring for C99 (kernel.h APIs, keywords, types, strings, comments, numbers)
- Line numbers gutter that synchronizes with scrolling
- Floating IntelliSense autocompletion popup (Ctrl+Space or auto-typing)
- Snippet insertion and jump-to-function capabilities
"""

import re
import tkinter as tk
from tkinter import font as tkfont
import customtkinter as ctk
from typing import Optional, Callable, List, Tuple
from core.models import Project
from core.autocomplete import IntelliSenseEngine, CompletionItem


class CodeEditor(ctk.CTkFrame):
    """IDE-grade code editor with syntax highlighting, line numbers, and IntelliSense."""

    # Syntax regular expressions
    RE_PREPROCESSOR = re.compile(r"^\s*#[a-zA-Z_]+\b.*$", re.MULTILINE)
    RE_COMMENT_SINGLE = re.compile(r"//.*$")
    RE_COMMENT_MULTI = re.compile(r"/\*.*?\*/", re.DOTALL)
    RE_STRING = re.compile(r'"(?:\\.|[^"\\])*"')
    RE_NUMBER = re.compile(r"\b(?:0x[0-9a-fA-F]+|\d+(?:\.\d+)?)\b")
    RE_KERNEL_API = re.compile(r"\b(k_[a-zA-Z0-9_]+)\b")
    RE_HANDLE = re.compile(r"\b(h_[a-zA-Z0-9_]+|main_win)\b")
    RE_KEYWORDS = re.compile(r"\b(auto|break|case|const|continue|default|do|else|enum|extern|for|goto|if|register|return|sizeof|static|struct|switch|typedef|union|volatile|while|NULL|true|false)\b")
    RE_TYPES = re.compile(r"\b(int|char|void|float|double|long|short|signed|unsigned|handle_t|size_t|uint8_t|uint16_t|uint32_t|uint64_t|int32_t|Event|FileStat|DriveStat)\b")

    def __init__(self, master, project: Project, on_content_changed: Optional[Callable[[], None]] = None, **kwargs):
        super().__init__(master, fg_color="#101824", corner_radius=0, **kwargs)
        self.project = project
        self.on_content_changed = on_content_changed
        self.autocomplete_engine = IntelliSenseEngine(project)

        # Popup state
        self._popup_window: Optional[tk.Toplevel] = None
        self._popup_listbox: Optional[tk.Listbox] = None
        self._popup_items: List[CompletionItem] = []
        self._popup_prefix = ""
        self._highlight_job = None

        self._build_editor()

    def _build_editor(self) -> None:
        # Container frame
        editor_container = ctk.CTkFrame(self, fg_color="#0e1722", corner_radius=0)
        editor_container.pack(fill="both", expand=True)

        # Gutter for line numbers
        self.gutter = tk.Text(
            editor_container,
            width=5,
            padx=4,
            pady=8,
            bg="#090e15",
            fg="#4e6580",
            font=("Consolas", 10),
            bd=0,
            takefocus=0,
            cursor="arrow",
            state="disabled",
            wrap="none"
        )
        self.gutter.pack(side="left", fill="y")

        # Main text widget
        self.text = tk.Text(
            editor_container,
            padx=10,
            pady=8,
            bg="#0e1722",
            fg="#e2e8f0",
            insertbackground="#60a5fa",
            selectbackground="#203d63",
            selectforeground="#ffffff",
            font=("Consolas", 10),
            bd=0,
            wrap="none",
            undo=True,
            maxundo=100
        )

        scroll_y = tk.Scrollbar(editor_container, orient="vertical", command=self._on_scroll_y)
        scroll_x = tk.Scrollbar(self, orient="horizontal", command=self.text.xview)

        self.text.configure(yscrollcommand=self._on_text_scroll_y, xscrollcommand=scroll_x.set)

        scroll_y.pack(side="right", fill="y")
        self.text.pack(side="left", fill="both", expand=True)
        scroll_x.pack(side="bottom", fill="x")

        # Configure Syntax Highlighting Tags
        self.text.tag_configure("preprocessor", foreground="#f43f5e")      # Rose
        self.text.tag_configure("comment", foreground="#64748b", font=("Consolas", 10, "italic")) # Muted
        self.text.tag_configure("string", foreground="#fde047")           # Amber Yellow
        self.text.tag_configure("number", foreground="#fb923c")           # Orange
        self.text.tag_configure("keyword", foreground="#60a5fa", font=("Consolas", 10, "bold")) # Aero Blue
        self.text.tag_configure("type", foreground="#a78bfa")              # Violet
        self.text.tag_configure("kernel_api", foreground="#34d399", font=("Consolas", 10, "bold")) # Emerald
        self.text.tag_configure("handle", foreground="#38bdf8")           # Cyan
        self.text.tag_configure("highlight_line", background="#182c44")   # Active focus line

        # Bindings
        self.text.bind("<KeyRelease>", self._on_key_release)
        self.text.bind("<KeyPress>", self._on_key_press)
        self.text.bind("<Button-1>", self._on_click)
        self.text.bind("<Control-space>", lambda e: self._trigger_autocomplete(force=True))
        self.text.bind("<Tab>", self._on_tab_press)

    def _on_scroll_y(self, *args):
        self.text.yview(*args)
        self.gutter.yview(*args)

    def _on_text_scroll_y(self, *args):
        self.gutter.yview_moveto(args[0])
        # Pass to scrollbar
        # Hide completion popup if scrolling
        self._dismiss_autocomplete()

    def set_code(self, code: str) -> None:
        """Loads code string and runs highlighter."""
        self.text.delete("1.0", "end")
        self.text.insert("1.0", code)
        self._update_line_numbers()
        self.highlight_syntax()

    def get_code(self) -> str:
        return self.text.get("1.0", "end-1c")

    def _update_line_numbers(self) -> None:
        lines_count = int(self.text.index("end-1c").split(".")[0])
        gutter_text = "\n".join(str(i) for i in range(1, lines_count + 1))
        self.gutter.config(state="normal")
        self.gutter.delete("1.0", "end")
        self.gutter.insert("1.0", gutter_text)
        self.gutter.config(state="disabled")

    def highlight_syntax(self) -> None:
        """Applies real-time syntax coloring across all tokens."""
        content = self.text.get("1.0", "end-1c")
        for tag in ["preprocessor", "comment", "string", "number", "keyword", "type", "kernel_api", "handle"]:
            self.text.tag_remove(tag, "1.0", "end")

        # 1. Multi-line comments
        for match in self.RE_COMMENT_MULTI.finditer(content):
            start = f"1.0+{match.start()}c"
            end = f"1.0+{match.end()}c"
            self.text.tag_add("comment", start, end)

        # 2. Preprocessor directives
        for match in self.RE_PREPROCESSOR.finditer(content):
            start = f"1.0+{match.start()}c"
            end = f"1.0+{match.end()}c"
            self.text.tag_add("preprocessor", start, end)

        # 3. Strings
        for match in self.RE_STRING.finditer(content):
            start = f"1.0+{match.start()}c"
            end = f"1.0+{match.end()}c"
            self.text.tag_add("string", start, end)

        # 4. Numbers
        for match in self.RE_NUMBER.finditer(content):
            start = f"1.0+{match.start()}c"
            end = f"1.0+{match.end()}c"
            self.text.tag_add("number", start, end)

        # 5. Keywords
        for match in self.RE_KEYWORDS.finditer(content):
            start = f"1.0+{match.start()}c"
            end = f"1.0+{match.end()}c"
            self.text.tag_add("keyword", start, end)

        # 6. Types
        for match in self.RE_TYPES.finditer(content):
            start = f"1.0+{match.start()}c"
            end = f"1.0+{match.end()}c"
            self.text.tag_add("type", start, end)

        # 7. Kernel APIs
        for match in self.RE_KERNEL_API.finditer(content):
            start = f"1.0+{match.start()}c"
            end = f"1.0+{match.end()}c"
            self.text.tag_add("kernel_api", start, end)

        # 8. Handles
        for match in self.RE_HANDLE.finditer(content):
            start = f"1.0+{match.start()}c"
            end = f"1.0+{match.end()}c"
            self.text.tag_add("handle", start, end)

        # 9. Single line comments (highest priority to override any token inside)
        lines = content.split("\n")
        for line_no, line in enumerate(lines, start=1):
            idx = line.find("//")
            if idx != -1:
                start = f"{line_no}.{idx}"
                end = f"{line_no}.end"
                self.text.tag_add("comment", start, end)

    def _schedule_highlight(self) -> None:
        if self._highlight_job:
            self.after_cancel(self._highlight_job)
        self._highlight_job = self.after(150, self.highlight_syntax)

    def _on_key_release(self, event) -> None:
        self._update_line_numbers()
        self._schedule_highlight()
        if self.on_content_changed:
            self.on_content_changed()

        # Check for typing characters to trigger autocomplete
        if event.char and (event.char.isalnum() or event.char in ("_", ".")):
            self._trigger_autocomplete(force=False)
        elif event.keysym in ("BackSpace", "Delete"):
            self._trigger_autocomplete(force=False)
        elif event.keysym in ("Return", "space", "semicolon"):
            self._dismiss_autocomplete()

    def _on_key_press(self, event) -> str:
        # Route arrow keys to autocomplete popup if visible
        if self._popup_window and self._popup_window.winfo_exists():
            if event.keysym == "Down":
                cur = self._popup_listbox.curselection()
                idx = (cur[0] + 1) if cur else 0
                if idx < self._popup_listbox.size():
                    self._popup_listbox.selection_clear(0, "end")
                    self._popup_listbox.selection_set(idx)
                    self._popup_listbox.see(idx)
                return "break"
            elif event.keysym == "Up":
                cur = self._popup_listbox.curselection()
                idx = (cur[0] - 1) if cur else 0
                if idx >= 0:
                    self._popup_listbox.selection_clear(0, "end")
                    self._popup_listbox.selection_set(idx)
                    self._popup_listbox.see(idx)
                return "break"
            elif event.keysym in ("Return", "Tab"):
                self._apply_autocomplete_selection()
                return "break"
            elif event.keysym == "Escape":
                self._dismiss_autocomplete()
                return "break"

        return ""

    def _on_tab_press(self, event) -> str:
        if self._popup_window and self._popup_window.winfo_exists():
            self._apply_autocomplete_selection()
            return "break"
        # Insert 4 spaces instead of tab
        self.text.insert("insert", "    ")
        return "break"

    def _on_click(self, event) -> None:
        self._dismiss_autocomplete()

    # -------------------------------------------------------------
    # IntelliSense Autocompletion Popup
    # -------------------------------------------------------------
    def _get_current_word_and_prefix(self) -> Tuple[str, str]:
        """Extracts the partial identifier being typed before the cursor."""
        cursor_index = self.text.index("insert")
        line, col = map(int, cursor_index.split("."))
        line_text = self.text.get(f"{line}.0", f"{line}.{col}")
        match = re.search(r"([a-zA-Z0-9_]+)$", line_text)
        if match:
            return match.group(1), f"{line}.{col - len(match.group(1))}"
        return "", cursor_index

    def _trigger_autocomplete(self, force: bool = False) -> None:
        prefix, start_idx = self._get_current_word_and_prefix()
        if not force and len(prefix) < 2:
            self._dismiss_autocomplete()
            return

        matches = self.autocomplete_engine.query(prefix)
        if not matches:
            self._dismiss_autocomplete()
            return

        self._popup_items = matches
        self._popup_prefix = prefix
        self._show_autocomplete_popup(matches)

    def _show_autocomplete_popup(self, items: List[CompletionItem]) -> None:
        # Get screen coordinates of text cursor
        cursor_bbox = self.text.bbox("insert")
        if not cursor_bbox:
            return

        cx, cy, cw, ch = cursor_bbox
        # Absolute screen coordinates
        root_x = self.text.winfo_rootx() + cx
        root_y = self.text.winfo_rooty() + cy + ch + 2

        if not self._popup_window or not self._popup_window.winfo_exists():
            self._popup_window = tk.Toplevel(self)
            self._popup_window.wm_overrideredirect(True)
            self._popup_window.attributes("-topmost", True)

            frame = tk.Frame(self._popup_window, bg="#1c2a3a", bd=1, relief="solid")
            frame.pack(fill="both", expand=True)

            # Header info
            hdr = tk.Label(frame, text="⚡ IntelliSense (kernel.h)", bg="#22364c", fg="#93c5fd", font=("Segoe UI", 8, "bold"), anchor="w", padx=6, pady=2)
            hdr.pack(fill="x")

            self._popup_listbox = tk.Listbox(
                frame,
                bg="#131e2b",
                fg="#f1f5f9",
                selectbackground="#3464a8",
                selectforeground="#ffffff",
                font=("Consolas", 9),
                bd=0,
                height=min(8, len(items)),
                width=45,
                activestyle="none",
                highlightthickness=0
            )
            self._popup_listbox.pack(fill="both", expand=True)
            self._popup_listbox.bind("<Double-Button-1>", lambda e: self._apply_autocomplete_selection())

        self._popup_listbox.delete(0, "end")
        for it in items:
            kind_badge = "⚡ API" if it.kind == "api" else ("📦 OBJ" if it.kind == "handle" else ("✨ SNIP" if it.kind == "snippet" else "🔑 KEY"))
            self._popup_listbox.insert("end", f" {kind_badge:<7} {it.text}")

        self._popup_listbox.selection_set(0)
        self._popup_window.geometry(f"+{root_x}+{root_y}")

    def _dismiss_autocomplete(self) -> None:
        if self._popup_window and self._popup_window.winfo_exists():
            self._popup_window.destroy()
            self._popup_window = None

    def _apply_autocomplete_selection(self) -> None:
        if not self._popup_window or not self._popup_listbox:
            return

        cur = self._popup_listbox.curselection()
        if not cur or cur[0] >= len(self._popup_items):
            self._dismiss_autocomplete()
            return

        item = self._popup_items[cur[0]]
        self._dismiss_autocomplete()

        # Replace the prefix with the item insert text
        cursor_index = self.text.index("insert")
        line, col = map(int, cursor_index.split("."))
        start_idx = f"{line}.{col - len(self._popup_prefix)}"

        # Strip placeholder markers like ${1:value}
        clean_insert = re.sub(r"\$\{\d+:(.*?)\}", r"\1", item.insert_text)
        clean_insert = re.sub(r"\$\{\d+\}", "", clean_insert)

        self.text.delete(start_idx, cursor_index)
        self.text.insert(start_idx, clean_insert)

        self.highlight_syntax()
        self._update_line_numbers()
        if self.on_content_changed:
            self.on_content_changed()

    # -------------------------------------------------------------
    # Navigation & Snippet Insertion
    # -------------------------------------------------------------
    def jump_to_handler(self, handler_name: str) -> bool:
        """Finds callback function or generates it, and moves cursor inside its body."""
        content = self.get_code()
        # Search for exact match or flexible variants
        candidates = [
            handler_name,
            handler_name.replace("_event", "") if handler_name.endswith("_event") else f"{handler_name}_event"
        ]

        for cand in candidates:
            patterns = [
                rf"(static\s+void\s+{re.escape(cand)}\s*\([^)]*\)\s*\{{)",
                rf"(void\s+{re.escape(cand)}\s*\([^)]*\)\s*\{{)"
            ]
            for pat in patterns:
                match = re.search(pat, content)
                if match:
                    char_idx = match.end()
                    pos = f"1.0+{char_idx}c"
                    self.text.focus_set()
                    self.text.mark_set("insert", pos)
                    self.text.see(pos)
                    # Insert a newline and indent if empty
                    line_no = self.text.index(pos).split(".")[0]
                    self.text.tag_remove("highlight_line", "1.0", "end")
                    self.text.tag_add("highlight_line", f"{line_no}.0", f"{int(line_no)+2}.0")
                    self.after(1500, lambda: self.text.tag_remove("highlight_line", "1.0", "end"))
                    return True

        # If not found, intelligently synthesize callback skeleton in the signal callbacks section
        skeleton = f"\n\nstatic void {handler_name}(void) {{\n    /* TODO: Add your event logic here */\n    \n}}"
        # Find where callbacks section is or insert before main()
        main_idx = content.find("int main(")
        if main_idx != -1:
            ins_pos = f"1.0+{main_idx}c"
            self.text.insert(ins_pos, skeleton)
        else:
            self.text.insert("end", skeleton)

        self.highlight_syntax()
        self._update_line_numbers()
        return self.jump_to_handler(handler_name)

    def insert_snippet(self, snippet_code: str) -> None:
        """Inserts formatted snippet code at current cursor position."""
        self.text.focus_set()
        self.text.insert("insert", snippet_code + "\n")
        self.highlight_syntax()
        self._update_line_numbers()
        if self.on_content_changed:
            self.on_content_changed()
