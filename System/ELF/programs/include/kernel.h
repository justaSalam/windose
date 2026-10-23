// kernel.h - the Windose kernel API as seen from C. MUST match KernelApi in kernel/ElfRunner.cs.
// Programs call the k_* wrappers below, never the table directly: the transport (function table today,
// syscall instruction one day) can then change without touching program sources.
#ifndef WINDOSE_KERNEL_H
#define WINDOSE_KERNEL_H
#include <stddef.h>
#include <stdint.h>

// If a program crashes on its first kernel call, switch to ms_abi (must match the C# side's calling
// convention) and rebuild libc + programs.
#define KABI __attribute__((sysv_abi))
#define KAPI_VERSION 11
#define K_KILLED (-100)          // returned by every call after the program was killed; the wrappers exit(137)

typedef int handle_t;            // opaque; 0 = none/failed; never do arithmetic on it

typedef struct Event { int type, handle, a, b; } Event;
enum {
    EV_NONE = 0, EV_CLICK = 1, EV_CLOSE = 2, EV_KEY = 3,
    EV_MOUSEMOVE = 4, EV_MOUSEDOWN = 5, EV_MOUSEUP = 6,
    EV_FOCUS = 7, EV_DROP = 8, EV_DBLCLICK = 9
};

// Matches ElfFileStat on the C# side. stat_file fills it: is_dir 0/1, 64-bit size.
typedef struct FileStat { int is_dir, size_lo, size_hi; } FileStat;

// Matches DriveStat on the C# side.
typedef struct DriveStat { int total_lo, total_hi, free_lo, free_hi; } DriveStat;
// EV_KEY: a = key code, b = (modifiers << 16) | char.
// EV_MOUSEMOVE: a = x, b = y (component-local). EV_MOUSEDOWN/UP: a = x | (button << 28), b = y
// (button: 1 left, 2 right, 3 middle). handle = innermost program-owned component under the cursor.

