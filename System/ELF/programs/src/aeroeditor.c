/**
 * Windose OS Native AeroEditor IDE (ELF)
 * Advanced C Integrated Development Environment:
 * - Live Autocomplete with dynamic suggestion pills and parameter signature hints
 * - Dual-mode Syntax Checking (structural balance & compiler-assisted)
 * - Error Outlining with F4 Quick-Jump navigation directly to offending lines
 * - One-click / F5 Build & Run pipeline powered by TinyCC x86_64
 * - Starter project templates (GUI Window, CLI Console, Canvas Drawing)
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

#define MAX_CODE_SIZE 65536

#define AC_WIDTH       280
#define AC_HEIGHT      175
#define AC_ITEM_HEIGHT 20
#define AC_MAX_ITEMS   6
#define MAX_MATCHES    24

/* Component Handles */
static handle_t main_win = 0;

/* Menu handles */
static handle_t m_new = 0;
static handle_t m_tmpl_gui = 0;
static handle_t m_tmpl_cli = 0;
static handle_t m_tmpl_canvas = 0;
static handle_t m_open = 0;
static handle_t m_save = 0;
static handle_t m_saveas = 0;
static handle_t m_exit = 0;

static handle_t m_clear = 0;

static handle_t m_check_syntax = 0;
static handle_t m_compile = 0;
static handle_t m_build_run = 0;
static handle_t m_run = 0;
static handle_t m_jump_err = 0;
static handle_t m_clean = 0;

static handle_t m_about = 0;

/* Toolbar handles */
static handle_t h_btn_new = 0;
static handle_t h_btn_open = 0;
static handle_t h_btn_save = 0;
static handle_t h_btn_check = 0;
static handle_t h_btn_build = 0;
static handle_t h_btn_run = 0;
static handle_t h_lbl_filename = 0;

/* Editor, Output, and Floating Autocomplete handles */
static handle_t h_text_buffer = 0;
static handle_t h_ac_canvas = 0;
static handle_t h_lbl_output = 0;
static handle_t h_output_buffer = 0;
static handle_t h_status_bar = 0;

static char current_file[256] = "/mnt/untitled.c";
static int last_err_line = 0;
static char last_err_msg[256] = "";

/* Floating Autocomplete state */
static int ac_visible = 0;
static int ac_selected = 0;
static int ac_match_count = 0;
static int ac_matches[MAX_MATCHES];
static int ac_x = 0;
static int ac_y = 0;
static int cur_prefix_start = 0;
static int cur_prefix_end = 0;

/* ---------------- Symbol Database ---------------- */

typedef struct {
    const char *name;
    const char *sig;
    const char *desc;
} SymbolEntry;

