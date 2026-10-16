#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

// ELF file explorer, C# FileExplorer layout: menu bar, toolbar, address bar with
// type-ahead, live search filter, drive-rooted tree, details list, status bar,
// context menus, back/forward history, text viewer. Double-click opens.
// usage: explorer [path]
#define MAXENT 512
#define MAXPATH 256
#define LISTBUF (64 * 1024)
#define VIEWBUF (48 * 1024)
#define VIEWROWS 16
#define COPYMAX (1024 * 1024)
#define MAXTNODES 200
#define HISTMAX 32

typedef struct { char name[256]; int is_dir; long size; } Entry;
typedef struct { handle_t h; char path[MAXPATH]; int loaded; } TNode;

static handle_t win, tree, files, addrField, searchField;
static handle_t sb;
static int sbObjs, sbSel;
static handle_t tBack, tForward, tUp, tCut, tCopy, tPaste, tDelete, tRefresh;
static handle_t mClose, mCut, mCopy, mPaste, mDelete, mRename;
static handle_t mRefresh, mSortName, mSortSize, mUp, mRoot, mAbout;
static handle_t vpPopup, vpRefresh, vpNewFile, vpNewFolder, vpEmpty;
static handle_t itPopup, itOpen, itRename, itDelete;

static char cwd[MAXPATH];
static Entry *entries;
static int nentries, selected;
static int view[MAXENT];
static int vcount;
static int sortBySize;
static int inputMode;          // 0 none, 1 rename, 2 new folder, 3 delete confirm, 4 go to, 5 new file
static char input[256];
static char listbuf[LISTBUF];
static char clipPath[MAXPATH];
static int clipCut;
static handle_t focusedH;
static char searchbuf[128];

static TNode tnodes[MAXTNODES];
static int tncount;

static char hist[HISTMAX][MAXPATH];
static int hlen, hpos;

// viewer state (one at a time)
static handle_t vwin, vprev, vnext, vclose;
static handle_t vrows[VIEWROWS];
static char *vtext;
static char **vlines;
static int vlinecount, vpage, vpages;

static const char *ICON_DIR = "/mnt/System/Icons/directory_closed.png";
static const char *ICON_DRIVE = "/mnt/System/Icons/hard_disk_drive.png";

static void refresh(void);
static void navigate_push(const char *path);
static void open_viewer(const char *path);
static void close_viewer(void);
static void paint_viewer(void);
static void paint_rows(void);
static void on_row_selected(void);
static void go_up_hist(void);
static void refilter(void);

static void set_objs(const char *s) { k_status_set_text(sb, sbObjs, s); }
static void set_sel(const char *s) { k_status_set_text(sb, sbSel, s); }

static void join_path(const char *dir, const char *name, char *out)
{
    if (!strcmp(dir, "/")) snprintf(out, MAXPATH, "/%s", name);
    else snprintf(out, MAXPATH, "%s/%s", dir, name);
}

static void base_name(const char *path, char *out)
{
    const char *p = path + strlen(path);
    while (p > path && *(p - 1) != '/') p--;
    strncpy(out, p, 255);
    out[255] = 0;
}

static void fmt_size(long size, char *out)
{
    if (size < 1024) snprintf(out, 16, "%ld B", size);
    else if (size < 1024L * 1024L) snprintf(out, 16, "%ld KB", size / 1024);
    else if (size < 1024L * 1024L * 1024L) snprintf(out, 16, "%ld MB", size / (1024L * 1024L));
    else snprintf(out, 16, "%ld GB", size / (1024L * 1024L * 1024L));
}

static void fmt_drive(unsigned lo, unsigned hi, char *out)
{
    long total = ((long)hi << 32) | lo;
    fmt_size(total, out);
}

static void file_type(const char *name, int is_dir, char *out)
{
    int n;
    if (is_dir) { strcpy(out, "File Folder"); return; }
    n = (int)strlen(name);
    if (n > 4 && !strcmp(name + n - 4, ".elf")) strcpy(out, "ELF Program");
    else if (n > 4 && !strcmp(name + n - 4, ".txt")) strcpy(out, "Text File");
    else if (n > 7 && !strcmp(name + n - 7, ".breeze")) strcpy(out, "Breeze Script");
    else strcpy(out, "File");
}

