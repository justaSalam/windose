#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

// File search: recursive name match under /mnt with 4-column ListView,
// automatic icon resolution, category filters, and right-click context menu.
// usage: search [pattern]
#define MAXRES 256
#define MAXPATH 256
#define LISTBUF (64 * 1024)
#define MAXDIRS 2000

static handle_t win, field, comboFilter, list, goBtn, clearBtn, statusLab;
static handle_t popMenu, itOpen, itOpenLoc, itCopy;
static char listbuf[LISTBUF];
static char results[MAXRES][MAXPATH];
static char resDirs[MAXRES][MAXPATH];
static long resSizes[MAXRES];
static int resIsDir[MAXRES];
static int nresults, truncated;
static int selected = -1;

static void set_status(const char *s) { k_set_text(statusLab, s); }

static int eq_ci(const char *a, const char *b)
{
    while (*a && *b) {
        char ca = *a++, cb = *b++;
        if (ca >= 'A' && ca <= 'Z') ca += 32;
        if (cb >= 'A' && cb <= 'Z') cb += 32;
        if (ca != cb) return 0;
    }
    return *a == *b;
}

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

static void fmt_size(long size, char *out)
{
    if (size < 1024) snprintf(out, 16, "%ld B", size);
    else if (size < 1024L * 1024L) snprintf(out, 16, "%ld KB", size / 1024);
    else if (size < 1024L * 1024L * 1024L) snprintf(out, 16, "%ld MB", size / (1024L * 1024L));
    else snprintf(out, 16, "%ld GB", size / (1024L * 1024L * 1024L));
}

static void file_type(const char *name, int is_dir, char *out)
{
    const char *ext;
    if (is_dir) { strcpy(out, "File Folder"); return; }
    ext = strrchr(name, '.');
    if (!ext) { strcpy(out, "File"); return; }
    if (eq_ci(ext, ".elf")) strcpy(out, "Application");
    else if (eq_ci(ext, ".txt") || eq_ci(ext, ".log")) strcpy(out, "Text Document");
    else if (eq_ci(ext, ".breeze")) strcpy(out, "Breeze Script");
    else if (eq_ci(ext, ".lua")) strcpy(out, "Lua Script");
    else if (eq_ci(ext, ".c")) strcpy(out, "C Source File");
    else if (eq_ci(ext, ".h")) strcpy(out, "C Header File");
    else if (eq_ci(ext, ".cs")) strcpy(out, "C# Source File");
    else if (eq_ci(ext, ".json")) strcpy(out, "JSON File");
    else if (eq_ci(ext, ".ini") || eq_ci(ext, ".cfg")) strcpy(out, "Configuration File");
    else if (eq_ci(ext, ".png") || eq_ci(ext, ".bmp") || eq_ci(ext, ".jpg") || eq_ci(ext, ".jpeg") || eq_ci(ext, ".gif")) strcpy(out, "Image File");
    else if (eq_ci(ext, ".wav") || eq_ci(ext, ".mp3")) strcpy(out, "Audio File");
    else if (eq_ci(ext, ".avi") || eq_ci(ext, ".mp4")) strcpy(out, "Video File");
    else if (eq_ci(ext, ".iso")) strcpy(out, "Disc Image");
    else strcpy(out, "File");
}

static int matches_filter(const char *name, int is_dir, int filter_type)
{
    const char *ext;
    if (filter_type == 0) return 1; // All Files
    if (is_dir) return 0;
    ext = strrchr(name, '.');
    if (!ext) return 0;
    if (filter_type == 1) { // Applications
        return eq_ci(ext, ".elf");
    } else if (filter_type == 2) { // Documents
        return (eq_ci(ext, ".txt") || eq_ci(ext, ".log") || eq_ci(ext, ".breeze") ||
                eq_ci(ext, ".c") || eq_ci(ext, ".h") || eq_ci(ext, ".cs") ||
                eq_ci(ext, ".json") || eq_ci(ext, ".ini") || eq_ci(ext, ".cfg") || eq_ci(ext, ".md"));
    } else if (filter_type == 3) { // Images
        return (eq_ci(ext, ".png") || eq_ci(ext, ".bmp") || eq_ci(ext, ".jpg") ||
                eq_ci(ext, ".jpeg") || eq_ci(ext, ".gif"));
    }
    return 1;
}

