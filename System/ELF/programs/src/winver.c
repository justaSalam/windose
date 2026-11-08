/**
 * Windose OS Native About Dialog (ELF)
 * Displays OS version, kernel API, system uptime, and copyright.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

int main(int argc, char **argv) {
    (void)argc; (void)argv;

    handle_t win = k_create_window_ex(160, 140, 420, 280, "About Windose", WIN_FLAG_NO_RESIZE);
    if (!win) return 1;

    k_set_icon(win, "/mnt/System/Icons/about.png");

    /* OS & Branding Labels */
    k_create_label(win, 16, 16, "Windose Operating System v10 (Aero)");
    k_create_label(win, 16, 40, "Kernel Version 14 (Native x86-64 ELF Subsystem)");

    /* Query process count from ps_list */
    char ps_buf[4096];
    int n = k_ps_list(ps_buf, (int)sizeof(ps_buf));
    int proc_count = 0;
    if (n > 0) {
        ps_buf[n] = '\0';
        for (int i = 0; i < n; i++) {
            if (ps_buf[i] == '\n') proc_count++;
        }
    }

    char stat_str[128];
    snprintf(stat_str, sizeof(stat_str), "Active Processes: %d | Host: Cosmos NativeAOT x64", proc_count > 0 ? proc_count : 1);
    k_create_label(win, 16, 74, stat_str);

    int ticks = k_ticks();
    int total_sec = ticks > 0 ? ticks / 1000 : 0;
    int mins = total_sec / 60;
    int secs = total_sec % 60;

    char uptime_str[128];
    snprintf(uptime_str, sizeof(uptime_str), "System Uptime: %d min %d sec", mins, secs);
    k_create_label(win, 16, 98, uptime_str);

    k_create_label(win, 16, 130, "Protected by Windose Kernel Session & Process Subsystem.");
    k_create_label(win, 16, 154, "Copyright (c) 2026 Windose Project Contributors.");
    k_create_label(win, 16, 178, "All rights reserved.");

    /* OK Button */
    handle_t btn_ok = k_create_button(win, 320, 230, 80, 28, "OK");

    for (;;) {
        Event e;
        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE && e.handle == win) {
                k_close(win);
                return 0;
            }
            if (e.type == EV_KEY) {
                char ch = (char)(e.b & 0xFFFF);
                if (ch == 27 || ch == '\r') {
                    k_close(win);
                    return 0;
                }
            }
            if (e.type == EV_CLICK && e.handle == btn_ok) {
                k_close(win);
                return 0;
            }
        }
        k_sleep(20);
    }
}
