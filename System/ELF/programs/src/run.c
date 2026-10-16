#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

// Run dialog: type a program name, path, or file. Enter runs, Esc closes.
// Tries: built-in app name, then path as given, then /mnt/Programs/<name>[.elf].
int main(int argc, char **argv)
{
    handle_t win, field, ok, cancel, status;
    char cmd[256];
    (void)argc; (void)argv;

    win = k_create_window(420, 140, "Run");
    if (!win) { puts("window creation failed"); return 1; }

    k_create_label(win, 12, 12, "Program:");
    field = k_create_textfield(win, 84, 12, 324);
    ok = k_create_button(win, 228, 52, 86, 26, "OK");
    cancel = k_create_button(win, 322, 52, 86, 26, "Cancel");
    status = k_create_label(win, 12, 88, "");
    if (!field || !ok || !status) { puts("component creation failed"); k_close(win); return 1; }

    for (;;) {
        Event e;
        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE && e.handle == win) { k_close(win); return 0; }
            if (e.type == EV_KEY) {
                char ch = (char)(e.b & 0xFFFF);
                if (ch == 27) { k_close(win); return 0; }
                if (ch == '\r') goto run;
                continue;
            }
            if (e.type != EV_CLICK) continue;
            if (e.handle == cancel) { k_close(win); return 0; }
            if (e.handle == ok) {
            run:;
                if (k_get_text(field, cmd, (int)sizeof cmd) < 0 || !cmd[0]) continue;
                {
                    char *sp = cmd;
                    while (*sp == ' ') sp++;
                    if (!*sp) continue;
                    // 1. built-in app name (first word)
                    {
                        char name[64];
                        int i = 0;
                        while (sp[i] && sp[i] != ' ' && i < (int)sizeof name - 1) {
                            char c = sp[i];
                            if (c >= 'A' && c <= 'Z') c += 32;
                            name[i] = c;
                            i++;
                        }
                        name[i] = 0;
                        if (k_launch_app(name, "") == 0) { k_close(win); return 0; }
                    }
                    // 2. path as given
                    {
                        FileStat st;
                        if (k_stat_file(sp, &st) == 0) {
                            if (k_open_file(sp) == 0) { k_close(win); return 0; }
                        }
                    }
                    // 3. /mnt/Programs/<name>[.elf]
                    {
                        char base[64], p[300];
                        int i = 0;
                        while (sp[i] && sp[i] != ' ' && i < (int)sizeof base - 1) { base[i] = sp[i]; i++; }
                        base[i] = 0;
                        snprintf(p, sizeof p, "/mnt/Programs/%s", base);
                        {
                            FileStat st;
                            if (k_stat_file(p, &st) == 0) {
                                if (k_open_file(p) == 0) { k_close(win); return 0; }
                            } else {
                                snprintf(p, sizeof p, "/mnt/Programs/%s.elf", base);
                                if (k_open_file(p) == 0) { k_close(win); return 0; }
                            }
                        }
                    }
                    k_set_text(status, "not found");
                }
            }
        }
        k_sleep(10);
    }
}