static void add_result_row(const char *name, const char *dir, long size, int is_dir)
{
    char szBuf[32];
    char tpBuf[48];
    char rowBuf[512];
    int p = 0;
    const char *cells[4];
    int k;

    if (is_dir) {
        szBuf[0] = 0;
        strcpy(tpBuf, "File Folder");
    } else {
        fmt_size(size, szBuf);
        file_type(name, 0, tpBuf);
    }

    cells[0] = name;
    cells[1] = dir;
    cells[2] = szBuf;
    cells[3] = tpBuf;

    for (k = 0; k < 4; k++) {
        int l = (int)strlen(cells[k]) + 1;
        if (p + l >= (int)sizeof rowBuf) break;
        memcpy(rowBuf + p, cells[k], (size_t)l);
        p += l;
    }
    k_list_add_row(list, rowBuf, p);
}

static void do_search(const char *pattern)
{
    static char stack[96][MAXPATH];
    int top = 0, visited = 0;
    char s[128];
    int filter_type = k_combo_get_selected(comboFilter);
    if (filter_type < 0) filter_type = 0;

    nresults = 0;
    truncated = 0;
    selected = -1;
    k_list_clear(list);

    if (!pattern[0]) { set_status("Type a name or wildcard pattern to search"); return; }
    set_status("Searching...");

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
            if (!strcmp(nm, ".") || !strcmp(nm, "..")) continue;
            if (dir[1] == 0) snprintf(path, sizeof path, "/%s", nm);
            else snprintf(path, sizeof path, "%s/%s", dir, nm);
            if (k_stat_file(path, &st) != 0) continue;
            if (st.is_dir) {
                if (top + 1 < 96) strcpy(stack[++top], path);
                else truncated = 1;
            }
            long fsize = ((long)st.size_hi << 32) | (unsigned int)st.size_lo;
            if (contains_ci(nm, pattern) && matches_filter(nm, st.is_dir, filter_type)) {
                if (nresults >= MAXRES) { truncated = 1; break; }
                strncpy(results[nresults], path, MAXPATH - 1);
                results[nresults][MAXPATH - 1] = 0;
                strncpy(resDirs[nresults], dir, MAXPATH - 1);
                resDirs[nresults][MAXPATH - 1] = 0;
                resSizes[nresults] = fsize;
                resIsDir[nresults] = st.is_dir;

                add_result_row(nm, dir, fsize, st.is_dir);
                nresults++;
            }
        }
    }
    snprintf(s, sizeof s, "%d item(s) found%s", nresults, truncated ? " (search limit reached)" : "");
    set_status(s);
}

static void open_result(int idx)
{
    if (idx < 0 || idx >= nresults) return;
    if (resIsDir[idx]) {
        k_spawn("/mnt/Programs/explorer.elf", results[idx], (int)strlen(results[idx]));
        return;
    }
    if (k_open_file(results[idx]) != 0) {
        if (strstr(results[idx], ".elf")) {
            k_spawn(results[idx], "", 0);
        } else {
            set_status("Cannot open selected file");
        }
    }
}