static int cmp_entry(const void *a, const void *b)
{
    const Entry *x = (const Entry *)a, *y = (const Entry *)b;
    if (x->is_dir != y->is_dir) return y->is_dir - x->is_dir;
    if (sortBySize && !x->is_dir && !y->is_dir) {
        if (x->size != y->size) return x->size < y->size ? 1 : -1;
    }
    return strcmp(x->name, y->name);
}

static int name_matches(const char *name)
{
    int i, j;
    if (!searchbuf[0]) return 1;
    for (i = 0; name[i]; i++) {
        for (j = 0; ; j++) {
            char a = name[i + j], b = searchbuf[j];
            if (!b) return 1;
            if (!a) break;
            if (a >= 'A' && a <= 'Z') a += 32;
            if (b >= 'A' && b <= 'Z') b += 32;
            if (a != b) break;
        }
    }
    return 0;
}

static void refilter(void)
{
    int i, k = 0;
    for (i = 0; i < nentries && k < MAXENT; i++)
        if (name_matches(entries[i].name)) view[k++] = i;
    vcount = k;
    // keep selection only if still visible
    {
        int kept = 0, r;
        for (r = 0; r < vcount; r++)
            if (view[r] == selected) { kept = 1; break; }
        if (!kept) selected = -1;
    }
    paint_rows();
}

static int row_of(int entry_idx)
{
    int r;
    for (r = 0; r < vcount; r++)
        if (view[r] == entry_idx) return r;
    return -1;
}

static void paint_rows(void)
{
    k_list_clear(files);
    {
        int r, k;
        char sz[16], tp[32];
        char row[300];
        for (r = 0; r < vcount; r++) {
            int i = view[r], p = 0;
            const char *cells[3];
            char name[256];
            strncpy(name, entries[i].name, sizeof name - 1);
            name[sizeof name - 1] = 0;
            cells[0] = name;
            if (entries[i].is_dir) {
                cells[1] = "";
                cells[2] = "File Folder";
            } else {
                fmt_size(entries[i].size, sz);
                file_type(entries[i].name, 0, tp);
                cells[1] = sz;
                cells[2] = tp;
            }
            for (k = 0; k < 3; k++) {
                int l = (int)strlen(cells[k]) + 1;
                if (p + l >= (int)sizeof row) break;
                memcpy(row + p, cells[k], (size_t)l);
                p += l;
            }
            k_list_add_row(files, row, p);
        }
    }
}

// ---------------- tree ----------------

static int tnode_add(handle_t h, const char *path)
{
    if (tncount >= MAXTNODES || !h) return 0;
    tnodes[tncount].h = h;
    strncpy(tnodes[tncount].path, path, MAXPATH - 1);
    tnodes[tncount].path[MAXPATH - 1] = 0;
    tnodes[tncount].loaded = 0;
    tncount++;
    return 1;
}

static TNode *tnode_find(handle_t h)
{
    int i;
    for (i = 0; i < tncount; i++)
        if (tnodes[i].h == h) return &tnodes[i];
    return NULL;
}

static void tree_load_children(TNode *tn)
{
    char buf[8192], path[MAXPATH];
    FileStat st;
    int n, off, i;
    handle_t ch;
    if (!tn || tn->loaded) return;
    tn->loaded = 1;
    n = k_list_dir(tn->path, buf, (int)sizeof buf);
    if (n <= 0) return;
    off = 0;
    for (i = 0; i < n; i++) {
        char *nm = buf + off;
        off += (int)strlen(nm) + 1;
        join_path(tn->path, nm, path);
        if (k_stat_file(path, &st) != 0 || !st.is_dir) continue;
        ch = k_tree_add_child(tree, tn->h, nm, ICON_DIR);
        if (ch) tnode_add(ch, path);
        if (tncount >= MAXTNODES) return;
    }
}

