// windose/core.h - Core Windose Kernel API types, function table, and ABI definitions.
#ifndef WINDOSE_CORE_H
#define WINDOSE_CORE_H

#include <stddef.h>
#include <stdint.h>

// Calling convention for host <-> guest boundary
#define KABI __attribute__((sysv_abi))
#define KAPI_VERSION 15
#define K_KILLED (-100)

typedef int handle_t; // opaque kernel handle; 0 = invalid/none

// Control anchor flags
enum {
    ANCHOR_NONE   = 0,
    ANCHOR_LEFT   = 1 << 0,
    ANCHOR_TOP    = 1 << 1,
    ANCHOR_RIGHT  = 1 << 2,
    ANCHOR_BOTTOM = 1 << 3
};

// System metrics (matches GetSystemMetrics)
enum {
    SM_CXSCREEN     = 0,
    SM_CYSCREEN     = 1,
    SM_CYCAPTION    = 2,
    SM_CXBORDER     = 3,
    SM_CYTASKBAR    = 4,
    SM_WORKWIDTH    = 5,
    SM_WORKHEIGHT   = 6
};

// Window states
enum {
    WINDOW_STATE_NORMAL        = 0,
    WINDOW_STATE_MINIMIZED     = 1,
    WINDOW_STATE_MAXIMIZED     = 2,
    WINDOW_STATE_SNAPPED_LEFT  = 3,
    WINDOW_STATE_SNAPPED_RIGHT = 4
};

// Standard cursors
enum {
    CURSOR_ARROW    = 0,
    CURSOR_HAND     = 1,
    CURSOR_IBEAM    = 2,
    CURSOR_SIZENS   = 3,
    CURSOR_SIZEWE   = 4,
    CURSOR_SIZENWSE = 5,
    CURSOR_SIZENESW = 6,
    CURSOR_SIZEALL  = 7,
    CURSOR_WAIT     = 8
};

// Window creation flags
enum {
    WIN_FLAG_NONE        = 0,
    WIN_FLAG_NO_TITLEBAR = 1 << 0,
    WIN_FLAG_NO_TASKBAR  = 1 << 1,
    WIN_FLAG_POPUP       = 1 << 2,
    WIN_FLAG_BORDERLESS  = 1 << 3,
    WIN_FLAG_NO_RESIZE   = 1 << 7
};

// Event types
typedef struct Event { int type, handle, a, b; } Event;
enum {
    EV_NONE = 0, EV_CLICK = 1, EV_CLOSE = 2, EV_KEY = 3,
    EV_MOUSEMOVE = 4, EV_MOUSEDOWN = 5, EV_MOUSEUP = 6,
    EV_FOCUS = 7, EV_DROP = 8, EV_DBLCLICK = 9,
    EV_RESIZE = 10
};

// File & Drive statistics
typedef struct FileStat { int is_dir, size_lo, size_hi; } FileStat;
typedef struct DriveStat { int total_lo, total_hi, free_lo, free_hi; } DriveStat;

// Kernel API dispatch table. MUST match KernelApi in System/ELF/ElfRunner.cs.
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
    handle_t (KABI *create_terminal)(int pid, handle_t parent, int x, int y, int w, int h);
    int      (KABI *set_icon)(int pid, handle_t win, const char *icon_path);
    handle_t (KABI *create_window_ex)(int pid, int x, int y, int w, int h, const char *title, int flags);
    int      (KABI *move_window)(int pid, handle_t win, int x, int y);
    int      (KABI *screen_size)(int pid, int *out_w, int *out_h);
    int      (KABI *restart_proc)(int pid, int target_pid);
    int      (KABI *restart_shell)(int pid);
    int      (KABI *play_sound)(int pid, const char *name);
    int      (KABI *play_wave)(int pid, const void *wave_buf, int len);
    int      (KABI *set_cursor)(int pid, int cursor_type);
    int      (KABI *window_minimize)(int pid, handle_t win);
    int      (KABI *window_maximize)(int pid, handle_t win);
    int      (KABI *window_restore)(int pid, handle_t win);
    int      (KABI *window_get_state)(int pid, handle_t win);
    int      (KABI *shell_run_dialog)(int pid);
    int      (KABI *shell_about_dialog)(int pid);
    int      (KABI *get_system_metrics)(int pid, int metric);
    int      (KABI *ps_list)(int pid, char *buf, int max);
    int      (KABI *ps_kill)(int pid, int target_pid);
    int      (KABI *reg_list)(int pid, const char *prefix, char *buf, int max);
    int      (KABI *reg_get)(int pid, const char *key, char *val_buf, int max);
    int      (KABI *reg_set)(int pid, const char *key, const char *val);
    int      (KABI *widget_set_anchors)(int pid, handle_t obj, int anchor_flags);
    int      (KABI *widget_set_bounds)(int pid, handle_t obj, int x, int y, int w, int h);
    int      (KABI *list_set_item_icon)(int pid, handle_t obj, int item, const char *icon_path);
    int      (KABI *sys_mem)(int pid, uint64_t *out_total, uint64_t *out_used, uint64_t *out_free);
    int      (KABI *reg_delete)(int pid, const char *key);
} KernelApi;

extern KernelApi *__k;
void exit(int code) __attribute__((noreturn));

static inline int __kchk(int r) { if (r == K_KILLED) exit(137); return r; }

#endif // WINDOSE_CORE_H