static const SymbolEntry SYMBOL_DB[] = {
    // Windowing & Shell
    { "k_create_window", "k_create_window(int w, int h, const char *title)", "Creates standard GUI window" },
    { "k_create_window_with_icon", "k_create_window_with_icon(int w, int h, const char *title, const char *icon_path)", "Creates window with custom icon" },
    { "k_close", "k_close(handle_t h)", "Closes window or object" },
    { "k_resize_window", "k_resize_window(handle_t win, int w, int h)", "Resizes window" },
    { "k_move_window", "k_move_window(handle_t win, int x, int y)", "Moves window to position" },
    { "k_set_icon", "k_set_icon(handle_t win, const char *icon_path)", "Sets window titlebar icon" },

    // Widgets & Controls
    { "k_create_button", "k_create_button(handle_t parent, int x, int y, int w, int h, const char *text)", "Creates push button" },
    { "k_create_label", "k_create_label(handle_t parent, int x, int y, const char *text)", "Creates static label" },
    { "k_create_textfield", "k_create_textfield(handle_t parent, int x, int y, int w)", "Creates text input field" },
    { "k_create_checkbox", "k_create_checkbox(handle_t parent, int x, int y, const char *text)", "Creates checkbox" },
    { "k_create_radio", "k_create_radio(handle_t parent, int x, int y, const char *text, const char *group)", "Creates radio button" },
    { "k_create_slider", "k_create_slider(handle_t parent, int x, int y, int w, int horiz, int min, int max, int val)", "Creates slider" },
    { "k_create_progress", "k_create_progress(handle_t parent, int x, int y, int w, int h)", "Creates progress bar" },
    { "k_create_combo", "k_create_combo(handle_t parent, int x, int y, int w)", "Creates dropdown combo" },
    { "k_create_listbox", "k_create_listbox(handle_t parent, int x, int y, int w, int h)", "Creates details listbox" },
    { "k_create_tabs", "k_create_tabs(handle_t parent, int x, int y, int w, int h)", "Creates tab control" },
    { "k_create_canvas", "k_create_canvas(handle_t parent, int x, int y, int w, int h)", "Creates direct pixel canvas" },
    { "k_draw_pixels", "k_draw_pixels(handle_t canvas, int x, int y, int w, int h, const void *buf, int len)", "Draws ARGB pixel buffer" },
    { "k_canvas_clear", "k_canvas_clear(handle_t canvas, unsigned argb)", "Clears canvas to solid color" },
    { "k_canvas_fill_rect", "k_canvas_fill_rect(handle_t canvas, int x, int y, int w, int h, unsigned argb)", "Fills solid rectangle on canvas" },
    { "k_canvas_draw_rect", "k_canvas_draw_rect(handle_t canvas, int x, int y, int w, int h, unsigned argb)", "Draws rectangle outline on canvas" },
    { "k_canvas_draw_line", "k_canvas_draw_line(handle_t canvas, int x1, int y1, int x2, int y2, unsigned argb)", "Draws line between two points" },
    { "k_canvas_draw_text", "k_canvas_draw_text(handle_t canvas, int x, int y, const char *text, unsigned argb, int font_size)", "Draws text on canvas" },
    { "k_canvas_draw_gradient", "k_canvas_draw_gradient(handle_t canvas, int x, int y, int w, int h, unsigned top, unsigned bot)", "Draws vertical gradient rectangle" },
    { "k_canvas_draw_rounded", "k_canvas_draw_rounded(handle_t canvas, int x, int y, int w, int h, int r, unsigned argb, int filled)", "Draws rounded rectangle" },
    { "k_canvas_measure_text", "k_canvas_measure_text(handle_t canvas, const char *text, int font_size, int *out_w, int *out_h)", "Measures rendered text dimensions" },
    { "k_textfield_get_caret_xy", "k_textfield_get_caret_xy(handle_t obj, int *out_x, int *out_y)", "Gets visual pixel coordinates of caret" },
    { "k_widget_set_visible", "k_widget_set_visible(handle_t obj, int visible)", "Shows or hides a component" },

    // Widget Manipulation
    { "k_set_text", "k_set_text(handle_t obj, const char *text)", "Sets text of component" },
    { "k_get_text", "k_get_text(handle_t obj, char *buf, int max)", "Retrieves text of component" },
    { "k_widget_set_bounds", "k_widget_set_bounds(handle_t obj, int x, int y, int w, int h)", "Sets component bounds" },
    { "k_widget_set_anchors", "k_widget_set_anchors(handle_t obj, int anchor_flags)", "Sets resize anchors" },
    { "k_widget_set_enabled", "k_widget_set_enabled(handle_t obj, int enabled)", "Enables or disables widget" },
    { "k_textfield_setup", "k_textfield_setup(handle_t obj, int maxlen, int password, int ro)", "Configures textfield" },
    { "k_textfield_set_multiline", "k_textfield_set_multiline(handle_t obj, int multiline)", "Enables multiline editing" },
    { "k_textfield_select", "k_textfield_select(handle_t obj, int start, int len)", "Selects text range or sets caret" },
    { "k_textfield_get_caret", "k_textfield_get_caret(handle_t obj)", "Gets cursor offset in textfield" },
    { "k_list_add_item", "k_list_add_item(handle_t obj, const char *text)", "Adds item to listbox" },
    { "k_list_add_row", "k_list_add_row(handle_t obj, const char *buf, int len)", "Adds multi-column row to list" },
    { "k_list_add_column", "k_list_add_column(handle_t obj, const char *title, int w)", "Adds column to listbox" },
    { "k_list_get_selected", "k_list_get_selected(handle_t obj)", "Gets selected list item index" },
    { "k_list_clear", "k_list_clear(handle_t obj)", "Clears all items in listbox" },
    { "k_menubar_create", "k_menubar_create(handle_t win)", "Creates window menubar" },
    { "k_menu_add_item", "k_menu_add_item(handle_t obj, const char *page, const char *text)", "Adds item to menubar" },
    { "k_statusbar_create", "k_statusbar_create(handle_t parent, int x, int y, int w)", "Creates statusbar" },
    { "k_status_add_panel", "k_status_add_panel(handle_t obj, const char *text, int width)", "Adds statusbar panel" },
    { "k_status_set_text", "k_status_set_text(handle_t obj, int panel, const char *text)", "Sets status panel text" },

    // Events & Timing
    { "k_poll_event", "k_poll_event(Event *e)", "Polls next input event" },
    { "k_sleep", "k_sleep(int ms)", "Sleeps calling thread" },
    { "k_ticks", "k_ticks(void)", "Gets system uptime ticks" },
    { "k_yield", "k_yield(void)", "Yields processor slice" },

    // File System & Processes
    { "k_read_file", "k_read_file(const char *path, char *buf, int max)", "Reads file from disk" },
    { "k_write_file", "k_write_file(const char *path, const char *buf, int len)", "Writes file to disk" },
    { "k_stat_file", "k_stat_file(const char *path, FileStat *out)", "Gets file metadata" },
    { "k_list_dir", "k_list_dir(const char *path, char *buf, int max)", "Lists files in directory" },
    { "k_make_dir", "k_make_dir(const char *path)", "Creates directory" },
    { "k_delete_path", "k_delete_path(const char *path)", "Deletes file or directory" },
    { "k_spawn", "k_spawn(const char *path, const char *argbuf, int arglen)", "Spawns child ELF process" },
    { "k_kill", "k_kill(int target)", "Terminates target process" },
    { "k_wait_pid", "k_wait_pid(int target, int *exit_code, int timeout_ms)", "Waits for process exit" },
    { "k_open_file", "k_open_file(const char *path)", "Opens file with associated app" },
    { "k_launch_app", "k_launch_app(const char *name, const char *arg)", "Launches system app" },
    { "k_sys_mem", "k_sys_mem(uint64_t *out_tot, uint64_t *out_usd, uint64_t *out_fre)", "Queries physical RAM" },

    // Core Types & Constants
    { "handle_t", "typedef int handle_t;", "Widget or window handle" },
    { "Event", "typedef struct Event { int type, handle, a, b; } Event;", "Event structure" },
    { "FileStat", "typedef struct FileStat { int is_dir, size_lo, size_hi; } FileStat;", "File status" },
    { "DriveStat", "typedef struct DriveStat { int total_lo, total_hi, free_lo, free_hi; } DriveStat;", "Drive status" },
    { "EV_CLICK", "#define EV_CLICK 1", "Mouse click event" },
    { "EV_CLOSE", "#define EV_CLOSE 2", "Window close event" },
    { "EV_KEY", "#define EV_KEY 3", "Keyboard event" },
    { "EV_DBLCLICK", "#define EV_DBLCLICK 9", "Double click event" },
    { "ANCHOR_LEFT", "#define ANCHOR_LEFT 1", "Left resize anchor" },
    { "ANCHOR_TOP", "#define ANCHOR_TOP 2", "Top resize anchor" },
    { "ANCHOR_RIGHT", "#define ANCHOR_RIGHT 4", "Right resize anchor" },
    { "ANCHOR_BOTTOM", "#define ANCHOR_BOTTOM 8", "Bottom resize anchor" },

    // Standard Libc Functions
    { "printf", "printf(const char *format, ...)", "Formatted console output" },
    { "snprintf", "snprintf(char *buf, size_t size, const char *format, ...)", "Formatted string output" },
    { "puts", "puts(const char *s)", "Writes string and newline" },
    { "putchar", "putchar(int c)", "Writes single character" },
    { "malloc", "malloc(size_t size)", "Allocates dynamic memory" },
    { "free", "free(void *ptr)", "Frees dynamic memory" },
    { "realloc", "realloc(void *ptr, size_t size)", "Reallocates memory" },
    { "exit", "exit(int status)", "Terminates program" },
    { "strlen", "strlen(const char *s)", "Computes string length" },
    { "strcpy", "strcpy(char *dest, const char *src)", "Copies string" },
    { "strncpy", "strncpy(char *dest, const char *src, size_t n)", "Copies bounded string" },
    { "strcmp", "strcmp(const char *s1, const char *s2)", "Compares strings" },
    { "strncmp", "strncmp(const char *s1, const char *s2, size_t n)", "Compares bounded strings" },
    { "memcpy", "memcpy(void *dest, const void *src, size_t n)", "Copies memory bytes" },
    { "memset", "memset(void *dest, int c, size_t n)", "Sets memory bytes" },

    // C Keywords & Types
    { "return", "return [expr];", "Returns from function" },
    { "if", "if (condition) { }", "Conditional statement" },
    { "else", "else { }", "Alternative branch" },
    { "while", "while (condition) { }", "Loop while condition holds" },
    { "for", "for (init; cond; step) { }", "Counted loop" },
    { "struct", "struct Name { ... };", "Record structure" },
    { "typedef", "typedef Type Alias;", "Type definition" },
    { "sizeof", "sizeof(type_or_expr)", "Size in bytes" },
    { "unsigned", "unsigned", "Unsigned integer modifier" },
    { "int", "int", "32-bit signed integer" },
    { "char", "char", "8-bit character" },
    { "void", "void", "Empty type" }
};
#define NUM_SYMBOLS ((int)(sizeof(SYMBOL_DB) / sizeof(SYMBOL_DB[0])))