static void rebuild_tree(void)
{
    char buf[4096];
    handle_t r;
    int n, off, i;
    tncount = 0;
    k_tree_clear(tree);
    n = k_list_drives(buf, (int)sizeof buf);
    if (n <= 0) {
        // no drive info: fall back to /mnt contents
        n = k_list_dir("/mnt", buf, (int)sizeof buf);
        if (n <= 0) return;
        off = 0;
        for (i = 0; i < n; i++) {
            char *nm = buf + off;
            char path[MAXPATH];
            FileStat st;
            off += (int)strlen(nm) + 1;
            join_path("/mnt", nm, path);
            if (k_stat_file(path, &st) != 0 || !st.is_dir) continue;
            r = k_tree_add_root(tree, nm, ICON_DRIVE);
            if (r && tnode_add(r, path)) tree_load_children(&tnodes[tncount - 1]);
            if (tncount >= MAXTNODES) return;
        }
        return;
    }
    off = 0;
    for (i = 0; i < n; i++) {
        char *mp = buf + off;
        char name[256];
        off += (int)strlen(mp) + 1;
        base_name(mp, name);
        if (!name[0]) strncpy(name, mp, sizeof name - 1);
        r = k_tree_add_root(tree, name, ICON_DRIVE);
        if (r && tnode_add(r, mp)) tree_load_children(&tnodes[tncount - 1]);
        if (tncount >= MAXTNODES) return;
    }
}

static int is_trash_junk(const char *dir, const char *name)
{
    int n = (int)strlen(name);
    if (n > 9 && !strcmp(name + n - 9, ".origpath")) return 1;
    if (!strcmp(name, ".trash") && !strcmp(dir, "/mnt")) return 1;
    return 0;
}

// ---------------- listing ----------------

static void refresh(void)
{
    int n, i, off;
    char path[MAXPATH];
    FileStat st;

    free(entries);
    entries = NULL;
    nentries = 0;
    vcount = 0;
    selected = -1;

    n = k_list_dir(cwd, listbuf, (int)sizeof listbuf);
    if (n < 0) { set_objs("unavailable"); return; }
    if (n > MAXENT) n = MAXENT;

    if (n > 0) {
        entries = malloc((size_t)n * sizeof(Entry));
        if (!entries) { set_objs("out of memory"); return; }
        off = 0;
        {
            int k = 0;
            for (i = 0; i < n; i++) {
                char *nm = listbuf + off;
                int nl = (int)strlen(nm);
                off += nl + 1;
                if (is_trash_junk(cwd, nm)) continue;
                strncpy(entries[k].name, nm, sizeof entries[k].name - 1);
                entries[k].name[sizeof entries[k].name - 1] = 0;
                join_path(cwd, entries[k].name, path);
                if (k_stat_file(path, &st) == 0) {
                    entries[k].is_dir = st.is_dir;
                    entries[k].size = ((long)(unsigned)st.size_hi << 32) | (unsigned)st.size_lo;
                } else {
                    entries[k].is_dir = 0;
                    entries[k].size = 0;
                }
                k++;
            }
            n = k;
        }
        nentries = n;
        qsort(entries, (size_t)n, sizeof(Entry), cmp_entry);
    }

    {
        char s[64];
        snprintf(s, sizeof s, "%d object(s)%s", nentries, sortBySize ? " [by size]" : "");
        set_objs(s);
    }
    set_sel("");
    refilter();
}

static void hist_push(const char *path)
{
    int i;
    if (hlen > 0 && !strcmp(hist[hlen - 1], path) && hpos == hlen - 1) return;
    if (hpos < hlen - 1) hlen = hpos + 1;
    if (hlen >= HISTMAX) {
        for (i = 1; i < hlen; i++) strcpy(hist[i - 1], hist[i]);
        hlen--;
    }
    strncpy(hist[hlen], path, MAXPATH - 1);
    hist[hlen][MAXPATH - 1] = 0;
    hlen++;
    hpos = hlen - 1;
}

static void navigate(const char *path)
{
    strncpy(cwd, path, sizeof cwd - 1);
    cwd[sizeof cwd - 1] = 0;
    k_set_text(addrField, cwd);
    inputMode = 0;
    refresh();
}

static void navigate_push(const char *path)
{
    navigate(path);
    hist_push(path);
}

