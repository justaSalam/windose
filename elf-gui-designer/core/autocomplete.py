"""
IntelliSense & Autocomplete Engine for Windose Designer / ELF Studio.
Provides intelligent symbol completion for:
- Windose kernel.h APIs with signatures and documentation
- Dynamic project component handles (h_<widget_name>)
- C/C# keywords, types, and constants
- Code snippet expansions
"""

from typing import List, Dict, Any, Optional
from core.models import Project


class CompletionItem:
    """Represents a completion proposal."""

    def __init__(
        self,
        text: str,
        kind: str,
        detail: str = "",
        doc: str = "",
        insert_text: Optional[str] = None
    ):
        self.text = text
        self.kind = kind            # "api", "handle", "keyword", "type", "snippet", "const"
        self.detail = detail        # e.g. "(const char *title, int w, int h)"
        self.doc = doc              # Description of the function/variable
        self.insert_text = insert_text or text

    def __repr__(self) -> str:
        return f"<{self.kind}> {self.text}: {self.detail}"


# Windose OS kernel.h Complete Function Catalog
KERNEL_APIS = [
    CompletionItem(
        "k_create_window", "api",
        "(const char *title, int w, int h) -> handle_t",
        "Creates a top-level Aero desktop window with title and dimensions.",
        "k_create_window(\"${1:Window Title}\", ${2:800}, ${3:500})"
    ),
    CompletionItem(
        "k_create_button", "api",
        "(handle_t parent, int x, int y, int w, int h, const char *text) -> handle_t",
        "Creates a Windows 7 Aero push button.",
        "k_create_button(main_win, ${1:x}, ${2:y}, ${3:w}, ${4:h}, \"${5:Button}\")"
    ),
    CompletionItem(
        "k_create_label", "api",
        "(handle_t parent, int x, int y, const char *text) -> handle_t",
        "Creates a text label in Segoe UI.",
        "k_create_label(main_win, ${1:x}, ${2:y}, \"${3:Label text}\")"
    ),
    CompletionItem(
        "k_create_textfield", "api",
        "(handle_t parent, int x, int y, int w) -> handle_t",
        "Creates a single-line or multiline text input field.",
        "k_create_textfield(main_win, ${1:x}, ${2:y}, ${3:w})"
    ),
    CompletionItem(
        "k_textfield_setup", "api",
        "(handle_t obj, int maxlen, int password, int readonly) -> int",
        "Configures max length, password masking, and read-only mode for text fields.",
        "k_textfield_setup(${1:h_entry}, ${2:256}, ${3:0}, ${4:0});"
    ),
    CompletionItem(
        "k_get_text", "api",
        "(handle_t obj, char *buf, int max) -> int",
        "Retrieves text content from an entry field, label, or textarea into buffer.",
        "k_get_text(${1:h_entry}, ${2:buffer}, sizeof(${2:buffer}));"
    ),
    CompletionItem(
        "k_set_text", "api",
        "(handle_t obj, const char *text) -> int",
        "Updates the text or caption of any UI component.",
        "k_set_text(${1:h_label}, \"${2:Updated text}\");"
    ),
    CompletionItem(
        "k_create_checkbox", "api",
        "(handle_t parent, int x, int y, const char *text) -> handle_t",
        "Creates a toggle checkbox.",
        "k_create_checkbox(main_win, ${1:x}, ${2:y}, \"${3:Enable}\")"
    ),
    CompletionItem(
        "k_get_checked", "api",
        "(handle_t obj) -> int",
        "Returns 1 if checkbox or radio button is checked, 0 otherwise.",
        "k_get_checked(${1:h_checkbox})"
    ),
    CompletionItem(
        "k_set_checked", "api",
        "(handle_t obj, int checked) -> int",
        "Sets checked state (1 or 0) for checkbox or radio button.",
        "k_set_checked(${1:h_checkbox}, ${2:1});"
    ),
    CompletionItem(
        "k_create_radio", "api",
        "(handle_t parent, int x, int y, const char *text, const char *group) -> handle_t",
        "Creates a radio button belonging to an exclusive group.",
        "k_create_radio(main_win, ${1:x}, ${2:y}, \"${3:Option}\", \"${4:grp}\")"
    ),
    CompletionItem(
        "k_create_slider", "api",
        "(handle_t parent, int x, int y, int w, int horiz, int min, int max, int value) -> handle_t",
        "Creates an interactive range trackbar / slider.",
        "k_create_slider(main_win, ${1:x}, ${2:y}, ${3:w}, 1, ${4:0}, ${5:100}, ${6:50})"
    ),
    CompletionItem(
        "k_slider_get", "api",
        "(handle_t obj) -> int",
        "Gets the current integer value of a range slider.",
        "k_slider_get(${1:h_slider})"
    ),
    CompletionItem(
        "k_slider_set", "api",
        "(handle_t obj, int value) -> int",
        "Sets the value of a range slider.",
        "k_slider_set(${1:h_slider}, ${2:value});"
    ),
    CompletionItem(
        "k_create_progress", "api",
        "(handle_t parent, int x, int y, int w, int h) -> handle_t",
        "Creates an Aero green gloss progressbar.",
        "k_create_progress(main_win, ${1:x}, ${2:y}, ${3:w}, ${4:h})"
    ),
    CompletionItem(
        "k_progress_set", "api",
        "(handle_t obj, int value) -> int",
        "Sets the current progress percentage (0-100).",
        "k_progress_set(${1:h_progressbar}, ${2:value});"
    ),
    CompletionItem(
        "k_create_combo", "api",
        "(handle_t parent, int x, int y, int w) -> handle_t",
        "Creates a dropdown combobox selector.",
        "k_create_combo(main_win, ${1:x}, ${2:y}, ${3:w})"
    ),
    CompletionItem(
        "k_combo_add_item", "api",
        "(handle_t obj, const char *text) -> int",
        "Appends an option item to a combobox.",
        "k_combo_add_item(${1:h_combo}, \"${2:Option}\");"
    ),
    CompletionItem(
        "k_combo_get_selected", "api",
        "(handle_t obj) -> int",
        "Returns the zero-based index of the currently selected combobox item.",
        "k_combo_get_selected(${1:h_combo})"
    ),
    CompletionItem(
        "k_create_listbox", "api",
        "(handle_t parent, int x, int y, int w, int h) -> handle_t",
        "Creates a scrollable listbox component.",
        "k_create_listbox(main_win, ${1:x}, ${2:y}, ${3:w}, ${4:h})"
    ),
    CompletionItem(
        "k_list_add_item", "api",
        "(handle_t obj, const char *text) -> int",
        "Appends an item to a listbox.",
        "k_list_add_item(${1:h_listbox}, \"${2:Item text}\");"
    ),
    CompletionItem(
        "k_list_get_selected", "api",
        "(handle_t obj) -> int",
        "Gets selected item index in a listbox (-1 if none).",
        "k_list_get_selected(${1:h_listbox})"
    ),
    CompletionItem(
        "k_grid_create", "api",
        "(handle_t parent, int x, int y, int w, int h) -> handle_t",
        "Creates an Explorer multi-column DataGrid table.",
        "k_grid_create(main_win, ${1:x}, ${2:y}, ${3:w}, ${4:h})"
    ),
    CompletionItem(
        "k_grid_add_column", "api",
        "(handle_t obj, const char *title, int width) -> int",
        "Adds a column header to a DataGrid table.",
        "k_grid_add_column(${1:h_grid}, \"${2:Title}\", ${3:120});"
    ),
    CompletionItem(
        "k_grid_set_cell", "api",
        "(handle_t obj, int row, int col, const char *text) -> int",
        "Populates text in a specific cell of a DataGrid.",
        "k_grid_set_cell(${1:h_grid}, ${2:row}, ${3:col}, \"${4:value}\");"
    ),
    CompletionItem(
        "k_create_tabs", "api",
        "(handle_t parent, int x, int y, int w, int h) -> handle_t",
        "Creates a multi-page tab strip container.",
        "k_create_tabs(main_win, ${1:x}, ${2:y}, ${3:w}, ${4:h})"
    ),
    CompletionItem(
        "k_tab_add_page", "api",
        "(handle_t obj, const char *title) -> handle_t",
        "Adds a tab page to a tab strip and returns page container handle.",
        "k_tab_add_page(${1:h_tabs}, \"${2:General}\");"
    ),
    CompletionItem(
        "k_msgbox", "api",
        "(const char *title, const char *text, const char *buttons, int len) -> handle_t",
        "Shows an Aero modal dialog box with title, message text, and button flags.",
        "k_msgbox(\"${1:Information}\", \"${2:Operation completed successfully!}\", \"OK\", 2);"
    ),
    CompletionItem(
        "k_statusbar_create", "api",
        "(handle_t parent, int x, int y, int w) -> handle_t",
        "Creates a bottom status bar with panels.",
        "k_statusbar_create(main_win, ${1:x}, ${2:y}, ${3:w})"
    ),
    CompletionItem(
        "k_status_set_text", "api",
        "(handle_t obj, int index, const char *text) -> int",
        "Updates panel text in a status bar.",
        "k_status_set_text(${1:h_statusbar}, 0, \"${2:Ready}\");"
    ),
    CompletionItem(
        "k_numeric_create", "api",
        "(handle_t parent, int x, int y, int w, int min, int max, int inc, int value) -> handle_t",
        "Creates an up/down numeric spinner.",
        "k_numeric_create(main_win, ${1:x}, ${2:y}, ${3:w}, ${4:0}, ${5:100}, ${6:1}, ${7:0})"
    ),
    CompletionItem(
        "k_numeric_get", "api",
        "(handle_t obj) -> int",
        "Returns the numeric value of a spinbox.",
        "k_numeric_get(${1:h_numeric})"
    ),
    CompletionItem(
        "k_numeric_set", "api",
        "(handle_t obj, int value) -> int",
        "Sets the value of a spinbox.",
        "k_numeric_set(${1:h_numeric}, ${2:value});"
    ),
    CompletionItem(
        "k_colorpicker_create", "api",
        "(handle_t parent, int x, int y) -> handle_t",
        "Creates a palette color picker control.",
        "k_colorpicker_create(main_win, ${1:x}, ${2:y})"
    ),
    CompletionItem(
        "k_colorpicker_get", "api",
        "(handle_t obj) -> int",
        "Gets selected 32-bit ARGB color.",
        "k_colorpicker_get(${1:h_colorpicker})"
    ),
    CompletionItem(
        "k_notify_send", "api",
        "(const char *title, const char *text) -> int",
        "Sends a toast desktop notification in Windose OS.",
        "k_notify_send(\"${1:Windose Alert}\", \"${2:Notification message}\");"
    ),
    CompletionItem(
        "k_launch_app", "api",
        "(const char *name, const char *arg) -> int",
        "Launches another system application or executable by name.",
        "k_launch_app(\"${1:Calculator}\", \"\");"
    ),
    CompletionItem(
        "k_read_file", "api",
        "(const char *path, char *buf, int max) -> int",
        "Reads file content from disk/VFS into buffer. Returns bytes read.",
        "k_read_file(\"${1:/mnt/C/file.txt}\", ${2:buf}, sizeof(${2:buf}));"
    ),
    CompletionItem(
        "k_write_file", "api",
        "(const char *path, const char *buf, int len) -> int",
        "Writes buffer content to a file on disk or /dev device.",
        "k_write_file(\"${1:/dev/log}\", \"${2:Log message\\n}\", 0);"
    ),
    CompletionItem(
        "k_sleep", "api",
        "(int ms) -> int",
        "Yields CPU execution for specified milliseconds.",
        "k_sleep(${1:50});"
    ),
    CompletionItem(
        "k_close", "api",
        "(handle_t h) -> int",
        "Closes a window or destroys a UI component.",
        "k_close(main_win);"
    ),
    CompletionItem(
        "k_poll_event", "api",
        "(Event *out) -> int",
        "Polls the next UI event from the Windose event queue.",
        "k_poll_event(&ev)"
    ),
]

