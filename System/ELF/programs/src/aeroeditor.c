/**
 * Windose OS Native AeroEditor IDE (ELF)
 * Integrated C Development Environment with MenuBar, Build actions,
 * live compilation via TinyCC, docked Output Console, and F5 one-click execution.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

/* Component Handles */
static handle_t main_win = 0;

/* Menu handles */
static handle_t m_new = 0;
static handle_t m_open = 0;
static handle_t m_save = 0;
static handle_t m_saveas = 0;
static handle_t m_exit = 0;

static handle_t m_clear = 0;

static handle_t m_compile = 0;
static handle_t m_build_run = 0;
static handle_t m_run = 0;
static handle_t m_clean = 0;

static handle_t m_about = 0;

/* Toolbar handles */
static handle_t h_btn_new = 0;
static handle_t h_btn_open = 0;
static handle_t h_btn_save = 0;
static handle_t h_btn_saveas = 0;
static handle_t h_btn_build = 0;
static handle_t h_btn_run = 0;
static handle_t h_lbl_filename = 0;

/* Editor and Output Dock handles */
static handle_t h_text_buffer = 0;
static handle_t h_lbl_output = 0;
static handle_t h_output_buffer = 0;
static handle_t h_status_bar = 0;

static char current_file[256] = "/mnt/untitled.c";

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
    char buf[32768];
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
    strncpy(current_file, "/mnt/untitled.c", sizeof(current_file) - 1);
    k_set_text(h_lbl_filename, current_file);
    k_set_text(h_text_buffer,
               "/* Windose OS Native Application */\n"
               "#include <stdio.h>\n"
               "#include \"kernel.h\"\n\n"
               "int main(int argc, char **argv) {\n"
               "    handle_t win = k_create_window(440, 260, \"My Windose App\");\n"
               "    k_create_label(win, 20, 24, \"Hello from compiled C program!\");\n"
               "    handle_t btn = k_create_button(win, 20, 60, 100, 26, \"Close\");\n\n"
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
}

static void on_save_file(void) {
    char path[256] = {0};
    k_get_text(h_lbl_filename, path, (int)sizeof(path) - 1);
    if (strlen(path) == 0) {
        strncpy(path, current_file, sizeof(path) - 1);
    }

    char buf[32768];
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
    // 1. Auto-save current file
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

    // 2. Prepare arguments for tcc.elf:
    // arg 0: current_file
    // arg 1: -o
    // arg 2: out_elf
    // arg 3: -log
    // arg 4: /mnt/build.log
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

    // 3. Clear /mnt/build.log before run
    k_write_file("/mnt/build.log", "", 0);

    // 4. Spawn compiler
    int child_pid = k_spawn("/mnt/Programs/tcc.elf", argbuf, p);
    if (child_pid < 0) {
        log_output("[Build] error: failed to spawn compiler /mnt/Programs/tcc.elf");
        update_status("Build failed: tcc.elf not found");
        return;
    }

    // 5. Wait for compiler completion
    int exit_code = 0;
    k_wait_pid(child_pid, &exit_code, 8000);

    // 6. Read compiler output log
    char log_data[8192];
    int log_n = k_read_file("/mnt/build.log", log_data, sizeof(log_data) - 1);
    if (log_n > 0) {
        log_data[log_n] = '\0';
        log_output(log_data);
    }

    if (exit_code == 0) {
        snprintf(msg, sizeof(msg), "========== Build: 1 succeeded, 0 failed, Output: %s ==========", out_elf);
        log_output(msg);
        update_status("Build Succeeded");

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
        update_status("Build Failed");
    }
}

static void show_about(void) {
    static const char ok_btn[] = "OK";
    k_msgbox("About AeroEditor IDE",
             "AeroEditor IDE 3.0 (Native ELF Edition)\n"
             "Integrated C Development Environment for Windose OS.\n"
             "Powered by TinyCC x86_64, MenuBar, Build Pipeline, and live Output Console.",
             ok_btn, (int)sizeof(ok_btn));
}

