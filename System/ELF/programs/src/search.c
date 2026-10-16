#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

// File search: recursive name match under /mnt, double-click/Enter opens.
// usage: search [pattern]
#define MAXRES 200
#define MAXPATH 256
#define LISTBUF (64 * 1024)
#define MAXDIRS 2000

static handle_t win, field, list, goBtn, statusLab;
static char listbuf[LISTBUF];
static char results[MAXRES][MAXPATH];
static int nresults, truncated;

static void set_status(const char *s) { k_set_text(statusLab, s); }

static int contains_ci(const char *hay, const char *needle)
{
    int i, j;
    if (!needle[0]) return 1;
    for (i = 0; hay[i]; i++) {
        for (j = 0; ; j++) {
            char a = hay[i + j], b = needle[j];
            if (!b) return 1;
            if (!a) break;
            if (a >= 'A' && a <= 'Z') a += 32;
            if (b >= 'A' && b <= 'Z') b += 32;
            if (a != b) break;
        }
    }
    return 0;
}

static void do_search(const char *pattern)
{
    // iterative stack of directories (avoids recursion depth issues)
    static char stack[96][MAXPATH];
    int top = 0, visited = 0;
    char s[128];
    nresults = 0;
    truncated = 0;
    k_list_clear(list);
    if (!pattern[0]) { set_status("type a pattern"); return; }
    strcpy(stack[0], "/mnt");
    while (top >= 0 && !truncated) {
        char dir[MAXPATH], path[MAXPATH];
        int n, off, i;
        FileStat st;
        strncpy(dir, stack[top--], MAXPATH - 1);
        dir[MAXPATH - 1] = 0;
        if (++visited > MAXDIRS) { truncated = 1; break; }
        n = k_list_dir(dir, listbuf, (int)sizeof listbuf);
        if (n <= 0) continue;
        off = 0;
        for (i = 0; i < n; i++) {
            char *nm = listbuf + off;
            off += (int)strlen(nm) + 1;
            if (dir[1] == 0) snprintf(path, sizeof path, "/%s", nm);
            else snprintf(path, sizeof path, "%s/%s", dir, nm);
            if (k_stat_file(path, &st) != 0) continue;
            if (st.is_dir) {
                if (top + 1 < 96) strcpy(stack[++top], path);
                else truncated = 1;
            }
            if (contains_ci(nm, pattern)) {
                if (nresults >= MAXRES) { truncated = 1; break; }
                strncpy(results[nresults], path, MAXPATH - 1);
                results[nresults][MAXPATH - 1] = 0;
                k_list_add_item(list, path);
                nresults++;
            }
        }
    }
    snprintf(s, sizeof s, "%d found%s", nresults, truncated ? " (truncated)" : "");
    set_status(s);
}

static void open_result(int idx)
{
    if (idx < 0 || idx >= nresults) return;
    if (k_open_file(results[idx]) != 0) set_status("cannot open");
}

int main(int argc, char **argv)
{
    handle_t goLab;
    char start[256] = "";
    if (argc > 1) { strncpy(start, argv[1], sizeof start - 1); }

    win = k_create_window(560, 400, "Search");
    if (!win) { puts("window creation failed"); return 1; }

    goLab = k_create_label(win, 8, 8, "Find:");
    (void)goLab;
    field = k_create_textfield(win, 56, 8, 400);
    goBtn = k_create_button(win, 464, 8, 80, 24, "Search");
    list = k_create_listbox(win, 8, 40, 544, 300);
    statusLab = k_create_label(win, 8, 348, "");
    if (!field || !list || !statusLab) { puts("component creation failed"); k_close(win); return 1; }

    if (start[0]) {
        k_set_text(field, start);
        do_search(start);
    }

    for (;;) {
        Event e;
        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE && e.handle == win) { k_close(win); return 0; }
            if (e.type == EV_KEY) {
                char ch = (char)(e.b & 0xFFFF);
                if (ch == 27) { k_close(win); return 0; }
                if (ch == '\r') {
                    char pat[256];
                    if (k_get_text(field, pat, (int)sizeof pat) >= 0) do_search(pat);
                }
                continue;
            }
            if (e.type == EV_DBLCLICK && e.handle == list) { open_result(e.a); continue; }
            if (e.type != EV_CLICK) continue;
            if (e.handle == goBtn) {
                char pat[256];
                if (k_get_text(field, pat, (int)sizeof pat) >= 0) do_search(pat);
            } else if (e.handle == list) {
                // single click: show full path in status
                int idx = k_list_get_selected(list);
                if (idx >= 0 && idx < nresults) set_status(results[idx]);
            }
        }
        k_sleep(10);
    }
}