# C Language Keywords and Common Types
C_KEYWORDS = [
    CompletionItem("handle_t", "type", "Opaque component reference", "typedef int handle_t;"),
    CompletionItem("int", "type", "Signed 32-bit integer", "int"),
    CompletionItem("char", "type", "8-bit character", "char"),
    CompletionItem("void", "type", "Empty return type", "void"),
    CompletionItem("static", "keyword", "Internal linkage modifier", "static"),
    CompletionItem("return", "keyword", "Exit function with value", "return 0;"),
    CompletionItem("if", "keyword", "Conditional branch", "if (${1:condition}) {\n    ${2}\n}"),
    CompletionItem("else", "keyword", "Alternative branch", "else {\n    ${1}\n}"),
    CompletionItem("while", "keyword", "Loop while true", "while (${1:condition}) {\n    ${2}\n}"),
    CompletionItem("for", "keyword", "Iteration loop", "for (int i = 0; i < ${1:count}; i++) {\n    ${2}\n}"),
    CompletionItem("switch", "keyword", "Multi-way branch", "switch (${1:ev.type}) {\n    case ${2:EV_CLICK}:\n        ${3}\n        break;\n}"),
    CompletionItem("case", "keyword", "Switch case label", "case ${1:value}:"),
    CompletionItem("break", "keyword", "Break out of loop or switch", "break;"),
    CompletionItem("sizeof", "keyword", "Size in bytes of type or expression", "sizeof(${1})"),
    CompletionItem("NULL", "const", "Null pointer constant (0)", "NULL"),
    CompletionItem("printf", "api", "Formatted console output", "printf(\"${1:%s}\\n\", ${2});"),
    CompletionItem("snprintf", "api", "Formatted string buffer output", "snprintf(${1:buf}, sizeof(${1:buf}), \"${2:%d}\", ${3});"),
]