static void go_up_hist(void)
{
    int len = (int)strlen(cwd), i;
    char p[MAXPATH];
    if (!strcmp(cwd, "/")) return;
    for (i = len - 1; i > 0 && cwd[i] != '/'; i--) ;
    if (i == 0) strcpy(p, "/");
    else { strncpy(p, cwd, (size_t)i); p[i] = 0; }
    navigate_push(p);
}

static int ends_elf(const char *name)
{
    int n = (int)strlen(name);
    return n > 4 && !strcmp(name + n - 4, ".elf");
}

static void open_selected(void)
{
    char path[MAXPATH], s[320];
    static const char noargs = 0;
    if (selected < 0 || selected >= nentries) return;
    if (!strcmp(cwd, "/mnt/.trash")) {
        if (k_trash_restore(entries[selected].name) != 0) set_sel("restore failed");
        else refresh();
        return;
    }
    join_path(cwd, entries[selected].name, path);
    if (entries[selected].is_dir) { navigate_push(path); return; }
    if (ends_elf(entries[selected].name)) {
        int pid = k_spawn(path, &noargs, 0);
        if (pid < 0) { snprintf(s, sizeof s, "cannot run %s", entries[selected].name); set_sel(s); }
        return;
    }
    open_viewer(path);
}

static void on_row_selected(void)
{
    char s[320], sz[16], tp[32];
    if (selected < 0 || selected >= nentries) return;
    if (entries[selected].is_dir) {
        snprintf(s, sizeof s, "selected: %s", entries[selected].name);
    } else {
        fmt_size(entries[selected].size, sz);
        file_type(entries[selected].name, 0, tp);
        snprintf(s, sizeof s, "%s (%s, %s)", entries[selected].name, sz, tp);
    }
    set_sel(s);
}

// ---------------- viewer ----------------

static void close_viewer(void)
{
    if (vwin) { k_close(vwin); vwin = 0; }
    free(vtext); vtext = NULL;
    free(vlines); vlines = NULL;
    vlinecount = 0;
}

static void paint_viewer(void)
{
    char row[260];
    int i;
    for (i = 0; i < VIEWROWS; i++) {
        int idx = vpage * VIEWROWS + i;
        if (idx < vlinecount) {
            strncpy(row, vlines[idx], sizeof row - 1);
            row[sizeof row - 1] = 0;
            k_set_text(vrows[i], row);
        } else k_set_text(vrows[i], "");
    }
    snprintf(row, sizeof row, "viewer  (%d/%d)", vpage + 1, vpages);
    k_set_text(vwin, row);
}

static void open_viewer(const char *path)
{
    int i, n, lines, start;
    char title[300];
    close_viewer();

    vtext = malloc(VIEWBUF + 1);
    if (!vtext) { set_sel("out of memory"); return; }
    n = k_read_file(path, vtext, VIEWBUF);
    if (n < 0) { set_sel("cannot read file"); free(vtext); vtext = NULL; return; }
    vtext[n] = 0;

    lines = 1;
    for (i = 0; i < n; i++) if (vtext[i] == '\n') lines++;
    if (lines > 2000) lines = 2000;
    vlines = malloc((size_t)lines * sizeof(char *));
    if (!vlines) { free(vtext); vtext = NULL; set_sel("out of memory"); return; }
    vlinecount = 0;
    start = 0;
    for (i = 0; i <= n && vlinecount < lines; i++) {
        if (i == n || vtext[i] == '\n') {
            vtext[i] = 0;
            vlines[vlinecount++] = vtext + start;
            start = i + 1;
        }
    }
    vpage = 0;
    vpages = (vlinecount + VIEWROWS - 1) / VIEWROWS;
    if (vpages < 1) vpages = 1;

    base_name(path, title);
    vwin = k_create_window(560, 420, title);
    if (!vwin) { close_viewer(); set_sel("window failed"); return; }
    for (i = 0; i < VIEWROWS; i++)
        vrows[i] = k_create_label(vwin, 8, 8 + i * 22, "");
    vprev = k_create_button(vwin, 8, 8 + VIEWROWS * 22, 70, 26, "Prev");
    vnext = k_create_button(vwin, 84, 8 + VIEWROWS * 22, 70, 26, "Next");
    vclose = k_create_button(vwin, 160, 8 + VIEWROWS * 22, 70, 26, "Close");
    paint_viewer();
}

