#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

// Run dialog: Windows 7 style command launcher with description text,
// "Browse..." button, and command history navigable with Up/Down arrow keys.
// usage: run [initial_cmd]

#define MAXHIST 16

static char history[MAXHIST][256];
static int histCount = 0;
static int histIndex = -1;

static void history_push(const char *cmd)
{
    int i;
    if (!cmd || !*cmd) return;
    // Don't add duplicate if it's already top of history
    if (histCount > 0 && !strcmp(history[0], cmd)) return;

    // Shift existing history down
    for (i = (histCount < MAXHIST ? histCount : MAXHIST - 1); i > 0; i--) {
        strncpy(history[i], history[i - 1], sizeof(history[i]) - 1);
        history[i][sizeof(history[i]) - 1] = 0;
    }
    strncpy(history[0], cmd, sizeof(history[0]) - 1);
    history[0][sizeof(history[0]) - 1] = 0;
    if (histCount < MAXHIST) histCount++;
    histIndex = -1;
}

int main(int argc, char **argv)
{
    handle_t win, desc1, desc2, lblOpen, field, ok, cancel, browse, status;
    char cmd[256];
    char initCmd[256] = "";

    if (argc > 1) {
        strncpy(initCmd, argv[1], sizeof(initCmd) - 1);
    }

    // Default history items
    history_push("cmd");
    history_push("calc");
    history_push("paint");
    history_push("taskmgr");
    history_push("explorer");

    win = k_create_window_ex(140, 140, 450, 180, "Run", WIN_FLAG_NO_RESIZE);
    if (!win) { puts("run: window creation failed"); return 1; }
    k_set_icon(win, "/mnt/System/Icons/run.png");

    desc1 = k_create_label(win, 16, 12, "Type the name of a program, folder, document, or");
    desc2 = k_create_label(win, 16, 28, "Internet resource, and Windose will open it for you.");
    lblOpen = k_create_label(win, 16, 58, "Open:");
    field = k_create_textfield(win, 64, 55, 366);

    ok = k_create_button(win, 168, 96, 80, 26, "OK");
    cancel = k_create_button(win, 256, 96, 80, 26, "Cancel");
    browse = k_create_button(win, 350, 96, 80, 26, "Browse...");

    status = k_create_label(win, 16, 134, "");

    (void)desc1; (void)desc2; (void)lblOpen;

    if (initCmd[0]) {
        k_set_text(field, initCmd);
    }

    for (;;) {
        Event e;
        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE && e.handle == win) { k_close(win); return 0; }
            if (e.type == EV_KEY) {
                char ch = (char)(e.b & 0xFFFF);
                if (ch == 27) { k_close(win); return 0; }
                if (ch == '\r') goto do_run;

                // Up arrow: navigate back in history
                if (ch == 17) {
                    if (histCount > 0) {
                        if (histIndex + 1 < histCount) histIndex++;
                        k_set_text(field, history[histIndex]);
                    }
                    continue;
                }

                // Down arrow: navigate forward in history
                if (ch == 18) {
                    if (histIndex > 0) {
                        histIndex--;
                        k_set_text(field, history[histIndex]);
                    } else if (histIndex == 0) {
                        histIndex = -1;
                        k_set_text(field, "");
                    }
                    continue;
                }
                continue;
            }
            if (e.type != EV_CLICK) continue;
            if (e.handle == cancel) { k_close(win); return 0; }
            if (e.handle == browse) {
                k_spawn("/mnt/Programs/explorer.elf", "/mnt/Programs", 13);
                continue;
            }
            if (e.handle == ok) {
            do_run:;
                if (k_get_text(field, cmd, (int)sizeof cmd) < 0 || !cmd[0]) continue;
                {
                    char *sp = cmd;
                    while (*sp == ' ') sp++;
                    if (!*sp) continue;

                    history_push(sp);

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

                    // 4. /mnt/System/<name>[.elf]
                    {
                        char base[64], p[300];
                        int i = 0;
                        while (sp[i] && sp[i] != ' ' && i < (int)sizeof base - 1) { base[i] = sp[i]; i++; }
                        base[i] = 0;
                        snprintf(p, sizeof p, "/mnt/System/%s.elf", base);
                        {
                            FileStat st;
                            if (k_stat_file(p, &st) == 0) {
                                if (k_open_file(p) == 0) { k_close(win); return 0; }
                            }
                        }
                    }

                    // 5. Try k_spawn directly
                    if (k_spawn(sp, "", 0) >= 0) {
                        k_close(win);
                        return 0;
                    }

                    k_set_text(status, "Cannot find the specified item.");
                }
            }
        }
        k_sleep(10);
    }
}