# Event constants
EVENT_CONSTS = [
    CompletionItem("EV_CLICK", "const", "1: Component click / push event", "EV_CLICK"),
    CompletionItem("EV_CLOSE", "const", "2: Window close request event", "EV_CLOSE"),
    CompletionItem("EV_KEY", "const", "3: Keyboard character / key event", "EV_KEY"),
    CompletionItem("EV_MOUSEMOVE", "const", "4: Mouse pointer movement", "EV_MOUSEMOVE"),
    CompletionItem("EV_MOUSEDOWN", "const", "5: Mouse button pressed", "EV_MOUSEDOWN"),
    CompletionItem("EV_MOUSEUP", "const", "6: Mouse button released", "EV_MOUSEUP"),
]

# Ready-to-use Code Snippets
COMMON_SNIPPETS = [
    CompletionItem(
        "snippet_msgbox", "snippet",
        "Display Alert Dialog",
        "Shows a pop-up dialog with an OK button.",
        'k_msgbox("${1:Notice}", "${2:Operation completed successfully!}", "OK", 2);'
    ),
    CompletionItem(
        "snippet_read_input", "snippet",
        "Read Text from Entry",
        "Buffers and reads input text from an entry widget.",
        'char input_buf[256];\nk_get_text(h_${1:entry_name}, input_buf, sizeof(input_buf));\nprintf("Input: %s\\n", input_buf);'
    ),
    CompletionItem(
        "snippet_update_label", "snippet",
        "Set Label Caption",
        "Updates the displayed text of a label.",
        'char label_buf[128];\nsnprintf(label_buf, sizeof(label_buf), "${1:Value: %d}", ${2:val});\nk_set_text(h_${3:label_name}, label_buf);'
    ),
    CompletionItem(
        "snippet_log", "snippet",
        "Write to System Log",
        "Writes debug output to /dev/log device.",
        'k_write_file("/dev/log", "${1:Debug event dispatched}\\n", 0);'
    ),
    CompletionItem(
        "snippet_notify", "snippet",
        "Send Desktop Notification",
        "Pops up a Windows 7 Aero notification toast.",
        'k_notify_send("${1:Windose Alert}", "${2:Task completed successfully}");'
    ),
    CompletionItem(
        "snippet_event_loop", "snippet",
        "Standard Windose Event Loop",
        "Processes window events until closed.",
        'Event ev;\nwhile (k_poll_event(&ev)) {\n    if (ev.type == EV_CLOSE && ev.handle == main_win) break;\n    if (ev.type == EV_CLICK) {\n        /* Handle button clicks */\n    }\n    k_sleep(10);\n}'
    )
]