/* ---------------- Helper Functions ---------------- */

static void log_output(const char *msg) {
    char buf[16384];
    int len = k_get_text(h_output_buffer, buf, (int)sizeof(buf) - 2);
    if (len < 0) len = 0;
    buf[len] = '\0';

    int msglen = (int)strlen(msg);
    if (len + msglen < (int)sizeof(buf) - 4) {
        if (len > 0 && buf[len - 1] != '\n') {
            buf[len++] = '\n';
            buf[len] = '\0';
        }
        memcpy(buf + len, msg, (size_t)msglen);
        len += msglen;
        if (buf[len - 1] != '\n') {
            buf[len++] = '\n';
        }
        buf[len] = '\0';
        k_set_text(h_output_buffer, buf);
    }
}

static void update_status(const char *msg) {
    char buf[MAX_CODE_SIZE];
    int len = k_get_text(h_text_buffer, buf, (int)sizeof(buf) - 1);
    if (len < 0) len = 0;
    buf[len] = '\0';

    int caret = k_textfield_get_caret(h_text_buffer);
    if (caret < 0) caret = 0;
    if (caret > len) caret = len;

    int lines = 1;
    int cur_line = 1;
    int cur_col = 1;

    for (int i = 0; i < len; i++) {
        if (i < caret) {
            if (buf[i] == '\n') { cur_line++; cur_col = 1; }
            else cur_col++;
        }
        if (buf[i] == '\n') lines++;
    }

    char p0[256];
    if (msg && msg[0])
        snprintf(p0, sizeof(p0), "%s: %s", current_file, msg);
    else
        snprintf(p0, sizeof(p0), "%s", current_file);

    char p1[64];
    snprintf(p1, sizeof(p1), "Ln %d, Col %d | %d lines", cur_line, cur_col, lines);

    k_status_set_text(h_status_bar, 0, p0);
    k_status_set_text(h_status_bar, 1, p1);
}

/* ---------------- Autocomplete Engine ---------------- */

static int is_word_char(char c) {
    return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
}

static int str_starts_with_ci(const char *str, const char *prefix) {
    while (*prefix) {
        char a = *str++, b = *prefix++;
        if (a >= 'A' && a <= 'Z') a += 32;
        if (b >= 'A' && b <= 'Z') b += 32;
        if (a != b) return 0;
    }
    return 1;
}

static void get_word_at_caret(const char *text, int caret, int *out_start, int *out_end, char *out_word, int max_word) {
    int start = caret;
    while (start > 0 && is_word_char(text[start - 1])) {
        start--;
    }
    int end = caret;
    while (text[end] && is_word_char(text[end])) {
        end++;
    }

    *out_start = start;
    *out_end = end;

    int len = end - start;
    if (len >= max_word) len = max_word - 1;
    memcpy(out_word, text + start, (size_t)len);
    out_word[len] = '\0';
}

