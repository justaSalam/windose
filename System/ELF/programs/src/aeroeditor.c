/**
 * Windose OS Native AeroEditor (ELF)
 * Full-featured text editor with real file I/O, argument loading, and responsive anchoring.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

/* Component Handles */
static handle_t main_win = 0;
static handle_t h_btn_new = 0;
static handle_t h_btn_open = 0;
static handle_t h_btn_save = 0;
static handle_t h_lbl_filename = 0;
static handle_t h_text_buffer = 0;
static handle_t h_status_bar = 0;

static char current_file[256] = "/mnt/untitled.txt";

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

        char msg[128];
        snprintf(msg, sizeof(msg), "Opened %s (%d bytes)", current_file, n);
        k_status_set_text(h_status_bar, 0, msg);
    } else {
        strncpy(current_file, path, sizeof(current_file) - 1);
        current_file[sizeof(current_file) - 1] = '\0';
        k_set_text(h_lbl_filename, current_file);

        char msg[128];
        snprintf(msg, sizeof(msg), "New file: %s", path);
        k_status_set_text(h_status_bar, 0, msg);
    }
}

static void on_new_file(void) {
    strncpy(current_file, "/mnt/untitled.txt", sizeof(current_file) - 1);
    k_set_text(h_lbl_filename, current_file);
    k_set_text(h_text_buffer, "");
    k_status_set_text(h_status_bar, 0, "New file created.");
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
            char msg[128];
            snprintf(msg, sizeof(msg), "Saved %s (%d bytes)", current_file, len);
            k_status_set_text(h_status_bar, 0, msg);
        } else {
            k_status_set_text(h_status_bar, 0, "Error: Failed to save file.");
        }
    }
}

int main(int argc, char **argv) {
    main_win = k_create_window(800, 500, "AeroEditor");
    if (!main_win) return 1;

    /* toolbar buttons */
    h_btn_new = k_create_button(main_win, 12, 8, 72, 28, "📄 New");
    k_widget_set_anchors(h_btn_new, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_open = k_create_button(main_win, 90, 8, 72, 28, "📂 Open");
    k_widget_set_anchors(h_btn_open, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_save = k_create_button(main_win, 168, 8, 72, 28, "💾 Save");
    k_widget_set_anchors(h_btn_save, ANCHOR_LEFT | ANCHOR_TOP);

    /* filename input field */
    h_lbl_filename = k_create_textfield(main_win, 250, 8, 538);
    k_textfield_setup(h_lbl_filename, 256, 0, 0);
    k_widget_set_anchors(h_lbl_filename, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT);

    /* multiline text editor */
    h_text_buffer = k_create_textfield(main_win, 12, 44, 776);
    k_textfield_setup(h_text_buffer, 32768, 0, 0);
    k_textfield_set_multiline(h_text_buffer, 1);
    k_widget_set_anchors(h_text_buffer, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT | ANCHOR_BOTTOM);
    k_widget_set_bounds(h_text_buffer, 12, 44, 776, 420);

    /* status bar */
    h_status_bar = k_statusbar_create(main_win, 0, 472, 800);
    k_status_add_panel(h_status_bar, "Ready | UTF-8 | Windose Native", 800);
    k_widget_set_anchors(h_status_bar, ANCHOR_LEFT | ANCHOR_RIGHT | ANCHOR_BOTTOM);

    if (argc > 1 && argv[1] && argv[1][0]) {
        load_file(argv[1]);
    } else {
        k_set_text(h_lbl_filename, current_file);
        k_set_text(h_text_buffer, "/* Windose OS Native AeroEditor */\n#include <stdio.h>\n#include \"kernel.h\"\n\nint main(int argc, char **argv) {\n    printf(\"Hello from native Windose ELF application!\n\");\n    return 0;\n}\n");
    }

    for (;;) {
        Event e;
        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE && e.handle == main_win) {
                k_close(main_win);
                return 0;
            }
            if (e.type == EV_CLICK) {
                if (e.handle == h_btn_new) {
                    on_new_file();
                } else if (e.handle == h_btn_open) {
                    on_open_file();
                } else if (e.handle == h_btn_save) {
                    on_save_file();
                }
            }
        }
        k_sleep(10);
    }
}