class IntelliSenseEngine:
    """Provides filtered completions and symbol lookups based on current cursor context."""

    def __init__(self, project: Project):
        self.project = project

    def get_component_handles(self) -> List[CompletionItem]:
        """Generates dynamic completion symbols for all widgets currently in the project."""
        items = [
            CompletionItem("main_win", "handle", "Top-level Window handle_t", "Handle to the application main window.")
        ]
        for w in self.project.widgets:
            items.append(CompletionItem(
                f"h_{w.name}", "handle",
                f"{w.widget_type.capitalize()} handle_t ({w.width}x{w.height})",
                f"Component handle for '{w.name}' ({w.widget_type}) at ({w.x}, {w.y})."
            ))
            if w.event_handler:
                items.append(CompletionItem(
                    w.event_handler, "api",
                    "void (void) - Event Handler",
                    f"Signal callback function dispatched on interaction with '{w.name}'."
                ))
        return items

    def get_all_symbols(self) -> List[CompletionItem]:
        """Combines all APIs, keywords, dynamic handles, and snippets."""
        return (
            self.get_component_handles() +
            KERNEL_APIS +
            C_KEYWORDS +
            EVENT_CONSTS +
            COMMON_SNIPPETS
        )

    def query(self, prefix: str, limit: int = 15) -> List[CompletionItem]:
        """Finds matching completion candidates given a typed prefix."""
        clean = prefix.strip()
        if not clean:
            # Return most popular APIs and handles if no prefix
            return (self.get_component_handles() + KERNEL_APIS[:8])[:limit]

        low = clean.lower()
        exact_prefix: List[CompletionItem] = []
        substring_matches: List[CompletionItem] = []

        all_syms = self.get_all_symbols()
        for sym in all_syms:
            sym_low = sym.text.lower()
            if sym_low.startswith(low):
                exact_prefix.append(sym)
            elif low in sym_low or (sym.detail and low in sym.detail.lower()):
                substring_matches.append(sym)

        # Prioritize exact prefix matches, then substring
        results = exact_prefix + substring_matches
        # Deduplicate by text
        seen = set()
        deduped = []
        for r in results:
            if r.text not in seen:
                seen.add(r.text)
                deduped.append(r)

        return deduped[:limit]