int main(int argc, char **argv) {
    main_win = k_create_window_with_icon(880, 580, "AeroEditor IDE", "/mnt/System/Icons/notepad.png");
    if (!main_win) main_win = k_create_window(880, 580, "AeroEditor IDE");
    if (!main_win) return 1;

    /* Menu bar */
    handle_t mbar = k_menubar_create(main_win);
    if (mbar) {
        m_new = k_menu_add_item(mbar, "File", "New");
        m_open = k_menu_add_item(mbar, "File", "Open...");
        m_save = k_menu_add_item(mbar, "File", "Save (Ctrl+S)");
        m_saveas = k_menu_add_item(mbar, "File", "Save As...");
        k_menu_add_item(mbar, "File", "-");
        m_exit = k_menu_add_item(mbar, "File", "Exit");

        m_clear = k_menu_add_item(mbar, "Edit", "Clear Editor");

        m_compile = k_menu_add_item(mbar, "Build", "Compile (Ctrl+B)");
        m_build_run = k_menu_add_item(mbar, "Build", "Build & Run (F5)");
        m_run = k_menu_add_item(mbar, "Build", "Run (Ctrl+F5)");
        k_menu_add_item(mbar, "Build", "-");
        m_clean = k_menu_add_item(mbar, "Build", "Clear Output Log");

        m_about = k_menu_add_item(mbar, "Help", "About AeroEditor IDE");
    }

    /* Toolbar buttons */
    h_btn_new = k_create_button(main_win, 10, 8, 55, 28, "New");
    k_widget_set_anchors(h_btn_new, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_open = k_create_button(main_win, 68, 8, 55, 28, "Open");
    k_widget_set_anchors(h_btn_open, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_save = k_create_button(main_win, 126, 8, 55, 28, "Save");
    k_widget_set_anchors(h_btn_save, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_build = k_create_button(main_win, 184, 8, 58, 28, "Build");
    k_widget_set_anchors(h_btn_build, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_run = k_create_button(main_win, 245, 8, 72, 28, "Run (F5)");
    k_widget_set_anchors(h_btn_run, ANCHOR_LEFT | ANCHOR_TOP);

    /* Filename input field */
    h_lbl_filename = k_create_textfield(main_win, 322, 8, 545);
    k_textfield_setup(h_lbl_filename, 256, 0, 0);
    k_widget_set_anchors(h_lbl_filename, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT);

    /* Multiline text editor */
    h_text_buffer = k_create_textfield(main_win, 10, 42, 860);
    k_textfield_setup(h_text_buffer, 32768, 0, 0);
    k_textfield_set_multiline(h_text_buffer, 1);
    k_widget_set_anchors(h_text_buffer, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT | ANCHOR_BOTTOM);
    k_widget_set_bounds(h_text_buffer, 10, 42, 860, 345);

    /* Output Console Label */
    h_lbl_output = k_create_label(main_win, 10, 394, "Output & Build Diagnostics:");
    k_widget_set_anchors(h_lbl_output, ANCHOR_LEFT | ANCHOR_BOTTOM);

    /* Output Console Buffer */
    h_output_buffer = k_create_textfield(main_win, 10, 414, 860);
    k_textfield_setup(h_output_buffer, 32768, 0, 1);
    k_textfield_set_multiline(h_output_buffer, 1);
    k_widget_set_anchors(h_output_buffer, ANCHOR_LEFT | ANCHOR_RIGHT | ANCHOR_BOTTOM);
    k_widget_set_bounds(h_output_buffer, 10, 414, 860, 120);

    /* Status bar */
    h_status_bar = k_statusbar_create(main_win, 0, 548, 880);
    k_status_add_panel(h_status_bar, "Ready", 420);
    k_status_add_panel(h_status_bar, "Lines: 1 | Size: 0 B", 220);
    k_status_add_panel(h_status_bar, "TinyCC x86_64 | Windose IDE", 220);
    k_widget_set_anchors(h_status_bar, ANCHOR_LEFT | ANCHOR_RIGHT | ANCHOR_BOTTOM);

    if (argc > 1 && argv[1] && argv[1][0]) {
        load_file(argv[1]);
    } else {
        on_new_file();
    }

    log_output("[AeroEditor IDE] Workspace initialized. Ready for C development.");

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
                    k_close(main_win);
                    return 0;
                }
                // F5: Build & Run
                if (ch == 132) {
                    do_build(1);
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
                } else if (e.handle == h_btn_build || e.handle == m_compile) {
                    do_build(0);
                } else if (e.handle == h_btn_run || e.handle == m_build_run) {
                    do_build(1);
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