// ---------------- input / actions ----------------

static void start_input(int mode, const char *preset, const char *prompt)
{
    char s[320];
    inputMode = mode;
    input[0] = 0;
    if (preset) { strncpy(input, preset, sizeof input - 1); input[sizeof input - 1] = 0; }
    snprintf(s, sizeof s, "%s%s", prompt, input);
    set_sel(s);
}

static void commit_input(void)
{
    char a[MAXPATH], b[MAXPATH];
    if (inputMode == 1) {
        if (!input[0] || selected < 0 || selected >= nentries) { inputMode = 0; refresh(); return; }
        join_path(cwd, entries[selected].name, a);
        join_path(cwd, input, b);
        if (k_rename_path(a, b) != 0) set_sel("rename failed");
        inputMode = 0;
        refresh();
    } else if (inputMode == 2) {
        if (!input[0]) { inputMode = 0; refresh(); return; }
        join_path(cwd, input, a);
        if (k_make_dir(a) != 0) set_sel("cannot create folder");
        inputMode = 0;
        refresh();
    } else if (inputMode == 4) {
        FileStat st;
        if (!input[0]) { inputMode = 0; refresh(); return; }
        if (k_stat_file(input, &st) == 0 && st.is_dir) { inputMode = 0; navigate_push(input); }
        else set_sel("not a folder");
        inputMode = 0;
    } else if (inputMode == 5) {
        if (!input[0]) { inputMode = 0; refresh(); return; }
        join_path(cwd, input, a);
        {
            static const char empty = 0;
            if (k_write_file(a, &empty, 0) != 0) set_sel("cannot create file");
        }
        inputMode = 0;
        refresh();
    }
}

static void uniquify(const char *dir, const char *name, char *out)
{
    char base[240], cand[300];
    FileStat st;
    strncpy(base, name, sizeof base - 1);
    base[sizeof base - 1] = 0;
    join_path(dir, base, cand);
    if (k_stat_file(cand, &st) != 0) { strcpy(out, cand); return; }
    {
        int i;
        for (i = 2; i < 100; i++) {
            snprintf(cand, sizeof cand, "%s (%d)", base, i);
            {
                char full[MAXPATH];
                join_path(dir, cand, full);
                if (k_stat_file(full, &st) != 0) { strcpy(out, full); return; }
            }
        }
        strcpy(out, cand);
    }
}

static void do_paste(void)
{
    char dest[MAXPATH];
    FileStat st;
    char *data;
    int n;
    if (!clipPath[0]) return;
    if (k_stat_file(clipPath, &st) != 0) { set_sel("source gone"); clipPath[0] = 0; refresh(); return; }
    if (st.is_dir) { set_sel("folder paste: enter it and use New"); return; }
    if (st.size_hi != 0 || (unsigned)st.size_lo > COPYMAX) { set_sel("file too big"); return; }
    if (clipCut) {
        char tmp[MAXPATH];
        int l;
        strncpy(tmp, clipPath, sizeof tmp - 1);
        tmp[sizeof tmp - 1] = 0;
        l = (int)strlen(tmp);
        while (l > 1 && tmp[l - 1] != '/') tmp[--l] = 0;
        if (l > 1) tmp[l - 1] = 0;
        if (l <= 1) strcpy(tmp, "/");
        if (!strcmp(tmp, cwd)) { clipPath[0] = 0; clipCut = 0; refresh(); return; }
    }
    {
        char base[256];
        base_name(clipPath, base);
        uniquify(cwd, base, dest);
        data = malloc((size_t)st.size_lo ? (size_t)st.size_lo : 1);
        if (!data) { set_sel("out of memory"); return; }
        n = st.size_lo == 0 ? 0 : k_read_file(clipPath, data, (int)st.size_lo);
        if (n < 0) { set_sel("read failed"); free(data); return; }
        if (k_write_file(dest, data, n) != 0) { set_sel("write failed"); free(data); return; }
        free(data);
        if (clipCut) {
            k_delete_path(clipPath);
            clipPath[0] = 0;
            clipCut = 0;
        }
        refresh();
    }
}