static void draw_autocomplete_popup(void) {
    if (!ac_visible || !h_ac_canvas || ac_match_count <= 0) return;

    // Dark sleek Aero window background
    k_canvas_clear(h_ac_canvas, 0xF81E2228);

    // Double border (outer subtle border + inner frame)
    k_canvas_draw_rect(h_ac_canvas, 0, 0, AC_WIDTH, AC_HEIGHT, 0xFF3E4F65);
    k_canvas_draw_rect(h_ac_canvas, 1, 1, AC_WIDTH - 2, AC_HEIGHT - 2, 0xFF2A3644);

    // Header gradient banner
    k_canvas_draw_gradient(h_ac_canvas, 2, 2, AC_WIDTH - 4, 18, 0xFF283444, 0xFF1D2633);
    k_canvas_draw_text(h_ac_canvas, 8, 4, "IntelliSense", 0xFF8AA8CE, 11);

    char count_str[32];
    snprintf(count_str, sizeof(count_str), "%d/%d", ac_selected + 1, ac_match_count);
    k_canvas_draw_text(h_ac_canvas, AC_WIDTH - 44, 4, count_str, 0xFF8AA8CE, 11);
    k_canvas_draw_line(h_ac_canvas, 2, 20, AC_WIDTH - 2, 20, 0xFF3E4F65);

    int max_draw = ac_match_count < AC_MAX_ITEMS ? ac_match_count : AC_MAX_ITEMS;
    for (int i = 0; i < max_draw; i++) {
        int row_y = 22 + i * AC_ITEM_HEIGHT;
        int sym_idx = ac_matches[i];
        const SymbolEntry *sym = &SYMBOL_DB[sym_idx];

        if (i == ac_selected) {
            k_canvas_draw_gradient(h_ac_canvas, 3, row_y, AC_WIDTH - 6, AC_ITEM_HEIGHT - 1, 0xFF2A6CB0, 0xFF184A80);
            k_canvas_draw_rect(h_ac_canvas, 3, row_y, AC_WIDTH - 6, AC_ITEM_HEIGHT - 1, 0xFF4A90E2);
        } else if (i % 2 == 1) {
            k_canvas_fill_rect(h_ac_canvas, 3, row_y, AC_WIDTH - 6, AC_ITEM_HEIGHT - 1, 0xFF242932);
        }

        unsigned badge_col = 0xFF00D2FF;
        char badge_char = 'f';
        if (!strncmp(sym->name, "k_", 2)) {
            badge_char = 'k';
            badge_col = 0xFF00E5FF;
        } else if (sym->sig[0] == '\0' || !strcmp(sym->sig, "keyword")) {
            badge_char = 'k';
            badge_col = 0xFFFFB300;
        } else if (strstr(sym->sig, "struct") || strstr(sym->desc, "type")) {
            badge_char = 't';
            badge_col = 0xFF69F0AE;
        } else if (strstr(sym->desc, "Macro") || strstr(sym->desc, "constant")) {
            badge_char = 'm';
            badge_col = 0xFFFF5252;
        }

        k_canvas_fill_rect(h_ac_canvas, 6, row_y + 2, 16, 15, 0xFF141820);
        k_canvas_draw_rect(h_ac_canvas, 6, row_y + 2, 16, 15, badge_col);
        char bstr[2] = { badge_char, '\0' };
        k_canvas_draw_text(h_ac_canvas, 10, row_y + 3, bstr, badge_col, 11);

        unsigned text_col = (i == ac_selected) ? 0xFFFFFFFF : 0xFFE0E6ED;
        k_canvas_draw_text(h_ac_canvas, 26, row_y + 3, sym->name, text_col, 12);

        const char *kind_label = (badge_char == 'k' && !strncmp(sym->name, "k_", 2)) ? "api" :
                                 (badge_char == 'k') ? "key" :
                                 (badge_char == 't') ? "type" : "fn";
        k_canvas_draw_text(h_ac_canvas, AC_WIDTH - 34, row_y + 3, kind_label, 0xFF7E8C9F, 11);
    }

    // Bottom parameter hint footer
    int footer_y = AC_HEIGHT - 24;
    k_canvas_draw_line(h_ac_canvas, 2, footer_y, AC_WIDTH - 2, footer_y, 0xFF3E4F65);
    k_canvas_draw_gradient(h_ac_canvas, 2, footer_y + 1, AC_WIDTH - 4, 21, 0xFF181C22, 0xFF12151A);

    const SymbolEntry *sel_sym = &SYMBOL_DB[ac_matches[ac_selected]];
    char preview_text[128];
    if (sel_sym->sig[0]) {
        snprintf(preview_text, sizeof(preview_text), "%.46s", sel_sym->sig);
    } else {
        snprintf(preview_text, sizeof(preview_text), "%.46s", sel_sym->desc);
    }
    k_canvas_draw_text(h_ac_canvas, 8, footer_y + 4, preview_text, 0xFF58D68D, 11);

    char st[256];
    snprintf(st, sizeof(st), "%s - %s", sel_sym->sig[0] ? sel_sym->sig : sel_sym->name, sel_sym->desc);
    update_status(st);
}

static void update_suggestions(void) {
    char code[MAX_CODE_SIZE];
    int len = k_get_text(h_text_buffer, code, (int)sizeof(code) - 1);
    if (len < 0) len = 0;
    code[len] = '\0';

    int caret = k_textfield_get_caret(h_text_buffer);
    if (caret < 0 || caret > len) caret = len;

    char prefix[64] = "";
    get_word_at_caret(code, caret, &cur_prefix_start, &cur_prefix_end, prefix, sizeof(prefix));

    ac_match_count = 0;
    int plen = (int)strlen(prefix);

    if (plen >= 2) {
        for (int i = 0; i < NUM_SYMBOLS && ac_match_count < MAX_MATCHES; i++) {
            if (str_starts_with_ci(SYMBOL_DB[i].name, prefix)) {
                ac_matches[ac_match_count++] = i;
            }
        }
    }

    if (ac_match_count == 0) {
        if (ac_visible) {
            k_widget_set_visible(h_ac_canvas, 0);
            ac_visible = 0;
        }
        return;
    }

    if (ac_selected >= ac_match_count) ac_selected = 0;

    int cx = 20, cy = 60;
    if (k_textfield_get_caret_xy(h_text_buffer, &cx, &cy) == 0) {
        ac_x = cx;
        ac_y = cy + 20;
    } else {
        ac_x = 20;
        ac_y = 60;
    }

    if (ac_x + AC_WIDTH > 860) ac_x = 860 - AC_WIDTH;
    if (ac_x < 10) ac_x = 10;
    if (ac_y + AC_HEIGHT > 390) ac_y = cy - AC_HEIGHT - 2;
    if (ac_y < 42) ac_y = 42;

    k_widget_set_bounds(h_ac_canvas, ac_x, ac_y, AC_WIDTH, AC_HEIGHT);
    k_widget_set_visible(h_ac_canvas, 1);
    ac_visible = 1;

    draw_autocomplete_popup();
}

static void apply_completion(int sym_idx) {
    if (sym_idx < 0 || sym_idx >= NUM_SYMBOLS) return;
    const char *replacement = SYMBOL_DB[sym_idx].name;

    char code[MAX_CODE_SIZE];
    int len = k_get_text(h_text_buffer, code, (int)sizeof(code) - 1);
    if (len < 0) len = 0;
    code[len] = '\0';

    if (cur_prefix_start < 0) cur_prefix_start = 0;
    if (cur_prefix_end > len) cur_prefix_end = len;

    char new_code[MAX_CODE_SIZE];
    int repl_len = (int)strlen(replacement);

    // Splice: code[0..cur_prefix_start] + replacement + code[cur_prefix_end..len]
    memcpy(new_code, code, (size_t)cur_prefix_start);
    memcpy(new_code + cur_prefix_start, replacement, (size_t)repl_len);
    int rest_len = len - cur_prefix_end;
    if (rest_len > 0) {
        memcpy(new_code + cur_prefix_start + repl_len, code + cur_prefix_end, (size_t)rest_len);
    }
    int new_total = cur_prefix_start + repl_len + rest_len;
    new_code[new_total] = '\0';

    k_set_text(h_text_buffer, new_code);

    int new_caret = cur_prefix_start + repl_len;
    k_textfield_select(h_text_buffer, new_caret, 0);

    if (ac_visible) {
        k_widget_set_visible(h_ac_canvas, 0);
        ac_visible = 0;
    }

    char st[256];
    snprintf(st, sizeof(st), "Applied %s", replacement);
    update_status(st);
}

