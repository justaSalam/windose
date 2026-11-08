/**
 * Windose OS Native AeroEditor (ELF)
 * Advanced text editor with MenuBar, File/Edit actions, real file I/O, Save As, and responsive layout.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

/* Component Handles */
static handle_t main_win = 0;
static handle_t m_new = 0;
static handle_t m_open = 0;
static handle_t m_save = 0;
static handle_t m_saveas = 0;
static handle_t m_exit = 0;
static handle_t m_clear = 0;
static handle_t m_about = 0;

static handle_t h_btn_new = 0;
static handle_t h_btn_open = 0;
static handle_t h_btn_save = 0;
static handle_t h_btn_saveas = 0;
static handle_t h_lbl_filename = 0;
static handle_t h_text_buffer = 0;
static handle_t h_status_bar = 0;

static char current_file[256] = "/mnt/untitled.txt";

static void update_status(const char *msg) {
    char buf[16384];
    int len = k_get_text(h_text_buffer, buf, (int)sizeof(buf) - 1);
    if (len < 0) len = 0;
    buf[len] = '\0';

    int lines = 1;
    for (int i = 0; i < len; i++) {
        if (buf[i] == '\n') lines++;
    }

    char p0[256];
    if (msg && msg[0])
        snprintf(p0, sizeof(p0), "%s: %s", current_file, msg);
    else
        snprintf(p0, sizeof(p0), "%s", current_file);

    char p1[64];
    snprintf(p1, sizeof(p1), "Lines: %d | Size: %d B", lines, len);

    k_status_set_text(h_status_bar, 0, p0);
    k_status_set_text(h_status_bar, 1, p1);
}

static void load_file(const char *path) {
    if (!path || !path[0]) return;
    char buf[16384];
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
}

static void on_new_file(void) {
    strncpy(current_file, "/mnt/untitled.txt", sizeof(current_file) - 1);
    k_set_text(h_lbl_filename, current_file);
    k_set_text(h_text_buffer, "");
    update_status("New document");
}

static void on_open_file(void) {
    char path[256] = {0};
    k_get_text(h_lbl_filename, path, (int)sizeof(path) - 1);
    if (strlen(path) > 0) {
        load_file(path);
    }
}

static void on_save_file(void) {
    char path[256] = {0};
    k_get_text(h_lbl_filename, path, (int)sizeof(path) - 1);
    if (strlen(path) == 0) {
        strncpy(path, current_file, sizeof(path) - 1);
    }

    char buf[16384];
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

static void on_save_as_file(void) {
    char path[256] = {0};
    k_get_text(h_lbl_filename, path, (int)sizeof(path) - 1);
    if (strlen(path) == 0 || strcmp(path, current_file) == 0) {
        update_status("Specify destination in toolbar field, then click Save");
    } else {
        on_save_file();
    }
}

static void show_about(void) {
    static const char ok_btn[] = "OK";
    k_msgbox("About AeroEditor",
             "AeroEditor 2.0 (Native ELF Edition)\n"
             "High-performance native text editor for Windose OS.\n"
             "Features MenuBar, live status bar, and multiline editing.",
             ok_btn, (int)sizeof(ok_btn));
}

int main(int argc, char **argv) {
    main_win = k_create_window_with_icon(800, 520, "AeroEditor", "/mnt/System/Icons/notepad.png");
    if (!main_win) main_win = k_create_window(800, 520, "AeroEditor");
    if (!main_win) return 1;

    /* Menu bar */
    handle_t mbar = k_menubar_create(main_win);
    if (mbar) {
        m_new = k_menu_add_item(mbar, "File", "New");
        m_open = k_menu_add_item(mbar, "File", "Open...");
        m_save = k_menu_add_item(mbar, "File", "Save");
        m_saveas = k_menu_add_item(mbar, "File", "Save As...");
        k_menu_add_item(mbar, "File", "-");
        m_exit = k_menu_add_item(mbar, "File", "Exit");

        m_clear = k_menu_add_item(mbar, "Edit", "Clear All");
        m_about = k_menu_add_item(mbar, "Help", "About AeroEditor");
    }

    /* Toolbar buttons */
    h_btn_new = k_create_button(main_win, 10, 8, 65, 28, "New");
    k_widget_set_anchors(h_btn_new, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_open = k_create_button(main_win, 80, 8, 65, 28, "Open");
    k_widget_set_anchors(h_btn_open, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_save = k_create_button(main_win, 150, 8, 65, 28, "Save");
    k_widget_set_anchors(h_btn_save, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_saveas = k_create_button(main_win, 220, 8, 75, 28, "Save As");
    k_widget_set_anchors(h_btn_saveas, ANCHOR_LEFT | ANCHOR_TOP);

    /* Filename input field */
    h_lbl_filename = k_create_textfield(main_win, 305, 8, 485);
    k_textfield_setup(h_lbl_filename, 256, 0, 0);
    k_widget_set_anchors(h_lbl_filename, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT);

    /* Multiline text editor */
    h_text_buffer = k_create_textfield(main_win, 10, 42, 780);
    k_textfield_setup(h_text_buffer, 32768, 0, 0);
    k_textfield_set_multiline(h_text_buffer, 1);
    k_widget_set_anchors(h_text_buffer, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT | ANCHOR_BOTTOM);
    k_widget_set_bounds(h_text_buffer, 10, 42, 780, 436);

    /* Status bar */
    h_status_bar = k_statusbar_create(main_win, 0, 488, 800);
    k_status_add_panel(h_status_bar, "Ready", 420);
    k_status_add_panel(h_status_bar, "Lines: 1 | Size: 0 B", 200);
    k_status_add_panel(h_status_bar, "UTF-8 | ELF Native", 180);
    k_widget_set_anchors(h_status_bar, ANCHOR_LEFT | ANCHOR_RIGHT | ANCHOR_BOTTOM);

    if (argc > 1 && argv[1] && argv[1][0]) {
        load_file(argv[1]);
    } else {
        k_set_text(h_lbl_filename, current_file);
        k_set_text(h_text_buffer, "/* Windose OS Native AeroEditor */\n#include <stdio.h>\n#include \"kernel.h\"\n\nint main(int argc, char **argv) {\n    printf(\"Hello from native Windose ELF application!\\n\");\n    return 0;\n}\n");
        update_status("Welcome");
    }

    for (;;) {
        Event e;
        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE && e.handle == main_win) {
                k_close(main_win);
                return 0;
            }
            if (e.type == EV_CLICK) {
                if (e.handle == h_btn_new || e.handle == m_new) {
                    on_new_file();
                } else if (e.handle == h_btn_open || e.handle == m_open) {
                    on_open_file();
                } else if (e.handle == h_btn_save || e.handle == m_save) {
                    on_save_file();
                } else if (e.handle == h_btn_saveas || e.handle == m_saveas) {
                    on_save_as_file();
                } else if (e.handle == m_clear) {
                    k_set_text(h_text_buffer, "");
                    update_status("Cleared");
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