static void on_clip(int cut)
{
    char s[320];
    if (selected < 0 || selected >= nentries) return;
    join_path(cwd, entries[selected].name, clipPath);
    clipCut = cut;
    snprintf(s, sizeof s, "%s: %s", cut ? "cut" : "copied", entries[selected].name);
    set_sel(s);
}

static void popup_at(handle_t widget, int lx, int ly, handle_t popup)
{
    int pos = k_widget_screen_pos(widget);
    int sx = (pos & 0xFFFF) + lx;
    int sy = ((pos >> 16) & 0xFFFF) + ly;
    k_popup_show_at(popup, sx, sy);
}

static void on_key(char ch)
{
    char s[320];
    int n;
    if (inputMode == 3) {
        if (ch == 'y' || ch == 'Y') {
            char path[MAXPATH];
            inputMode = 0;
            if (selected < 0 || selected >= nentries) { refresh(); return; }
            join_path(cwd, entries[selected].name, path);
            if (k_trash_delete(path) != 0) set_sel("delete failed");
            refresh();
        } else if (ch == 'n' || ch == 'N' || ch == 27) {
            inputMode = 0;
            refresh();
        }
        return;
    }
    if (inputMode == 1 || inputMode == 2 || inputMode == 4 || inputMode == 5) {
        if (ch == 27) { inputMode = 0; refresh(); return; }
        if (ch == '\r') { commit_input(); return; }
        if (ch == '\b') {
            n = (int)strlen(input);
            if (n > 0) input[n - 1] = 0;
        } else if (ch >= 32 && ch < 127) {
            n = (int)strlen(input);
            if (n < (int)sizeof input - 1) { input[n] = ch; input[n + 1] = 0; }
        } else return;
        snprintf(s, sizeof s, "%s%s",
                 inputMode == 1 ? "Rename to: " : inputMode == 2 ? "New folder: " :
                 inputMode == 4 ? "Go to: " : "New file: ", input);
        set_sel(s);
        return;
    }
    if (ch == 27) { k_close(win); }
}