typedef struct KernelApi {
    int version, size, pid, reserved;
    void     (KABI *write)(int pid, const char *text, int len);
    handle_t (KABI *create_window)(int pid, int w, int h, const char *title);
    int      (KABI *close)(int pid, handle_t h);
    int      (KABI *sleep)(int pid, int ms);
    handle_t (KABI *create_button)(int pid, handle_t parent, int x, int y, int w, int h, const char *text);
    handle_t (KABI *create_label)(int pid, handle_t parent, int x, int y, const char *text);
    int      (KABI *set_text)(int pid, handle_t obj, const char *text);
    int      (KABI *fill_rect)(int pid, handle_t win, int x, int y, int w, int h, unsigned argb);
    int      (KABI *poll_event)(int pid, Event *out);
    int      (KABI *read_file)(int pid, const char *path, char *buf, int max);
    int      (KABI *write_file)(int pid, const char *path, const char *buf, int len);
    void *   (KABI *alloc)(int pid, int size);
    int      (KABI *get_args)(int pid, char *buf, int max);
    int      (KABI *ticks)(int pid);
    int      (KABI *yield)(int pid);
    int      (KABI *spawn)(int pid, const char *path, const char *argbuf, int arglen);
    int      (KABI *kill)(int pid, int target);
    handle_t (KABI *create_canvas)(int pid, handle_t parent, int x, int y, int w, int h);
    int      (KABI *draw_pixels)(int pid, handle_t canvas, int x, int y, int w, int h, const void *buf, int len);
    int      (KABI *resize_window)(int pid, handle_t win, int w, int h);
    int      (KABI *list_dir)(int pid, const char *path, char *buf, int max);
    int      (KABI *stat_file)(int pid, const char *path, FileStat *out);
    int      (KABI *make_dir)(int pid, const char *path);
    int      (KABI *delete_path)(int pid, const char *path);
    int      (KABI *rename_path)(int pid, const char *oldpath, const char *newpath);
    handle_t (KABI *create_checkbox)(int pid, handle_t parent, int x, int y, const char *text);
    int      (KABI *set_checked)(int pid, handle_t obj, int checked);
    int      (KABI *get_checked)(int pid, handle_t obj);
    handle_t (KABI *create_radio)(int pid, handle_t parent, int x, int y, const char *text, const char *group);
    handle_t (KABI *create_slider)(int pid, handle_t parent, int x, int y, int w, int horiz, int min, int max, int value);
    int      (KABI *slider_set)(int pid, handle_t obj, int value);
    int      (KABI *slider_get)(int pid, handle_t obj);
    handle_t (KABI *create_progress)(int pid, handle_t parent, int x, int y, int w, int h);
    int      (KABI *progress_setup)(int pid, handle_t obj, int min, int max);
    int      (KABI *progress_set)(int pid, handle_t obj, int value);
    handle_t (KABI *create_textfield)(int pid, handle_t parent, int x, int y, int w);
    int      (KABI *textfield_setup)(int pid, handle_t obj, int maxlen, int password, int readonly_);
    int      (KABI *get_text)(int pid, handle_t obj, char *buf, int max);
    handle_t (KABI *create_combo)(int pid, handle_t parent, int x, int y, int w);
    int      (KABI *combo_add_item)(int pid, handle_t obj, const char *text);
    int      (KABI *combo_remove_item)(int pid, handle_t obj, int index);
    int      (KABI *combo_clear)(int pid, handle_t obj);
    int      (KABI *combo_get_selected)(int pid, handle_t obj);
    int      (KABI *combo_set_selected)(int pid, handle_t obj, int index);
    handle_t (KABI *create_listbox)(int pid, handle_t parent, int x, int y, int w, int h);
    int      (KABI *list_add_item)(int pid, handle_t obj, const char *text);
    int      (KABI *list_remove_item)(int pid, handle_t obj, int index);
    int      (KABI *list_clear)(int pid, handle_t obj);
    int      (KABI *list_get_selected)(int pid, handle_t obj);
    int      (KABI *list_select)(int pid, handle_t obj, int index);
    int      (KABI *list_add_row)(int pid, handle_t obj, const char *buf, int len);
    handle_t (KABI *create_tabs)(int pid, handle_t parent, int x, int y, int w, int h);
    handle_t (KABI *tab_add_page)(int pid, handle_t obj, const char *title);
    int      (KABI *tab_select)(int pid, handle_t obj, int index);
    int      (KABI *tab_get_selected)(int pid, handle_t obj);
    handle_t (KABI *menubar_create)(int pid, handle_t window);
    handle_t (KABI *menu_add_item)(int pid, handle_t obj, const char *page, const char *text);
    handle_t (KABI *msgbox)(int pid, const char *title, const char *text, const char *buttons, int len);
    handle_t (KABI *statusbar_create)(int pid, handle_t parent, int x, int y, int w);
    int      (KABI *status_add_panel)(int pid, handle_t obj, const char *text, int width);
    int      (KABI *status_set_text)(int pid, handle_t obj, int index, const char *text);
    int      (KABI *widget_set_enabled)(int pid, handle_t obj, int enabled);
    int      (KABI *widget_get_enabled)(int pid, handle_t obj);
    int      (KABI *widget_focus)(int pid, handle_t obj);
    int      (KABI *textfield_select)(int pid, handle_t obj, int start, int len);
    int      (KABI *textfield_get_caret)(int pid, handle_t obj);
    int      (KABI *textfield_set_multiline)(int pid, handle_t obj, int multiline);
    handle_t (KABI *colorpicker_create)(int pid, handle_t parent, int x, int y);
    int      (KABI *colorpicker_get)(int pid, handle_t obj);
    int      (KABI *colorpicker_set)(int pid, handle_t obj, int argb);
    handle_t (KABI *numeric_create)(int pid, handle_t parent, int x, int y, int w, int min, int max, int inc, int value);
    int      (KABI *numeric_set)(int pid, handle_t obj, int value);
    int      (KABI *numeric_get)(int pid, handle_t obj);
    handle_t (KABI *calendar_create)(int pid, handle_t parent, int x, int y);
    int      (KABI *calendar_get)(int pid, handle_t obj);
    int      (KABI *calendar_set)(int pid, handle_t obj, int y, int m, int d);
    handle_t (KABI *filedialog_open)(int pid, const char *title, const char *initial, const char *filter, int save);
    handle_t (KABI *grid_create)(int pid, handle_t parent, int x, int y, int w, int h);
    int      (KABI *grid_add_column)(int pid, handle_t obj, const char *title, int width);
    int      (KABI *grid_add_row)(int pid, handle_t obj, const char *buf, int len);
    int      (KABI *grid_clear)(int pid, handle_t obj);
    int      (KABI *grid_get_selected)(int pid, handle_t obj);
    int      (KABI *grid_select)(int pid, handle_t obj, int row);
    int      (KABI *grid_set_cell)(int pid, handle_t obj, int row, int col, const char *text);
    int      (KABI *grid_get_cell)(int pid, handle_t obj, int row, int col, char *buf, int max);
    int      (KABI *grid_sort)(int pid, handle_t obj, int col, int descending);
    int      (KABI *drop_accept)(int pid, handle_t win, int enabled);
    int      (KABI *dnd_get_text)(int pid, char *buf, int max);
    handle_t (KABI *tree_create)(int pid, handle_t parent, int x, int y, int w, int h);
    handle_t (KABI *tree_add_root)(int pid, handle_t obj, const char *text, const char *icon);
    handle_t (KABI *tree_add_child)(int pid, handle_t obj, handle_t item, const char *text, const char *icon);
    int      (KABI *tree_clear)(int pid, handle_t obj);
    handle_t (KABI *tree_selected_item)(int pid, handle_t obj);
    int      (KABI *tree_set_expanded)(int pid, handle_t obj, handle_t item, int expanded);
    handle_t (KABI *toolbar_create)(int pid, handle_t parent, int x, int y, int w);
    handle_t (KABI *toolbar_add_button)(int pid, handle_t obj, const char *text);
    int      (KABI *toolbar_add_separator)(int pid, handle_t obj);
    handle_t (KABI *popup_create)(int pid);
    handle_t (KABI *popup_add_item)(int pid, handle_t obj, const char *text);
    int      (KABI *popup_show_at)(int pid, handle_t obj, int x, int y);
    int      (KABI *list_add_column)(int pid, handle_t obj, const char *title, int width);
    int      (KABI *widget_screen_pos)(int pid, handle_t obj);
    int      (KABI *list_hit_test)(int pid, handle_t obj, int x, int y);
    int      (KABI *list_drives)(int pid, char *buf, int max);
    int      (KABI *drive_stat)(int pid, const char *mount, DriveStat *out);
    int      (KABI *launch_app)(int pid, const char *name, const char *arg);
    int      (KABI *open_file)(int pid, const char *path);
    int      (KABI *notify_send)(int pid, const char *title, const char *text);
    int      (KABI *desktop_count)(int pid);
    int      (KABI *desktop_current)(int pid);
    int      (KABI *desktop_switch)(int pid, int index);
    int      (KABI *window_set_desktop)(int pid, handle_t win, int index);
    int      (KABI *trash_delete)(int pid, const char *path);
    int      (KABI *trash_list)(int pid, char *buf, int max);
    int      (KABI *trash_restore)(int pid, const char *name);
    int      (KABI *trash_empty)(int pid);
    int      (KABI *wait_pid)(int pid, int target, int *exit_code, int timeout_ms);
    int      (KABI *clipboard_get)(int pid, char *buf, int max);
    int      (KABI *clipboard_set)(int pid, const char *text, int len);
    int      (KABI *get_env)(int pid, const char *name, char *buf, int max);
    int      (KABI *set_env)(int pid, const char *name, const char *val);
} KernelApi;

