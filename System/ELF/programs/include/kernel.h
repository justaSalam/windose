// kernel.h - Windose Kernel API legacy compatibility header.
// Programs can still include this header to use the traditional k_* wrappers.
// For modern, modular APIs, include <windose.h> or specific subsystem headers:
//   <windose/gui.h>   Windowing, widgets, drawing, events
//   <windose/fs.h>    Files, directories, drives, trash
//   <windose/sys.h>   Process control, timing, clipboard, desktop, app launching
//   <windose/core.h>  Kernel table, ABI, and core types
#ifndef WINDOSE_KERNEL_H
#define WINDOSE_KERNEL_H

#include "windose.h"

// ---- Legacy k_* wrappers mapped to modular Windose APIs ----
static inline void     k_write(const char *s, int len)              { sys_write(s, len); }
static inline handle_t k_create_window(int w, int h, const char *title) { return win_create(w, h, title); }
static inline int      k_close(handle_t h)                          { return win_close(h); }
static inline int      k_sleep(int ms)                              { return sys_sleep(ms); }
static inline handle_t k_create_button(handle_t parent, int x, int y, int w, int h, const char *text) { return button_create(parent, x, y, w, h, text); }
static inline handle_t k_create_label(handle_t parent, int x, int y, const char *text) { return label_create(parent, x, y, text); }
static inline int      k_set_text(handle_t obj, const char *text)  { return text_set(obj, text); }
static inline int      k_fill_rect(handle_t win, int x, int y, int w, int h, unsigned argb) { return draw_rect(win, x, y, w, h, argb); }
static inline int      k_poll_event(Event *e)                       { return event_poll(e); }
static inline int      k_read_file(const char *path, char *buf, int max) { return file_read_raw(path, buf, max); }
static inline int      k_write_file(const char *path, const char *buf, int len) { return file_write_raw(path, buf, len); }
static inline int      k_ticks(void)                                { return sys_ticks(); }
static inline int      k_yield(void)                               { return sys_yield(); }
static inline int      k_spawn(const char *path, const char *argbuf, int arglen) { return proc_spawn(path, argbuf, arglen); }
static inline int      k_kill(int target)                         { return proc_kill(target); }
static inline int      k_set_icon(handle_t win, const char *icon_path) { return win_set_icon(win, icon_path); }
static inline handle_t k_create_window_with_icon(int w, int h, const char *title, const char *icon_path) { return win_create_with_icon(w, h, title, icon_path); }
static inline handle_t k_create_canvas(handle_t parent, int x, int y, int w, int h) { return canvas_create(parent, x, y, w, h); }
static inline int      k_draw_pixels(handle_t canvas, int x, int y, int w, int h, const void *buf, int len) { return canvas_draw_pixels(canvas, x, y, w, h, buf, len); }
static inline int      k_resize_window(handle_t win, int w, int h) { return win_resize(win, w, h); }
static inline int      k_list_dir(const char *path, char *buf, int max) { return dir_list(path, buf, max); }
static inline int      k_stat_file(const char *path, FileStat *out) { return file_stat(path, out); }
static inline int      k_make_dir(const char *path)               { return dir_make(path); }
static inline int      k_delete_path(const char *path)            { return path_delete(path); }
static inline int      k_rename_path(const char *oldp, const char *newp) { return path_rename(oldp, newp); }
static inline handle_t k_create_checkbox(handle_t parent, int x, int y, const char *text) { return checkbox_create(parent, x, y, text); }
static inline int      k_set_checked(handle_t obj, int v) { return checkbox_set(obj, v); }
static inline int      k_get_checked(handle_t obj)        { return checkbox_get(obj); }
static inline handle_t k_create_radio(handle_t parent, int x, int y, const char *text, const char *group) { return radio_create(parent, x, y, text, group); }
static inline handle_t k_create_slider(handle_t parent, int x, int y, int w, int horiz, int min, int max, int value) { return slider_create(parent, x, y, w, horiz, min, max, value); }
static inline int      k_slider_set(handle_t obj, int v) { return slider_set(obj, v); }
static inline int      k_slider_get(handle_t obj)        { return slider_get(obj); }
static inline handle_t k_create_progress(handle_t parent, int x, int y, int w, int h) { return progress_create(parent, x, y, w, h); }
static inline int      k_progress_setup(handle_t obj, int min, int max) { return progress_setup(obj, min, max); }
static inline int      k_progress_set(handle_t obj, int v) { return progress_set(obj, v); }
static inline handle_t k_create_textfield(handle_t parent, int x, int y, int w) { return textfield_create(parent, x, y, w); }
static inline int      k_textfield_setup(handle_t obj, int maxlen, int password, int ro) { return textfield_setup(obj, maxlen, password, ro); }
static inline int      k_get_text(handle_t obj, char *buf, int max) { return text_get(obj, buf, max); }
static inline handle_t k_create_combo(handle_t parent, int x, int y, int w) { return combo_create(parent, x, y, w); }
static inline int      k_combo_add_item(handle_t obj, const char *text) { return combo_add_item(obj, text); }
static inline int      k_combo_remove_item(handle_t obj, int i) { return combo_remove_item(obj, i); }
static inline int      k_combo_clear(handle_t obj)        { return combo_clear(obj); }
static inline int      k_combo_get_selected(handle_t obj) { return combo_get_selected(obj); }
static inline int      k_combo_set_selected(handle_t obj, int i) { return combo_set_selected(obj, i); }
static inline handle_t k_create_listbox(handle_t parent, int x, int y, int w, int h) { return listbox_create(parent, x, y, w, h); }
static inline int      k_list_add_item(handle_t obj, const char *text) { return listbox_add_item(obj, text); }
static inline int      k_list_remove_item(handle_t obj, int i) { return listbox_remove_item(obj, i); }
static inline int      k_list_clear(handle_t obj)         { return listbox_clear(obj); }
static inline int      k_list_get_selected(handle_t obj)  { return listbox_get_selected(obj); }
static inline int      k_list_select(handle_t obj, int i) { return listbox_select(obj, i); }
static inline handle_t k_create_tabs(handle_t parent, int x, int y, int w, int h) { return tabs_create(parent, x, y, w, h); }
static inline handle_t k_tab_add_page(handle_t obj, const char *title) { return tabs_add_page(obj, title); }
static inline int      k_tab_select(handle_t obj, int i)  { return tabs_select(obj, i); }
static inline int      k_tab_get_selected(handle_t obj)   { return tabs_get_selected(obj); }
static inline handle_t k_menubar_create(handle_t win)     { return menubar_create(win); }
static inline handle_t k_menu_add_item(handle_t obj, const char *page, const char *text) { return menu_add_item(obj, page, text); }
static inline handle_t k_msgbox(const char *title, const char *text, const char *buttons, int len) { return msgbox_show(title, text, buttons, len); }
static inline handle_t k_statusbar_create(handle_t parent, int x, int y, int w) { return statusbar_create(parent, x, y, w); }
static inline int      k_status_add_panel(handle_t obj, const char *text, int w) { return status_add_panel(obj, text, w); }
static inline int      k_status_set_text(handle_t obj, int i, const char *text) { return status_set_text(obj, i, text); }
static inline int      k_widget_set_enabled(handle_t obj, int v) { return widget_set_enabled(obj, v); }
static inline int      k_widget_get_enabled(handle_t obj) { return widget_get_enabled(obj); }
static inline int      k_widget_focus(handle_t obj)      { return widget_focus(obj); }
static inline int      k_textfield_select(handle_t obj, int s, int n) { return textfield_select(obj, s, n); }
static inline int      k_textfield_get_caret(handle_t obj) { return textfield_get_caret(obj); }
static inline int      k_textfield_set_multiline(handle_t obj, int v) { return textfield_set_multiline(obj, v); }
static inline handle_t k_colorpicker_create(handle_t parent, int x, int y) { return colorpicker_create(parent, x, y); }
static inline int      k_colorpicker_get(handle_t obj)   { return colorpicker_get(obj); }
static inline int      k_colorpicker_set(handle_t obj, int argb) { return colorpicker_set(obj, argb); }
static inline handle_t k_numeric_create(handle_t parent, int x, int y, int w, int min, int max, int inc, int value) { return numeric_create(parent, x, y, w, min, max, inc, value); }
static inline int      k_numeric_set(handle_t obj, int v) { return numeric_set(obj, v); }
static inline int      k_numeric_get(handle_t obj)        { return numeric_get(obj); }
static inline handle_t k_calendar_create(handle_t parent, int x, int y) { return calendar_create(parent, x, y); }
static inline int      k_calendar_get(handle_t obj)      { return calendar_get(obj); }
static inline int      k_calendar_set(handle_t obj, int y, int m, int d) { return calendar_set(obj, y, m, d); }
static inline handle_t k_filedialog_open(const char *title, const char *initial, const char *filter, int save) { return filedialog_show(title, initial, filter, save); }
static inline handle_t k_grid_create(handle_t parent, int x, int y, int w, int h) { return grid_create(parent, x, y, w, h); }
static inline int      k_grid_add_column(handle_t obj, const char *title, int w) { return grid_add_column(obj, title, w); }
static inline int      k_grid_add_row(handle_t obj, const char *buf, int len) { return grid_add_row(obj, buf, len); }
static inline int      k_grid_clear(handle_t obj)        { return grid_clear(obj); }
static inline int      k_grid_get_selected(handle_t obj) { return grid_get_selected(obj); }
static inline int      k_grid_select(handle_t obj, int r) { return grid_select(obj, r); }
static inline int      k_grid_set_cell(handle_t obj, int r, int c, const char *t) { return grid_set_cell(obj, r, c, t); }
static inline int      k_grid_get_cell(handle_t obj, int r, int c, char *buf, int max) { return grid_get_cell(obj, r, c, buf, max); }
static inline int      k_grid_sort(handle_t obj, int c, int desc) { return grid_sort(obj, c, desc); }
static inline int      k_drop_accept(handle_t win, int v) { return win_accept_drop(win, v); }
static inline int      k_dnd_get_text(char *buf, int max) { return dnd_get_text_payload(buf, max); }
static inline handle_t k_tree_create(handle_t parent, int x, int y, int w, int h) { return tree_create(parent, x, y, w, h); }
static inline handle_t k_tree_add_root(handle_t obj, const char *text, const char *icon) { return tree_add_root(obj, text, icon); }
static inline handle_t k_tree_add_child(handle_t obj, handle_t item, const char *text, const char *icon) { return tree_add_child(obj, item, text, icon); }
static inline int      k_tree_clear(handle_t obj)         { return tree_clear(obj); }
static inline handle_t k_tree_selected_item(handle_t obj) { return tree_get_selected(obj); }
static inline int      k_tree_set_expanded(handle_t obj, handle_t item, int v) { return tree_set_expanded(obj, item, v); }
static inline handle_t k_toolbar_create(handle_t parent, int x, int y, int w) { return toolbar_create(parent, x, y, w); }
static inline handle_t k_toolbar_add_button(handle_t obj, const char *text) { return toolbar_add_button(obj, text); }
static inline int      k_toolbar_add_separator(handle_t obj) { return toolbar_add_separator(obj); }
static inline handle_t k_popup_create(void)               { return popup_create(); }
static inline handle_t k_popup_add_item(handle_t obj, const char *text) { return popup_add_item(obj, text); }
static inline int      k_popup_show_at(handle_t obj, int x, int y) { return popup_show_at(obj, x, y); }
static inline int      k_list_add_column(handle_t obj, const char *title, int w) { return listbox_add_column(obj, title, w); }
static inline int      k_list_add_row(handle_t obj, const char *buf, int len) { return listbox_add_row(obj, buf, len); }
static inline int      k_widget_screen_pos(handle_t obj) { return widget_screen_pos(obj); }
static inline int      k_list_hit_test(handle_t obj, int x, int y) { return listbox_hit_test(obj, x, y); }
static inline int      k_list_drives(char *buf, int max) { return drives_list(buf, max); }
static inline int      k_drive_stat(const char *mount, DriveStat *out) { return drive_get_stat(mount, out); }
static inline int      k_launch_app(const char *name, const char *arg) { return app_launch(name, arg); }
static inline int      k_open_file(const char *path) { return app_open_file(path); }
static inline int      k_notify_send(const char *title, const char *text) { return notify_post(title, text); }
static inline int      k_desktop_count(void) { return desktop_get_count(); }
static inline int      k_desktop_current(void) { return desktop_get_current(); }
static inline int      k_desktop_switch(int i) { return desktop_switch_to(i); }
static inline int      k_window_set_desktop(handle_t win, int i) { return win_set_desktop(win, i); }
static inline int      k_trash_delete(const char *path) { return trash_delete_file(path); }
static inline int      k_trash_list(char *buf, int max) { return trash_list_items(buf, max); }
static inline int      k_trash_restore(const char *name) { return trash_restore_item(name); }
static inline int      k_trash_empty(void) { return trash_empty_all(); }
static inline int      k_wait_pid(int target, int *exit_code, int timeout_ms) { return proc_wait(target, exit_code, timeout_ms); }
static inline int      k_clipboard_get(char *buf, int max) { return clipboard_get_text(buf, max); }
static inline int      k_clipboard_set(const char *text, int len) { return clipboard_set_text(text, len); }
static inline int      k_get_env(const char *name, char *buf, int max) { return env_get(name, buf, max); }
static inline int      k_set_env(const char *name, const char *val) { return env_set(name, val); }
static inline handle_t k_create_terminal(handle_t parent, int x, int y, int w, int h) { return terminal_create(parent, x, y, w, h); }
static inline int      k_pid(void)                                  { return proc_pid(); }
static inline handle_t k_create_window_ex(int x, int y, int w, int h, const char *title, int flags) { return win_create_ex(x, y, w, h, title, flags); }
static inline int      k_move_window(handle_t win, int x, int y) { return win_move(win, x, y); }
static inline int      k_screen_size(int *out_w, int *out_h) { return screen_get_size(out_w, out_h); }
static inline int      k_restart_proc(int target) { return proc_restart(target); }
static inline int      k_restart_shell(void) { return shell_restart(); }

#endif // WINDOSE_KERNEL_H