int main(int argc, char **argv)
{
    const char *start = argc > 1 ? argv[1] : "/mnt";
    handle_t bar, tb;
    handle_t goBtn, searchLab;
    handle_t vp, it;
    strncpy(cwd, start, sizeof cwd - 1);
    cwd[sizeof cwd - 1] = 0;

    win = k_create_window(880, 452, "Explorer");
    if (!win) { puts("window creation failed"); return 1; }

    bar = k_menubar_create(win);
    mClose = k_menu_add_item(bar, "File", "Close");
    mCut = k_menu_add_item(bar, "Edit", "Cut");
    mCopy = k_menu_add_item(bar, "Edit", "Copy");
    mPaste = k_menu_add_item(bar, "Edit", "Paste");
    k_menu_add_item(bar, "Edit", "-");
    mDelete = k_menu_add_item(bar, "Edit", "Delete");
    mRename = k_menu_add_item(bar, "Edit", "Rename");
    mRefresh = k_menu_add_item(bar, "View", "Refresh");
    mSortName = k_menu_add_item(bar, "View", "Sort by Name");
    mSortSize = k_menu_add_item(bar, "View", "Sort by Size");
    mUp = k_menu_add_item(bar, "Go", "Up");
    mRoot = k_menu_add_item(bar, "Go", "Root");
    mAbout = k_menu_add_item(bar, "Help", "About");

    tb = k_toolbar_create(win, 0, 22, 880);
    tBack = k_toolbar_add_button(tb, "Back");
    tForward = k_toolbar_add_button(tb, "Forward");
    tUp = k_toolbar_add_button(tb, "Up");
    k_toolbar_add_separator(tb);
    tCut = k_toolbar_add_button(tb, "Cut");
    tCopy = k_toolbar_add_button(tb, "Copy");
    tPaste = k_toolbar_add_button(tb, "Paste");
    k_toolbar_add_separator(tb);
    tDelete = k_toolbar_add_button(tb, "Delete");
    k_toolbar_add_separator(tb);
    tRefresh = k_toolbar_add_button(tb, "Refresh");

    k_create_label(win, 8, 54, "Address:");
    addrField = k_create_textfield(win, 76, 54, 380);
    goBtn = k_create_button(win, 462, 54, 60, 24, "Go");
    searchLab = k_create_label(win, 528, 54, "Search:");
    searchField = k_create_textfield(win, 594, 54, 278);
    if (!searchLab) { puts("component creation failed"); k_close(win); return 1; }

    tree = k_tree_create(win, 8, 86, 190, 336);
    files = k_create_listbox(win, 206, 86, 666, 336);
    k_list_add_column(files, "Name", 340);
    k_list_add_column(files, "Size", 110);
    k_list_add_column(files, "Type", 150);

    sb = k_statusbar_create(win, 8, 430, 864);
    sbObjs = k_status_add_panel(sb, "", 220);
    sbSel = k_status_add_panel(sb, "", 400);
    if (!tree || !files || !bar || !tb) { puts("component creation failed"); k_close(win); return 1; }

    vp = k_popup_create();
    vpRefresh = k_popup_add_item(vp, "Refresh");
    k_popup_add_item(vp, "-");
    vpNewFile = k_popup_add_item(vp, "New File");
    vpNewFolder = k_popup_add_item(vp, "New Folder");
    k_popup_add_item(vp, "-");
    vpEmpty = k_popup_add_item(vp, "Empty Recycle Bin");
    vpPopup = vp;

    it = k_popup_create();
    itOpen = k_popup_add_item(it, "Open");
    itRename = k_popup_add_item(it, "Rename");
    k_popup_add_item(it, "-");
    itDelete = k_popup_add_item(it, "Delete");
    itPopup = it;

    rebuild_tree();
    strncpy(cwd, start, sizeof cwd - 1);
    hist_push(cwd);
    navigate(cwd);

    for (;;) {
        Event e;
        while (k_poll_event(&e)) {
            if (e.handle == vwin || e.handle == vprev || e.handle == vnext || e.handle == vclose) {
                if (e.type == EV_CLOSE) { close_viewer(); continue; }
                if (e.type == EV_KEY && (e.b & 0xFFFF) == 27) { close_viewer(); continue; }
                if (e.type != EV_CLICK) continue;
                if (e.handle == vclose) close_viewer();
                else if (e.handle == vprev && vpage > 0) { vpage--; paint_viewer(); }
                else if (e.handle == vnext && vpage + 1 < vpages) { vpage++; paint_viewer(); }
                continue;
            }
            if (e.type == EV_CLOSE && e.handle == win) {
                k_close(win);
                free(entries);
                close_viewer();
                return 0;
            }
            if (e.type == EV_FOCUS) {
                if (e.a == 1) focusedH = e.handle;
                else if (e.handle == focusedH) focusedH = 0;
                continue;
            }
            if (e.type == EV_KEY) {
                char ch = (char)(e.b & 0xFFFF);
                if (inputMode != 0) { on_key(ch); continue; }
                if (focusedH == addrField) {
                    // typing is native; Enter navigates, Esc resyncs
                    if (ch == '\r') {
                        char p[300];
                        if (k_get_text(addrField, p, (int)sizeof p) >= 0) {
                            FileStat st;
                            if (k_stat_file(p, &st) == 0 && st.is_dir) navigate_push(p);
                            else set_sel("not a folder");
                        }
                    } else if (ch == 27) {
                        k_set_text(addrField, cwd);
                    }
                    continue;
                }
                if (focusedH == searchField) {
                    if (ch == 27) {
                        k_set_text(searchField, "");
                        searchbuf[0] = 0;
                        refilter();
                    } else {
                        char cur[128];
                        if (k_get_text(searchField, cur, (int)sizeof cur) >= 0 &&
                            strcmp(cur, searchbuf)) {
                            strncpy(searchbuf, cur, sizeof searchbuf - 1);
                            searchbuf[sizeof searchbuf - 1] = 0;
                            refilter();
                        }
                    }
                    continue;
                }
                on_key(ch);
                continue;
            }
            if (e.type == EV_MOUSEDOWN && (e.a >> 28) == 2) {
                int lx = e.a & 0xFFFFFFF, ly = e.b;
                if (e.handle == files) {
                    int idx = k_list_hit_test(files, lx, ly);
                    if (idx >= 0 && idx < vcount) {
                        selected = view[idx];
                        k_list_select(files, idx);
                        on_row_selected();
                        popup_at(files, lx, ly, itPopup);
                    } else popup_at(files, lx, ly, vpPopup);
                } else if (e.handle == tree) {
                    if (k_tree_selected_item(tree) != 0) popup_at(tree, lx, ly, itPopup);
                    else popup_at(tree, lx, ly, vpPopup);
                }
                continue;
            }
            if (e.type == EV_DBLCLICK) {
                if (e.handle == files) {
                    if (e.a >= 0 && e.a < vcount) {
                        selected = view[e.a];
                        open_selected();
                    }
                } else if (e.handle == tree) {
                    TNode *tn = tnode_find(e.a);
                    if (tn) navigate_push(tn->path);
                }
                continue;
            }
            if (e.type != EV_CLICK) continue;
            if (e.handle == goBtn) {
                char p[300];
                if (k_get_text(addrField, p, (int)sizeof p) >= 0) {
                    FileStat st;
                    if (k_stat_file(p, &st) == 0 && st.is_dir) navigate_push(p);
                    else set_sel("not a folder");
                }
            }
            else if (e.handle == tBack || e.handle == tForward || e.handle == tUp ||
                     e.handle == mUp || e.handle == mRoot) {
                if (e.handle == tBack) {
                    if (hpos > 0) { hpos--; navigate(hist[hpos]); }
                } else if (e.handle == tForward) {
                    if (hpos + 1 < hlen) { hpos++; navigate(hist[hpos]); }
                } else if (e.handle == tUp || e.handle == mUp) go_up_hist();
                else navigate_push("/mnt");
            }
            else if (e.handle == tRefresh || e.handle == mRefresh || e.handle == vpRefresh) {
                inputMode = 0;
                rebuild_tree();
                refresh();
            }
            else if (e.handle == mSortName) { sortBySize = 0; refresh(); }
            else if (e.handle == mSortSize) { sortBySize = 1; refresh(); }
            else if (e.handle == mAbout) {
                static const char ab[] = "OK\0";
                k_msgbox("Explorer", "ELF file explorer.", ab, (int)sizeof ab);
            }
            else if (e.handle == mClose) { k_close(win); free(entries); close_viewer(); return 0; }
            else if (e.handle == tCut || e.handle == mCut) on_clip(1);
            else if (e.handle == tCopy || e.handle == mCopy) on_clip(0);
            else if (e.handle == tPaste || e.handle == mPaste) do_paste();
            else if (e.handle == tDelete || e.handle == mDelete || e.handle == itDelete) {
                if (selected >= 0 && selected < nentries) {
                    char s[320];
                    snprintf(s, sizeof s, "Delete %s? (y/n)", entries[selected].name);
                    inputMode = 3;
                    set_sel(s);
                }
            }
            else if (e.handle == mRename || e.handle == itRename) {
                if (selected >= 0 && selected < nentries)
                    start_input(1, entries[selected].name, "Rename to: ");
            }
            else if (e.handle == vpNewFolder) start_input(2, NULL, "New folder: ");
            else if (e.handle == vpNewFile) start_input(5, NULL, "New file: ");
            else if (e.handle == vpEmpty) {
                k_trash_empty();
                refresh();
            }
            else if (e.handle == itOpen) open_selected();
            else if (e.handle == tree) {
                TNode *tn = tnode_find(e.a);
                if (tn) {
                    tree_load_children(tn);
                    navigate_push(tn->path);
                    {
                        DriveStat ds;
                        if (k_drive_stat(tn->path, &ds) == 0) {
                            char f[16], t[16], s[96];
                            fmt_drive((unsigned)ds.free_lo, (unsigned)ds.free_hi, f);
                            fmt_drive((unsigned)ds.total_lo, (unsigned)ds.total_hi, t);
                            snprintf(s, sizeof s, "%s free of %s", f, t);
                            set_sel(s);
                        }
                    }
                }
            }
            else if (e.handle == files) {
                if (e.a >= 0 && e.a < vcount) {
                    selected = view[e.a];
                    on_row_selected();
                }
            }
        }
        k_sleep(10);
    }
}