/* ---------------- Syntax Checking & Error Outlining ---------------- */

static int check_structural_syntax(const char *code, char *out_warn, int max_warn, int *out_line) {
    int brace_depth = 0;
    int paren_depth = 0;
    int bracket_depth = 0;
    int line = 1;
    int open_brace_line = 1;
    int open_paren_line = 1;
    int in_str = 0;
    int in_char = 0;

    for (int i = 0; code[i]; i++) {
        char c = code[i];
        if (c == '\n') line++;

        // Handle string escape
        if (c == '\\' && (in_str || in_char)) {
            if (code[i + 1]) i++;
            continue;
        }

        if (c == '"' && !in_char) { in_str = !in_str; continue; }
        if (c == '\'' && !in_str) { in_char = !in_char; continue; }
        if (in_str || in_char) continue;

        if (c == '{') { brace_depth++; open_brace_line = line; }
        else if (c == '}') {
            brace_depth--;
            if (brace_depth < 0) {
                snprintf(out_warn, max_warn, "Unexpected closing '}' at line %d", line);
                *out_line = line;
                return -1;
            }
        } else if (c == '(') { paren_depth++; open_paren_line = line; }
        else if (c == ')') {
            paren_depth--;
            if (paren_depth < 0) {
                snprintf(out_warn, max_warn, "Unexpected closing ')' at line %d", line);
                *out_line = line;
                return -1;
            }
        } else if (c == '[') bracket_depth++;
        else if (c == ']') {
            bracket_depth--;
            if (bracket_depth < 0) {
                snprintf(out_warn, max_warn, "Unexpected closing ']' at line %d", line);
                *out_line = line;
                return -1;
            }
        }
    }

    if (in_str) {
        snprintf(out_warn, max_warn, "Unterminated string literal '\"'");
        *out_line = line;
        return -1;
    }
    if (brace_depth > 0) {
        snprintf(out_warn, max_warn, "Unclosed '{' opened at line %d (missing '}')", open_brace_line);
        *out_line = open_brace_line;
        return -1;
    }
    if (paren_depth > 0) {
        snprintf(out_warn, max_warn, "Unclosed '(' opened at line %d (missing ')')", open_paren_line);
        *out_line = open_paren_line;
        return -1;
    }

    return 0;
}

static void get_line_offset(const char *text, int target_line, int *out_offset, int *out_len) {
    int line = 1;
    int offset = 0;
    while (text[offset] && line < target_line) {
        if (text[offset] == '\n') line++;
        offset++;
    }
    *out_offset = offset;

    int len = 0;
    while (text[offset + len] && text[offset + len] != '\n') {
        len++;
    }
    *out_len = len > 0 ? len : 1;
}

static void jump_to_error(int line) {
    if (line <= 0) return;
    char code[MAX_CODE_SIZE];
    int len = k_get_text(h_text_buffer, code, (int)sizeof(code) - 1);
    if (len <= 0) return;
    code[len] = '\0';

    int offset, line_len;
    get_line_offset(code, line, &offset, &line_len);

    k_textfield_select(h_text_buffer, offset, line_len);

    char msg[128];
    snprintf(msg, sizeof(msg), "Jumped to error at line %d", line);
    k_status_set_text(h_status_bar, 0, msg);
}

static void on_save_file(void);

static void do_syntax_check(void) {
    on_save_file();

    char code[MAX_CODE_SIZE];
    int len = k_get_text(h_text_buffer, code, (int)sizeof(code) - 1);
    if (len <= 0) return;
    code[len] = '\0';

    // 1. Structural check
    char warn[256] = "";
    int warn_line = 0;
    if (check_structural_syntax(code, warn, sizeof(warn), &warn_line) != 0) {
        last_err_line = warn_line;
        strncpy(last_err_msg, warn, sizeof(last_err_msg) - 1);

        char out_str[300];
        snprintf(out_str, sizeof(out_str), ">> [Syntax Error] Line %d: %s", warn_line, warn);
        log_output(out_str);

        char stat_str[256];
        snprintf(stat_str, sizeof(stat_str), "[!] Line %d: %s (Press F4 to jump)", warn_line, warn);
        k_status_set_text(h_status_bar, 0, stat_str);
        return;
    }

    // 2. Compiler-assisted syntax verification via tcc.elf -c
    log_output(">> [Syntax Check] Verifying with TinyCC...");

    char argbuf[512];
    int p = 0;
    const char *args[4];
    args[0] = current_file;
    args[1] = "-c";
    args[2] = "-log";
    args[3] = "/mnt/build.log";

    for (int i = 0; i < 4; i++) {
        int l = (int)strlen(args[i]) + 1;
        if (p + l < (int)sizeof(argbuf)) {
            memcpy(argbuf + p, args[i], (size_t)l);
            p += l;
        }
    }

    k_write_file("/mnt/build.log", "", 0);
    int pid = k_spawn("/mnt/Programs/tcc.elf", argbuf, p);
    if (pid < 0) {
        log_output("[Syntax Check] Compiler /mnt/Programs/tcc.elf not found");
        return;
    }

    int exit_code = 0;
    k_wait_pid(pid, &exit_code, 5000);

    char log_data[8192];
    int log_n = k_read_file("/mnt/build.log", log_data, sizeof(log_data) - 1);
    if (log_n > 0) {
        log_data[log_n] = '\0';
        log_output(log_data);

        // Parse line number if error occurred
        char *err_pos = strstr(log_data, "error:");
        if (err_pos) {
            // Find colon before error:
            char *line_pos = err_pos;
            while (line_pos > log_data && *(line_pos - 1) == ' ') line_pos--;
            if (line_pos > log_data && *(line_pos - 1) == ':') {
                line_pos -= 2;
                while (line_pos > log_data && *line_pos >= '0' && *line_pos <= '9') line_pos--;
                last_err_line = atoi(line_pos + 1);
            }
        }
    }

    if (exit_code == 0) {
        log_output(">> [Syntax Check] OK: No syntax errors detected.");
        k_status_set_text(h_status_bar, 0, "Syntax OK (0 errors)");
        last_err_line = 0;
    } else {
        char stat_str[256];
        snprintf(stat_str, sizeof(stat_str), "[!] Syntax Error at line %d (Press F4 to jump)", last_err_line);
        k_status_set_text(h_status_bar, 0, stat_str);
    }
}