extern KernelApi *__k;
void exit(int code) __attribute__((noreturn));

static inline int __kchk(int r) { if (r == K_KILLED) exit(137); return r; }

static inline void     k_write(const char *s, int len)              { __k->write(__k->pid, s, len); }
static inline handle_t k_create_window(int w, int h, const char *title) { return __kchk(__k->create_window(__k->pid, w, h, title)); }
static inline int      k_close(handle_t h)                          { return __kchk(__k->close(__k->pid, h)); }
static inline int      k_sleep(int ms)                              { return __kchk(__k->sleep(__k->pid, ms)); }
static inline handle_t k_create_button(handle_t parent, int x, int y, int w, int h, const char *text) { return __kchk(__k->create_button(__k->pid, parent, x, y, w, h, text)); }
static inline handle_t k_create_label(handle_t parent, int x, int y, const char *text) { return __kchk(__k->create_label(__k->pid, parent, x, y, text)); }
static inline int      k_set_text(handle_t obj, const char *text)  { return __kchk(__k->set_text(__k->pid, obj, text)); }
static inline int      k_fill_rect(handle_t win, int x, int y, int w, int h, unsigned argb) { return __kchk(__k->fill_rect(__k->pid, win, x, y, w, h, argb)); }
static inline int      k_poll_event(Event *e)                       { return __kchk(__k->poll_event(__k->pid, e)); }
static inline int      k_read_file(const char *path, char *buf, int max) { return __kchk(__k->read_file(__k->pid, path, buf, max)); }
static inline int      k_write_file(const char *path, const char *buf, int len) { return __kchk(__k->write_file(__k->pid, path, buf, len)); }
static inline int      k_ticks(void)                                { return __kchk(__k->ticks(__k->pid)); }
static inline int      k_yield(void)                               { return __kchk(__k->yield(__k->pid)); }
static inline int      k_spawn(const char *path, const char *argbuf, int arglen) { return __kchk(__k->spawn(__k->pid, path, argbuf, arglen)); }
static inline int      k_kill(int target)                         { return __kchk(__k->kill(__k->pid, target)); }
static inline handle_t k_create_canvas(handle_t parent, int x, int y, int w, int h) { return __kchk(__k->create_canvas(__k->pid, parent, x, y, w, h)); }
static inline int      k_draw_pixels(handle_t canvas, int x, int y, int w, int h, const void *buf, int len) { return __kchk(__k->draw_pixels(__k->pid, canvas, x, y, w, h, buf, len)); }
static inline int      k_resize_window(handle_t win, int w, int h) { return __kchk(__k->resize_window(__k->pid, win, w, h)); }
static inline int      k_list_dir(const char *path, char *buf, int max) { return __kchk(__k->list_dir(__k->pid, path, buf, max)); }
static inline int      k_stat_file(const char *path, FileStat *out) { return __kchk(__k->stat_file(__k->pid, path, out)); }
static inline int      k_make_dir(const char *path)               { return __kchk(__k->make_dir(__k->pid, path)); }
static inline int      k_delete_path(const char *path)            { return __kchk(__k->delete_path(__k->pid, path)); }
static inline int      k_rename_path(const char *oldp, const char *newp) { return __kchk(__k->rename_path(__k->pid, oldp, newp)); }
static inline handle_t k_create_checkbox(handle_t parent, int x, int y, const char *text) { return __kchk(__k->create_checkbox(__k->pid, parent, x, y, text)); }
static inline int      k_set_checked(handle_t obj, int v) { return __kchk(__k->set_checked(__k->pid, obj, v)); }
static inline int      k_get_checked(handle_t obj)        { return __kchk(__k->get_checked(__k->pid, obj)); }
static inline handle_t k_create_radio(handle_t parent, int x, int y, const char *text, const char *group) { return __kchk(__k->create_radio(__k->pid, parent, x, y, text, group)); }
static inline handle_t k_create_slider(handle_t parent, int x, int y, int w, int horiz, int min, int max, int value) { return __kchk(__k->create_slider(__k->pid, parent, x, y, w, horiz, min, max, value)); }
static inline int      k_slider_set(handle_t obj, int v) { return __kchk(__k->slider_set(__k->pid, obj, v)); }
static inline int      k_slider_get(handle_t obj)        { return __kchk(__k->slider_get(__k->pid, obj)); }
static inline handle_t k_create_progress(handle_t parent, int x, int y, int w, int h) { return __kchk(__k->create_progress(__k->pid, parent, x, y, w, h)); }
static inline int      k_progress_setup(handle_t obj, int min, int max) { return __kchk(__k->progress_setup(__k->pid, obj, min, max)); }
static inline int      k_progress_set(handle_t obj, int v) { return __kchk(__k->progress_set(__k->pid, obj, v)); }
static inline handle_t k_create_textfield(handle_t parent, int x, int y, int w) { return __kchk(__k->create_textfield(__k->pid, parent, x, y, w)); }
static inline int      k_textfield_setup(handle_t obj, int maxlen, int password, int ro) { return __kchk(__k->textfield_setup(__k->pid, obj, maxlen, password, ro)); }
static inline int      k_get_text(handle_t obj, char *buf, int max) { return __kchk(__k->get_text(__k->pid, obj, buf, max)); }
static inline handle_t k_create_combo(handle_t parent, int x, int y, int w) { return __kchk(__k->create_combo(__k->pid, parent, x, y, w)); }
static inline int      k_combo_add_item(handle_t obj, const char *text) { return __kchk(__k->combo_add_item(__k->pid, obj, text)); }
static inline int      k_combo_remove_item(handle_t obj, int i) { return __kchk(__k->combo_remove_item(__k->pid, obj, i)); }
static inline int      k_combo_clear(handle_t obj)        { return __kchk(__k->combo_clear(__k->pid, obj)); }
static inline int      k_combo_get_selected(handle_t obj) { return __kchk(__k->combo_get_selected(__k->pid, obj)); }
static inline int      k_combo_set_selected(handle_t obj, int i) { return __kchk(__k->combo_set_selected(__k->pid, obj, i)); }
static inline handle_t k_create_listbox(handle_t parent, int x, int y, int w, int h) { return __kchk(__k->create_listbox(__k->pid, parent, x, y, w, h)); }
static inline int      k_list_add_item(handle_t obj, const char *text) { return __kchk(__k->list_add_item(__k->pid, obj, text)); }
static inline int      k_list_remove_item(handle_t obj, int i) { return __kchk(__k->list_remove_item(__k->pid, obj, i)); }
static inline int      k_list_clear(handle_t obj)         { return __kchk(__k->list_clear(__k->pid, obj)); }
static inline int      k_list_get_selected(handle_t obj)  { return __kchk(__k->list_get_selected(__k->pid, obj)); }
static inline int      k_list_select(handle_t obj, int i) { return __kchk(__k->list_select(__k->pid, obj, i)); }
static inline handle_t k_create_tabs(handle_t parent, int x, int y, int w, int h) { return __kchk(__k->create_tabs(__k->pid, parent, x, y, w, h)); }
static inline handle_t k_tab_add_page(handle_t obj, const char *title) { return __kchk(__k->tab_add_page(__k->pid, obj, title)); }
static inline int      k_tab_select(handle_t obj, int i)  { return __kchk(__k->tab_select(__k->pid, obj, i)); }
static inline int      k_tab_get_selected(handle_t obj)   { return __kchk(__k->tab_get_selected(__k->pid, obj)); }
static inline handle_t k_menubar_create(handle_t win)     { return __kchk(__k->menubar_create(__k->pid, win)); }
static inline handle_t k_menu_add_item(handle_t obj, const char *page, const char *text) { return __kchk(__k->menu_add_item(__k->pid, obj, page, text)); }
static inline handle_t k_msgbox(const char *title, const char *text, const char *buttons, int len) { return __kchk(__k->msgbox(__k->pid, title, text, buttons, len)); }
static inline handle_t k_statusbar_create(handle_t parent, int x, int y, int w) { return __kchk(__k->statusbar_create(__k->pid, parent, x, y, w)); }
static inline int      k_status_add_panel(handle_t obj, const char *text, int w) { return __kchk(__k->status_add_panel(__k->pid, obj, text, w)); }
static inline int      k_status_set_text(handle_t obj, int i, const char *text) { return __kchk(__k->status_set_text(__k->pid, obj, i, text)); }
static inline int      k_widget_set_enabled(handle_t obj, int v) { return __kchk(__k->widget_set_enabled(__k->pid, obj, v)); }
static inline int      k_widget_get_enabled(handle_t obj) { return __kchk(__k->widget_get_enabled(__k->pid, obj)); }
static inline int      k_widget_focus(handle_t obj)      { return __kchk(__k->widget_focus(__k->pid, obj)); }
static inline int      k_textfield_select(handle_t obj, int s, int n) { return __kchk(__k->textfield_select(__k->pid, obj, s, n)); }
static inline int      k_textfield_get_caret(handle_t obj) { return __kchk(__k->textfield_get_caret(__k->pid, obj)); }
static inline int      k_textfield_set_multiline(handle_t obj, int v) { return __kchk(__k->textfield_set_multiline(__k->pid, obj, v)); }
static inline handle_t k_colorpicker_create(handle_t parent, int x, int y) { return __kchk(__k->colorpicker_create(__k->pid, parent, x, y)); }
static inline int      k_colorpicker_get(handle_t obj)   { return __kchk(__k->colorpicker_get(__k->pid, obj)); }
static inline int      k_colorpicker_set(handle_t obj, int argb) { return __kchk(__k->colorpicker_set(__k->pid, obj, argb)); }
static inline handle_t k_numeric_create(handle_t parent, int x, int y, int w, int min, int max, int inc, int value) { return __kchk(__k->numeric_create(__k->pid, parent, x, y, w, min, max, inc, value)); }
static inline int      k_numeric_set(handle_t obj, int v) { return __kchk(__k->numeric_set(__k->pid, obj, v)); }
static inline int      k_numeric_get(handle_t obj)        { return __kchk(__k->numeric_get(__k->pid, obj)); }
static inline handle_t k_calendar_create(handle_t parent, int x, int y) { return __kchk(__k->calendar_create(__k->pid, parent, x, y)); }
static inline int      k_calendar_get(handle_t obj)      { return __kchk(__k->calendar_get(__k->pid, obj)); }
static inline int      k_calendar_set(handle_t obj, int y, int m, int d) { return __kchk(__k->calendar_set(__k->pid, obj, y, m, d)); }
static inline handle_t k_filedialog_open(const char *title, const char *initial, const char *filter, int save) { return __kchk(__k->filedialog_open(__k->pid, title, initial, filter, save)); }
static inline handle_t k_grid_create(handle_t parent, int x, int y, int w, int h) { return __kchk(__k->grid_create(__k->pid, parent, x, y, w, h)); }
static inline int      k_grid_add_column(handle_t obj, const char *title, int w) { return __kchk(__k->grid_add_column(__k->pid, obj, title, w)); }
static inline int      k_grid_add_row(handle_t obj, const char *buf, int len) { return __kchk(__k->grid_add_row(__k->pid, obj, buf, len)); }
static inline int      k_grid_clear(handle_t obj)        { return __kchk(__k->grid_clear(__k->pid, obj)); }
static inline int      k_grid_get_selected(handle_t obj) { return __kchk(__k->grid_get_selected(__k->pid, obj)); }
static inline int      k_grid_select(handle_t obj, int r) { return __kchk(__k->grid_select(__k->pid, obj, r)); }
static inline int      k_grid_set_cell(handle_t obj, int r, int c, const char *t) { return __kchk(__k->grid_set_cell(__k->pid, obj, r, c, t)); }
static inline int      k_grid_get_cell(handle_t obj, int r, int c, char *buf, int max) { return __kchk(__k->grid_get_cell(__k->pid, obj, r, c, buf, max)); }
static inline int      k_grid_sort(handle_t obj, int c, int desc) { return __kchk(__k->grid_sort(__k->pid, obj, c, desc)); }
static inline int      k_drop_accept(handle_t win, int v) { return __kchk(__k->drop_accept(__k->pid, win, v)); }
static inline int      k_dnd_get_text(char *buf, int max) { return __kchk(__k->dnd_get_text(__k->pid, buf, max)); }
static inline handle_t k_tree_create(handle_t parent, int x, int y, int w, int h) { return __kchk(__k->tree_create(__k->pid, parent, x, y, w, h)); }
static inline handle_t k_tree_add_root(handle_t obj, const char *text, const char *icon) { return __kchk(__k->tree_add_root(__k->pid, obj, text, icon)); }
static inline handle_t k_tree_add_child(handle_t obj, handle_t item, const char *text, const char *icon) { return __kchk(__k->tree_add_child(__k->pid, obj, item, text, icon)); }
static inline int      k_tree_clear(handle_t obj)         { return __kchk(__k->tree_clear(__k->pid, obj)); }
static inline handle_t k_tree_selected_item(handle_t obj) { return __kchk(__k->tree_selected_item(__k->pid, obj)); }
static inline int      k_tree_set_expanded(handle_t obj, handle_t item, int v) { return __kchk(__k->tree_set_expanded(__k->pid, obj, item, v)); }
static inline handle_t k_toolbar_create(handle_t parent, int x, int y, int w) { return __kchk(__k->toolbar_create(__k->pid, parent, x, y, w)); }
static inline handle_t k_toolbar_add_button(handle_t obj, const char *text) { return __kchk(__k->toolbar_add_button(__k->pid, obj, text)); }
static inline int      k_toolbar_add_separator(handle_t obj) { return __kchk(__k->toolbar_add_separator(__k->pid, obj)); }
static inline handle_t k_popup_create(void)               { return __kchk(__k->popup_create(__k->pid)); }
static inline handle_t k_popup_add_item(handle_t obj, const char *text) { return __kchk(__k->popup_add_item(__k->pid, obj, text)); }
static inline int      k_popup_show_at(handle_t obj, int x, int y) { return __kchk(__k->popup_show_at(__k->pid, obj, x, y)); }
static inline int      k_list_add_column(handle_t obj, const char *title, int w) { return __kchk(__k->list_add_column(__k->pid, obj, title, w)); }
static inline int      k_list_add_row(handle_t obj, const char *buf, int len) { return __kchk(__k->list_add_row(__k->pid, obj, buf, len)); }
static inline int      k_widget_screen_pos(handle_t obj) { return __kchk(__k->widget_screen_pos(__k->pid, obj)); }
static inline int      k_list_hit_test(handle_t obj, int x, int y) { return __kchk(__k->list_hit_test(__k->pid, obj, x, y)); }
static inline int      k_list_drives(char *buf, int max) { return __kchk(__k->list_drives(__k->pid, buf, max)); }
static inline int      k_drive_stat(const char *mount, DriveStat *out) { return __kchk(__k->drive_stat(__k->pid, mount, out)); }
static inline int      k_launch_app(const char *name, const char *arg) { return __kchk(__k->launch_app(__k->pid, name, arg)); }
static inline int      k_open_file(const char *path) { return __kchk(__k->open_file(__k->pid, path)); }
static inline int      k_notify_send(const char *title, const char *text) { return __kchk(__k->notify_send(__k->pid, title, text)); }
static inline int      k_desktop_count(void) { return __kchk(__k->desktop_count(__k->pid)); }
static inline int      k_desktop_current(void) { return __kchk(__k->desktop_current(__k->pid)); }
static inline int      k_desktop_switch(int i) { return __kchk(__k->desktop_switch(__k->pid, i)); }
static inline int      k_window_set_desktop(handle_t win, int i) { return __kchk(__k->window_set_desktop(__k->pid, win, i)); }
static inline int      k_trash_delete(const char *path) { return __kchk(__k->trash_delete(__k->pid, path)); }
static inline int      k_trash_list(char *buf, int max) { return __kchk(__k->trash_list(__k->pid, buf, max)); }
static inline int      k_trash_restore(const char *name) { return __kchk(__k->trash_restore(__k->pid, name)); }
static inline int      k_trash_empty(void) { return __kchk(__k->trash_empty(__k->pid)); }
static inline int      k_wait_pid(int target, int *exit_code, int timeout_ms) { return __kchk(__k->wait_pid(__k->pid, target, exit_code, timeout_ms)); }
static inline int      k_clipboard_get(char *buf, int max) { return __kchk(__k->clipboard_get(__k->pid, buf, max)); }
static inline int      k_clipboard_set(const char *text, int len) { return __kchk(__k->clipboard_set(__k->pid, text, len)); }
static inline int      k_get_env(const char *name, char *buf, int max) { return __kchk(__k->get_env(__k->pid, name, buf, max)); }
static inline int      k_set_env(const char *name, const char *val) { return __kchk(__k->set_env(__k->pid, name, val)); }
static inline int      k_pid(void)                                  { return __k->pid; }

#endif
