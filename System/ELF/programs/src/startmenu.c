#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

// Windose ELF Start Menu
// Frameless popup positioned right above the taskbar.
// Supports app launching, live search filter, and dynamic shell restart.

typedef struct {
    const char *name;
    const char *cmd;
    const char *arg;
} ProgItem;

static const ProgItem ITEMS[] = {
    { "File Explorer",        "/mnt/Programs/explorer.elf", "/mnt" },
    { "Command Prompt",       "/mnt/Programs/terminal.elf", "" },
    { "Task Manager",         "taskmgr", "" },
    { "Text Editor",          "/mnt/Programs/aeroeditor.elf", "" },
    { "Calculator",           "/mnt/Programs/calc.elf", "" },
    { "Control Panel",        "control", "" },
    { "Display Settings",     "settings", "" },
    { "Device Manager",       "devicemanager", "" },
    { "Disk Management",      "diskmanagement", "" },
    { "File Search",          "/mnt/Programs/search.elf", "" },
    { "Widgets Gallery",      "/mnt/Programs/widgets.elf", "" },
    { "Canvas Graphics",      "/mnt/Programs/canvas.elf", "" },
    { "Memory Monitor",       "/mnt/Programs/mem.elf", "" }
};
#define NUM_ITEMS (int)(sizeof(ITEMS) / sizeof(ITEMS[0]))

static int filtered[NUM_ITEMS];
static int filter_count = 0;
static char curr_search[64] = "";

static int str_contains_nocase(const char *haystack, const char *needle) {
    if (!needle || !needle[0]) return 1;
    if (!haystack) return 0;
    int hlen = (int)strlen(haystack);
    int nlen = (int)strlen(needle);
    if (nlen > hlen) return 0;
    for (int i = 0; i <= hlen - nlen; i++) {
        int match = 1;
        for (int j = 0; j < nlen; j++) {
            char c1 = haystack[i + j];
            char c2 = needle[j];
            if (c1 >= 'A' && c1 <= 'Z') c1 += 32;
            if (c2 >= 'A' && c2 <= 'Z') c2 += 32;
            if (c1 != c2) {
                match = 0;
                break;
            }
        }
        if (match) return 1;
    }
    return 0;
}

static void update_filter(handle_t list, const char *query) {
    k_list_clear(list);
    filter_count = 0;
    for (int i = 0; i < NUM_ITEMS; i++) {
        if (str_contains_nocase(ITEMS[i].name, query)) {
            filtered[filter_count] = i;
            k_list_add_item(list, ITEMS[i].name);
            filter_count++;
        }
    }
    if (filter_count > 0) {
        k_list_select(list, 0);
    }
}

static void launch_prog(int sel_idx) {
    if (sel_idx < 0 || sel_idx >= filter_count) return;
    int idx = filtered[sel_idx];
    const ProgItem *it = &ITEMS[idx];
    if (it->cmd && it->cmd[0]) {
        if (strstr(it->cmd, ".elf")) {
            const char *arg = it->arg;
            int arglen = (arg && arg[0]) ? (int)strlen(arg) + 1 : 0;
            k_spawn(it->cmd, arg, arglen);
        } else {
            k_launch_app(it->cmd, it->arg);
        }
    }
}

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    int sw = 1024, sh = 768;
    k_screen_size(&sw, &sh);

    int mw = 340;
    int mh = 480;
    int taskbar_h = 36;
    int mx = 0;
    int my = sh - taskbar_h - mh;
    if (my < 0) my = 0;

    int flags = WIN_FLAG_NO_TITLEBAR | WIN_FLAG_NO_TASKBAR | WIN_FLAG_POPUP;
    handle_t win = k_create_window_ex(mx, my, mw, mh, "Start Menu", flags);
    if (!win) return 1;

    k_set_icon(win, "/mnt/System/Icons/start_menu_xp.png");

    k_create_label(win, 12, 10, "Windose NativeAOT");
    k_create_label(win, 12, 28, "Logged in as User");

    handle_t search_box = k_create_textfield(win, 10, 52, mw - 20);
    k_textfield_setup(search_box, 64, 0, 0);

    handle_t prog_list = k_create_listbox(win, 10, 84, mw - 20, mh - 138);
    update_filter(prog_list, "");

    handle_t btn_restart = k_create_button(win, 10, mh - 46, 110, 36, "Restart Shell");
    handle_t btn_lock    = k_create_button(win, 126, mh - 46, 60, 36, "Lock");
    handle_t btn_power   = k_create_button(win, 192, mh - 46, 76, 36, "Shutdown");
    handle_t btn_close   = k_create_button(win, 274, mh - 46, 56, 36, "Close");

    Event e;
    char text_buf[64];

    while (1) {
        if (k_get_text(search_box, text_buf, (int)sizeof(text_buf)) >= 0) {
            if (strcmp(text_buf, curr_search) != 0) {
                strncpy(curr_search, text_buf, sizeof(curr_search) - 1);
                curr_search[sizeof(curr_search) - 1] = '\0';
                update_filter(prog_list, curr_search);
            }
        }

        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE) {
                k_close(win);
                return 0;
            }

            if (e.type == EV_KEY) {
                char ch = (char)(e.b & 0xFFFF);
                if (e.a == 27 || ch == 27) {
                    k_close(win);
                    return 0;
                }
                if (e.a == '\r' || e.a == 10 || ch == '\r' || ch == 10) {
                    int sel = k_list_get_selected(prog_list);
                    if (sel >= 0) {
                        launch_prog(sel);
                        k_close(win);
                        return 0;
                    }
                }
            }

            if (e.type == EV_CLICK) {
                if (e.handle == btn_restart) {
                    k_restart_shell();
                    k_close(win);
                    return 0;
                }
                if (e.handle == btn_lock) {
                    k_launch_app("lock", "");
                    k_close(win);
                    return 0;
                }
                if (e.handle == btn_power) {
                    k_launch_app("shutdown", "");
                    k_close(win);
                    return 0;
                }
                if (e.handle == btn_close) {
                    k_close(win);
                    return 0;
                }
            }

            if (e.type == EV_DBLCLICK) {
                if (e.handle == prog_list) {
                    int sel = k_list_get_selected(prog_list);
                    if (sel >= 0) {
                        launch_prog(sel);
                        k_close(win);
                        return 0;
                    }
                }
            }
        }

        k_sleep(20);
    }

    k_close(win);
    return 0;
}