/* ---------------- Document & File Actions ---------------- */

static void load_file(const char *path) {
    if (!path || !path[0]) return;
    char buf[MAX_CODE_SIZE];
    int n = k_read_file(path, buf, (int)sizeof(buf) - 1);
    if (n >= 0) {
        buf[n] = '\0';
        k_set_text(h_text_buffer, buf);
        strncpy(current_file, path, sizeof(current_file) - 1);
        current_file[sizeof(current_file) - 1] = '\0';
        k_set_text(h_lbl_filename, current_file);
        update_status("Opened successfully");
    } else {
        strncpy(current_file, path, sizeof(current_file) - 1);
        current_file[sizeof(current_file) - 1] = '\0';
        k_set_text(h_lbl_filename, current_file);
        update_status("New file");
    }
    update_suggestions();
}

static void on_new_file(void) {
    strncpy(current_file, "/mnt/untitled.c", sizeof(current_file) - 1);
    k_set_text(h_lbl_filename, current_file);
    k_set_text(h_text_buffer,
               "/* Windose OS Native Application */\n"
               "#include <stdio.h>\n"
               "#include \"kernel.h\"\n\n"
               "int main(int argc, char **argv) {\n"
               "    handle_t win = k_create_window(460, 280, \"My Windose App\");\n"
               "    k_create_label(win, 20, 24, \"Hello from compiled C program!\");\n"
               "    handle_t btn = k_create_button(win, 20, 64, 100, 26, \"Close\");\n\n"
               "    for (;;) {\n"
               "        Event e;\n"
               "        while (k_poll_event(&e)) {\n"
               "            if ((e.type == EV_CLOSE && e.handle == win) ||\n"
               "                (e.type == EV_CLICK && e.handle == btn)) {\n"
               "                k_close(win);\n"
               "                return 0;\n"
               "            }\n"
               "        }\n"
               "        k_sleep(10);\n"
               "    }\n"
               "}\n");
    update_status("New C document");
    update_suggestions();
}

static void on_new_cli_template(void) {
    strncpy(current_file, "/mnt/cli_app.c", sizeof(current_file) - 1);
    k_set_text(h_lbl_filename, current_file);
    k_set_text(h_text_buffer,
               "/* Windose CLI Utility */\n"
               "#include <stdio.h>\n"
               "#include <stdlib.h>\n"
               "#include \"kernel.h\"\n\n"
               "int main(int argc, char **argv) {\n"
               "    printf(\"Running Windose CLI utility...\\n\");\n"
               "    for (int i = 0; i < argc; i++) {\n"
               "        printf(\"  arg[%d] = %s\\n\", i, argv[i]);\n"
               "    }\n"
               "    return 0;\n"
               "}\n");
    update_status("CLI template loaded");
    update_suggestions();
}

static void on_new_canvas_template(void) {
    strncpy(current_file, "/mnt/canvas_app.c", sizeof(current_file) - 1);
    k_set_text(h_lbl_filename, current_file);
    k_set_text(h_text_buffer,
               "/* Windose Pixel Canvas Application */\n"
               "#include <stdio.h>\n"
               "#include \"kernel.h\"\n\n"
               "#define CW 320\n"
               "#define CH 240\n\n"
               "static unsigned pixels[CW * CH];\n\n"
               "int main(int argc, char **argv) {\n"
               "    handle_t win = k_create_window(360, 300, \"Pixel Canvas Demo\");\n"
               "    handle_t cvs = k_create_canvas(win, 10, 10, CW, CH);\n\n"
               "    for (int y = 0; y < CH; y++) {\n"
               "        for (int x = 0; x < CW; x++) {\n"
               "            pixels[y * CW + x] = 0xFF000000 | ((x * 255 / CW) << 16) | ((y * 255 / CH) << 8) | 128;\n"
               "        }\n"
               "    }\n"
               "    k_draw_pixels(cvs, 0, 0, CW, CH, pixels, (int)sizeof(pixels));\n\n"
               "    for (;;) {\n"
               "        Event e;\n"
               "        while (k_poll_event(&e)) {\n"
               "            if (e.type == EV_CLOSE && e.handle == win) {\n"
               "                k_close(win);\n"
               "                return 0;\n"
               "            }\n"
               "        }\n"
               "        k_sleep(20);\n"
               "    }\n"
               "}\n");
    update_status("Canvas template loaded");
    update_suggestions();
}

static void on_save_file(void) {
    char path[256] = {0};
    k_get_text(h_lbl_filename, path, (int)sizeof(path) - 1);
    if (strlen(path) == 0) {
        strncpy(path, current_file, sizeof(path) - 1);
    }

    char buf[MAX_CODE_SIZE];
    int len = k_get_text(h_text_buffer, buf, (int)sizeof(buf) - 1);
    if (len >= 0) {
        buf[len] = '\0';
        int r = k_write_file(path, buf, len);
        if (r >= 0) {
            strncpy(current_file, path, sizeof(current_file) - 1);
            update_status("Saved");
        } else {
            update_status("Error: Failed to save");
        }
    }
}

static void on_open_file(void) {
    char path[256] = {0};
    k_get_text(h_lbl_filename, path, (int)sizeof(path) - 1);
    if (strlen(path) > 0) {
        load_file(path);
    }
}

static void on_save_as_file(void) {
    char path[256] = {0};
    k_get_text(h_lbl_filename, path, (int)sizeof(path) - 1);
    if (strlen(path) == 0 || strcmp(path, current_file) == 0) {
        update_status("Specify destination in toolbar field, then click Save");
    } else {
        on_save_file();
    }
}

