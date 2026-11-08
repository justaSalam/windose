#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

// Virtual desktop switcher: button matrix for desktops 1..N,
// active desktop visual highlighting, keyboard shortcuts 1..8 and arrows.

int main(int argc, char **argv)
{
    handle_t win, btns[8];
    int count, i, cur = -1;
    char label[32];
    (void)argc; (void)argv;

    count = k_desktop_count();
    if (count <= 0) { puts("pager: no desktops found"); return 1; }
    if (count > 8) count = 8;

    win = k_create_window_ex(100, 100, 32 + count * 54, 82, "Virtual Desktops", WIN_FLAG_NO_RESIZE);
    if (!win) { puts("pager: window creation failed"); return 1; }
    k_set_icon(win, "/mnt/System/Icons/window.png");

    for (i = 0; i < count; i++) {
        snprintf(label, sizeof label, " %d ", i + 1);
        btns[i] = k_create_button(win, 12 + i * 54, 10, 48, 30, label);
        if (!btns[i]) { puts("pager: button creation failed"); k_close(win); return 1; }
    }

    for (;;) {
        Event e;
        int now;
        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE && e.handle == win) { k_close(win); return 0; }
            if (e.type == EV_KEY) {
                char ch = (char)(e.b & 0xFFFF);
                if (ch == 27) { k_close(win); return 0; }

                // Number keys '1'..'8' switch directly to desktop
                if (ch >= '1' && ch <= '8') {
                    int target = ch - '1';
                    if (target < count) {
                        k_desktop_switch(target);
                    }
                    continue;
                }

                // Left Arrow (19) or Up Arrow (17): previous desktop
                if (ch == 19 || ch == 17) {
                    now = k_desktop_current();
                    int target = (now - 1 + count) % count;
                    k_desktop_switch(target);
                    continue;
                }

                // Right Arrow (20) or Down Arrow (18): next desktop
                if (ch == 20 || ch == 18) {
                    now = k_desktop_current();
                    int target = (now + 1) % count;
                    k_desktop_switch(target);
                    continue;
                }
                continue;
            }
            if (e.type != EV_CLICK) continue;
            for (i = 0; i < count; i++) {
                if (e.handle == btns[i]) {
                    k_desktop_switch(i);
                    break;
                }
            }
        }

        now = k_desktop_current();
        if (now != cur) {
            cur = now;
            for (i = 0; i < count; i++) {
                if (i == cur) {
                    snprintf(label, sizeof label, "[ %d ]", i + 1);
                } else {
                    snprintf(label, sizeof label, "  %d  ", i + 1);
                }
                k_set_text(btns[i], label);
            }
        }
        k_sleep(25);
    }
}
