/**
 * Windose OS Native Task Manager (ELF)
 * Process list, resource monitor, and process termination.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

typedef struct {
    int pid;
    char name[64];
    long mem_kb;
    char state[32];
} ProcEntry;

static handle_t main_win = 0;
static handle_t h_list = 0;
static handle_t h_lbl_status = 0;
static handle_t h_btn_refresh = 0;
static handle_t h_btn_kill = 0;

static ProcEntry procs[128];
static int proc_count = 0;
static int last_refresh_tick = 0;

static void refresh_processes(void) {
    char buf[4096];
    int n = k_ps_list(buf, (int)sizeof(buf));
    if (n <= 0) return;
    buf[n] = '\0';

    proc_count = 0;
    k_list_clear(h_list);

    char *line = buf;
    while (*line && proc_count < 128) {
        char *eol = strchr(line, '\n');
        if (eol) *eol = '\0';

        if (*line) {
            int pid = 0;
            char name[64] = {0};
            long mem_bytes = 0;
            char state[32] = {0};

            char *p1 = strchr(line, ',');
            if (p1) {
                *p1 = '\0';
                pid = atoi(line);
                char *p2 = strchr(p1 + 1, ',');
                if (p2) {
                    *p2 = '\0';
                    strncpy(name, p1 + 1, sizeof(name) - 1);
                    char *p3 = strchr(p2 + 1, ',');
                    if (p3) {
                        *p3 = '\0';
                        mem_bytes = atol(p2 + 1);
                        strncpy(state, p3 + 1, sizeof(state) - 1);
                    }
                }
            }

            procs[proc_count].pid = pid;
            strncpy(procs[proc_count].name, name, sizeof(procs[proc_count].name) - 1);
            procs[proc_count].mem_kb = mem_bytes / 1024;
            strncpy(procs[proc_count].state, state, sizeof(procs[proc_count].state) - 1);

            char row[128];
            snprintf(row, sizeof(row), "PID %-4d | %-16s | %ld KB | %s",
                     pid, name, procs[proc_count].mem_kb, state);
            k_list_add_item(h_list, row);
            proc_count++;
        }

        if (!eol) break;
        line = eol + 1;
    }

    char stat[64];
    snprintf(stat, sizeof(stat), "Processes: %d", proc_count);
    k_set_text(h_lbl_status, stat);
}

static void end_selected_process(void) {
    int sel = k_list_get_selected(h_list);
    if (sel >= 0 && sel < proc_count) {
        int target = procs[sel].pid;
        if (target == k_pid()) {
            static const char ok_btn[] = "OK";
            k_msgbox("Task Manager", "Cannot terminate Task Manager itself.", ok_btn, (int)sizeof(ok_btn));
            return;
        }
        int res = k_ps_kill(target);
        if (res <= 0) {
            static const char ok_btn[] = "OK";
            k_msgbox("Task Manager", "Failed to terminate process (protected or invalid).", ok_btn, (int)sizeof(ok_btn));
        }
        refresh_processes();
    } else {
        static const char ok_btn[] = "OK";
        k_msgbox("Task Manager", "Please select a process to terminate.", ok_btn, (int)sizeof(ok_btn));
    }
}

int main(int argc, char **argv) {
    main_win = k_create_window(480, 400, "Task Manager");
    if (!main_win) return 1;

    h_list = k_create_listbox(main_win, 10, 10, 460, 330);
    k_widget_set_anchors(h_list, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT | ANCHOR_BOTTOM);

    h_lbl_status = k_create_label(main_win, 12, 356, "Processes: 0");
    k_widget_set_anchors(h_lbl_status, ANCHOR_LEFT | ANCHOR_BOTTOM);

    h_btn_refresh = k_create_button(main_win, 240, 350, 100, 30, "Refresh");
    k_widget_set_anchors(h_btn_refresh, ANCHOR_RIGHT | ANCHOR_BOTTOM);

    h_btn_kill = k_create_button(main_win, 350, 350, 120, 30, "End Process");
    k_widget_set_anchors(h_btn_kill, ANCHOR_RIGHT | ANCHOR_BOTTOM);

    refresh_processes();
    last_refresh_tick = k_ticks();

    for (;;) {
        Event e;
        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE && e.handle == main_win) {
                k_close(main_win);
                return 0;
            }
            if (e.type == EV_CLICK) {
                if (e.handle == h_btn_refresh) {
                    refresh_processes();
                    last_refresh_tick = k_ticks();
                } else if (e.handle == h_btn_kill) {
                    end_selected_process();
                    last_refresh_tick = k_ticks();
                }
            }
        }

        int now = k_ticks();
        if (now - last_refresh_tick > 3000) {
            refresh_processes();
            last_refresh_tick = now;
        }

        k_sleep(20);
    }
}