static void do_build(int run_after) {
    on_save_file();

    char out_elf[256];
    strncpy(out_elf, current_file, sizeof(out_elf) - 1);
    char *dot = strrchr(out_elf, '.');
    if (dot) strcpy(dot, ".elf");
    else strcat(out_elf, ".elf");

    char msg[320];
    snprintf(msg, sizeof(msg), "------ Build started: Project: %s, Target: %s ------", current_file, out_elf);
    log_output(msg);
    update_status("Compiling with TinyCC...");

    char argbuf[512];
    int p = 0;
    const char *args[5];
    args[0] = current_file;
    args[1] = "-o";
    args[2] = out_elf;
    args[3] = "-log";
    args[4] = "/mnt/build.log";

    for (int i = 0; i < 5; i++) {
        int l = (int)strlen(args[i]) + 1;
        if (p + l < (int)sizeof(argbuf)) {
            memcpy(argbuf + p, args[i], (size_t)l);
            p += l;
        }
    }

    k_write_file("/mnt/build.log", "", 0);

    int child_pid = k_spawn("/mnt/Programs/tcc.elf", argbuf, p);
    if (child_pid < 0) {
        log_output("[Build] error: failed to spawn compiler /mnt/Programs/tcc.elf");
        update_status("Build failed: tcc.elf not found");
        return;
    }

    int exit_code = 0;
    k_wait_pid(child_pid, &exit_code, 8000);

    char log_data[8192];
    int log_n = k_read_file("/mnt/build.log", log_data, sizeof(log_data) - 1);
    if (log_n > 0) {
        log_data[log_n] = '\0';
        log_output(log_data);

        // Check if there was an error with line number
        char *err_pos = strstr(log_data, "error:");
        if (err_pos) {
            char *line_pos = err_pos;
            while (line_pos > log_data && *(line_pos - 1) == ' ') line_pos--;
            if (line_pos > log_data && *(line_pos - 1) == ':') {
                line_pos -= 2;
                while (line_pos > log_data && *line_pos >= '0' && *line_pos <= '9') line_pos--;
                last_err_line = atoi(line_pos + 1);
            }
        }
    }

    if (exit_code == 0) {
        snprintf(msg, sizeof(msg), "========== Build: 1 succeeded, 0 failed, Output: %s ==========", out_elf);
        log_output(msg);
        update_status("Build Succeeded");
        last_err_line = 0;

        if (run_after) {
            snprintf(msg, sizeof(msg), "[Run] Spawning application %s...", out_elf);
            log_output(msg);
            int app_pid = k_spawn(out_elf, "", 0);
            if (app_pid >= 0) {
                snprintf(msg, sizeof(msg), "[Run] Application running with PID %d.", app_pid);
                log_output(msg);
            } else {
                log_output("[Run] error: failed to launch compiled application.");
            }
        }
    } else {
        snprintf(msg, sizeof(msg), "========== Build: 0 succeeded, 1 failed (exit code %d) ==========", exit_code);
        log_output(msg);
        char stat_str[256];
        snprintf(stat_str, sizeof(stat_str), "Build Failed at line %d (Press F4 to jump)", last_err_line);
        k_status_set_text(h_status_bar, 0, stat_str);
    }
}

static void show_about(void) {
    static const char ok_btn[] = "OK";
    k_msgbox("About AeroEditor IDE",
             "AeroEditor IDE 3.5 (Native ELF Edition)\n"
             "Advanced C Development Environment for Windose OS.\n"
             "Features: Auto-Complete, Syntax Checking, F4 Quick-Jump Error Outlining, and TinyCC x86_64 compiler.",
             ok_btn, (int)sizeof(ok_btn));
}

