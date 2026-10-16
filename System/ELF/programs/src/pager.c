#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

// Virtual desktop pager: one button per desktop, current marked. Esc quits.
int main(int argc, char **argv)
{
    handle_t win, btns[8];
    int count, i, cur = -1;
    char label[16];
    (void)argc; (void)argv;

    count = k_desktop_count();
    if (count <= 0) { puts("no desktops"); return 1; }
    if (count > 8) count = 8;

    win = k_create_window(64 + count * 56, 92, "Desktops");
    if (!win) { puts("window creation failed"); return 1; }

    for (i = 0; i < count; i++) {
        snprintf(label, sizeof label, "%d", i + 1);
        btns[i] = k_create_button(win, 16 + i * 56, 12, 48, 32, label);
        if (!btns[i]) { puts("button failed"); k_close(win); return 1; }
    }

    for (;;) {
        Event e;
        int now;
        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE && e.handle == win) { k_close(win); return 0; }
            if (e.type == EV_KEY && (e.b & 0xFFFF) == 27) { k_close(win); return 0; }
            if (e.type != EV_CLICK) continue;
            for (i = 0; i < count; i++)
                if (e.handle == btns[i]) { k_desktop_switch(i); break; }
        }
        now = k_desktop_current();
        if (now != cur) {
            cur = now;
            for (i = 0; i < count; i++) {
                snprintf(label, sizeof label, "%s%d", i == cur ? "> " : "", i + 1);
                k_set_text(btns[i], label);
            }
        }
        k_sleep(200);
    }
}