int main(int argc, char **argv)
{
    handle_t lblFind, lblType;
    char start[256] = "";
    if (argc > 1) { strncpy(start, argv[1], sizeof start - 1); }

    win = k_create_window_with_icon(680, 460, "Search", "/mnt/System/Icons/search.png");
    if (!win) { puts("search: window creation failed"); return 1; }

    lblFind = k_create_label(win, 10, 11, "Find:");
    field = k_create_textfield(win, 48, 8, 240);
    lblType = k_create_label(win, 300, 11, "Type:");
    comboFilter = k_create_combo(win, 342, 8, 150);
    goBtn = k_create_button(win, 504, 8, 80, 24, "Search");
    clearBtn = k_create_button(win, 590, 8, 70, 24, "Clear");

    list = k_create_listbox(win, 10, 38, 650, 382);
    statusLab = k_create_label(win, 10, 428, "Ready");

    k_combo_add_item(comboFilter, "All Files (*.*)");
    k_combo_add_item(comboFilter, "Programs (*.elf)");
    k_combo_add_item(comboFilter, "Documents (*.txt, *.breeze)");
    k_combo_add_item(comboFilter, "Images (*.png, *.bmp)");
    k_combo_set_selected(comboFilter, 0);

    k_list_add_column(list, "Name", 220);
    k_list_add_column(list, "In Folder", 210);
    k_list_add_column(list, "Size", 80);
    k_list_add_column(list, "Type", 120);

    k_widget_set_anchors(lblFind, ANCHOR_LEFT | ANCHOR_TOP);
    k_widget_set_anchors(field, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT);
    k_widget_set_anchors(lblType, ANCHOR_TOP | ANCHOR_RIGHT);
    k_widget_set_anchors(comboFilter, ANCHOR_TOP | ANCHOR_RIGHT);
    k_widget_set_anchors(goBtn, ANCHOR_TOP | ANCHOR_RIGHT);
    k_widget_set_anchors(clearBtn, ANCHOR_TOP | ANCHOR_RIGHT);
    k_widget_set_anchors(list, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT | ANCHOR_BOTTOM);
    k_widget_set_anchors(statusLab, ANCHOR_LEFT | ANCHOR_BOTTOM | ANCHOR_RIGHT);

    popMenu = k_popup_create();
    itOpen = k_popup_add_item(popMenu, "Open");
    itOpenLoc = k_popup_add_item(popMenu, "Open Containing Folder");
    k_popup_add_item(popMenu, "-");
    itCopy = k_popup_add_item(popMenu, "Copy Path");

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
            if (e.type == EV_MOUSEDOWN && (e.a >> 28) == 2 && e.handle == list) {
                int lx = e.a & 0xFFFFFFF, ly = e.b;
                int idx = k_list_hit_test(list, lx, ly);
                if (idx >= 0 && idx < nresults) {
                    selected = idx;
                    k_list_select(list, idx);
                    {
                        int pos = k_widget_screen_pos(list);
                        int sx = (pos & 0xFFFF) + lx;
                        int sy = ((pos >> 16) & 0xFFFF) + ly;
                        k_popup_show_at(popMenu, sx, sy);
                    }
                }
                continue;
            }
            if (e.type == EV_DBLCLICK && e.handle == list) {
                open_result(e.a);
                continue;
            }
            if (e.type != EV_CLICK) continue;

            if (e.handle == goBtn) {
                char pat[256];
                if (k_get_text(field, pat, (int)sizeof pat) >= 0) do_search(pat);
            } else if (e.handle == clearBtn) {
                k_set_text(field, "");
                k_list_clear(list);
                nresults = 0;
                selected = -1;
                set_status("Ready");
            } else if (e.handle == list) {
                int idx = k_list_get_selected(list);
                if (idx >= 0 && idx < nresults) {
                    char s[320];
                    selected = idx;
                    snprintf(s, sizeof s, "%s (%ld bytes)", results[idx], resSizes[idx]);
                    set_status(s);
                }
            } else if (e.handle == itOpen) {
                if (selected >= 0 && selected < nresults) open_result(selected);
            } else if (e.handle == itOpenLoc) {
                if (selected >= 0 && selected < nresults) {
                    k_spawn("/mnt/Programs/explorer.elf", resDirs[selected], (int)strlen(resDirs[selected]));
                }
            } else if (e.handle == itCopy) {
                if (selected >= 0 && selected < nresults) {
                    k_clipboard_set(results[selected], (int)strlen(results[selected]));
                    set_status("Copied path to clipboard");
                }
            }
        }
        k_sleep(10);
    }
}