int main(int argc, char **argv) {
    main_win = k_create_window_with_icon(880, 620, "AeroEditor IDE", "/mnt/System/Icons/notepad.png");
    if (!main_win) main_win = k_create_window(880, 620, "AeroEditor IDE");
    if (!main_win) return 1;

    /* Menu bar */
    handle_t mbar = k_menubar_create(main_win);
    if (mbar) {
        m_new = k_menu_add_item(mbar, "File", "New Document");
        m_tmpl_gui = k_menu_add_item(mbar, "File", "Template: GUI Window App");
        m_tmpl_cli = k_menu_add_item(mbar, "File", "Template: CLI Console App");
        m_tmpl_canvas = k_menu_add_item(mbar, "File", "Template: Canvas Pixel App");
        k_menu_add_item(mbar, "File", "-");
        m_open = k_menu_add_item(mbar, "File", "Open...");
        m_save = k_menu_add_item(mbar, "File", "Save (Ctrl+S)");
        m_saveas = k_menu_add_item(mbar, "File", "Save As...");
        k_menu_add_item(mbar, "File", "-");
        m_exit = k_menu_add_item(mbar, "File", "Exit");

        m_clear = k_menu_add_item(mbar, "Edit", "Clear Editor");

        m_check_syntax = k_menu_add_item(mbar, "Build", "Check Syntax");
        m_compile = k_menu_add_item(mbar, "Build", "Compile (Ctrl+B)");
        m_build_run = k_menu_add_item(mbar, "Build", "Build & Run (F5)");
        m_run = k_menu_add_item(mbar, "Build", "Run (Ctrl+F5)");
        m_jump_err = k_menu_add_item(mbar, "Build", "Jump to Error (F4)");
        k_menu_add_item(mbar, "Build", "-");
        m_clean = k_menu_add_item(mbar, "Build", "Clear Output Log");

        m_about = k_menu_add_item(mbar, "Help", "About AeroEditor IDE");
    }

    /* Toolbar buttons */
    h_btn_new = k_create_button(main_win, 10, 8, 52, 28, "New");
    k_widget_set_anchors(h_btn_new, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_open = k_create_button(main_win, 65, 8, 52, 28, "Open");
    k_widget_set_anchors(h_btn_open, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_save = k_create_button(main_win, 120, 8, 52, 28, "Save");
    k_widget_set_anchors(h_btn_save, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_check = k_create_button(main_win, 175, 8, 58, 28, "Check");
    k_widget_set_anchors(h_btn_check, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_build = k_create_button(main_win, 236, 8, 54, 28, "Build");
    k_widget_set_anchors(h_btn_build, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_run = k_create_button(main_win, 293, 8, 70, 28, "Run (F5)");
    k_widget_set_anchors(h_btn_run, ANCHOR_LEFT | ANCHOR_TOP);

    /* Filename input field */
    h_lbl_filename = k_create_textfield(main_win, 368, 8, 500);
    k_textfield_setup(h_lbl_filename, 256, 0, 0);
    k_widget_set_anchors(h_lbl_filename, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT);

    /* Multiline text editor */
    h_text_buffer = k_create_textfield(main_win, 10, 42, 860);
    k_textfield_setup(h_text_buffer, 32768, 0, 0);
    k_textfield_set_multiline(h_text_buffer, 1);
    k_widget_set_anchors(h_text_buffer, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT | ANCHOR_BOTTOM);
    k_widget_set_bounds(h_text_buffer, 10, 42, 860, 356);

    /* Floating Autocomplete Canvas Component */
    h_ac_canvas = k_create_canvas(main_win, 20, 60, AC_WIDTH, AC_HEIGHT);
    k_widget_set_visible(h_ac_canvas, 0);

    /* Output Console Label */
    h_lbl_output = k_create_label(main_win, 10, 404, "Output & Error Diagnostics (Press F4 to jump to error):");
    k_widget_set_anchors(h_lbl_output, ANCHOR_LEFT | ANCHOR_BOTTOM);

    /* Output Console Buffer */
    h_output_buffer = k_create_textfield(main_win, 10, 424, 860);
    k_textfield_setup(h_output_buffer, 32768, 0, 1);
    k_textfield_set_multiline(h_output_buffer, 1);
    k_widget_set_anchors(h_output_buffer, ANCHOR_LEFT | ANCHOR_RIGHT | ANCHOR_BOTTOM);
    k_widget_set_bounds(h_output_buffer, 10, 424, 860, 140);

    /* Status bar */
    h_status_bar = k_statusbar_create(main_win, 0, 588, 880);
    k_status_add_panel(h_status_bar, "Ready", 420);
    k_status_add_panel(h_status_bar, "Ln 1, Col 1 | 1 lines", 220);
    k_status_add_panel(h_status_bar, "TinyCC x86_64 | Windose IDE", 220);
    k_widget_set_anchors(h_status_bar, ANCHOR_LEFT | ANCHOR_RIGHT | ANCHOR_BOTTOM);

    if (argc > 1 && argv[1] && argv[1][0]) {
        load_file(argv[1]);
    } else {
        on_new_file();
    }

    log_output("[AeroEditor IDE] Workspace initialized. Autocomplete, Syntax Check, and TinyCC active.");

    for (;;) {
        Event e;
        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE && e.handle == main_win) {
                k_close(main_win);
                return 0;
            }
            if (e.type == EV_KEY) {
                unsigned char ch = (unsigned char)(e.b & 0xFFFF);
                if (ch == 27) { // Escape
                    if (ac_visible) {
                        k_widget_set_visible(h_ac_canvas, 0);
                        ac_visible = 0;
                        continue;
                    }
                    k_close(main_win);
                    return 0;
                }
                // Up Arrow: navigate autocomplete
                if (ch == 17 && ac_visible) {
                    if (ac_match_count > 0) {
                        ac_selected = (ac_selected > 0) ? ac_selected - 1 : ac_match_count - 1;
                        draw_autocomplete_popup();
                    }
                    continue;
                }
                // Down Arrow: navigate autocomplete
                if (ch == 18 && ac_visible) {
                    if (ac_match_count > 0) {
                        ac_selected = (ac_selected < ac_match_count - 1) ? ac_selected + 1 : 0;
                        draw_autocomplete_popup();
                    }
                    continue;
                }
                // Tab or Enter: Apply autocomplete selection
                if ((ch == '\t' || (ch == '\r' && ac_visible)) && ac_visible && ac_match_count > 0) {
                    apply_completion(ac_matches[ac_selected]);
                    continue;
                }
                // F5: Build & Run
                if (ch == 132) {
                    do_build(1);
                    continue;
                }
                // F4 (ch == 131): Quick Jump to Error Line
                if (ch == 131) {
                    jump_to_error(last_err_line);
                    continue;
                }
                // Ctrl+B (ch == 2): Compile
                if (ch == 2) {
                    do_build(0);
                    continue;
                }
                // Ctrl+S (ch == 19): Save
                if (ch == 19) {
                    on_save_file();
                    continue;
                }

                // Any typing in editor: update suggestions and metrics
                update_suggestions();
                update_status(NULL);
            }
            if (e.type == EV_CLICK) {
                if (e.handle == h_ac_canvas) {
                    int click_y = e.b;
                    int row = (click_y - 22) / AC_ITEM_HEIGHT;
                    if (row >= 0 && row < ac_match_count && row < AC_MAX_ITEMS) {
                        apply_completion(ac_matches[row]);
                    }
                } else if (e.handle == h_btn_new || e.handle == m_new) {
                    on_new_file();
                } else if (e.handle == m_tmpl_gui) {
                    on_new_file();
                } else if (e.handle == m_tmpl_cli) {
                    on_new_cli_template();
                } else if (e.handle == m_tmpl_canvas) {
                    on_new_canvas_template();
                } else if (e.handle == h_btn_open || e.handle == m_open) {
                    on_open_file();
                } else if (e.handle == h_btn_save || e.handle == m_save) {
                    on_save_file();
                } else if (e.handle == m_saveas) {
                    on_save_as_file();
                } else if (e.handle == h_btn_check || e.handle == m_check_syntax) {
                    do_syntax_check();
                } else if (e.handle == h_btn_build || e.handle == m_compile) {
                    do_build(0);
                } else if (e.handle == h_btn_run || e.handle == m_build_run) {
                    do_build(1);
                } else if (e.handle == m_jump_err) {
                    jump_to_error(last_err_line);
                } else if (e.handle == m_run) {
                    char out_elf[256];
                    strncpy(out_elf, current_file, sizeof(out_elf) - 1);
                    char *dot = strrchr(out_elf, '.');
                    if (dot) strcpy(dot, ".elf");
                    else strcat(out_elf, ".elf");
                    k_spawn(out_elf, "", 0);
                } else if (e.handle == m_clean) {
                    k_set_text(h_output_buffer, "");
                } else if (e.handle == m_clear) {
                    k_set_text(h_text_buffer, "");
                    update_status("Cleared");
                    update_suggestions();
                } else if (e.handle == m_about) {
                    show_about();
                } else if (e.handle == m_exit) {
                    k_close(main_win);
                    return 0;
                }
            }
        }
        k_sleep(10);
    }
}
