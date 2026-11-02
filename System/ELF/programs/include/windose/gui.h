// windose/gui.h - Windose GUI, Windowing, and Widget API.
#ifndef WINDOSE_GUI_H
#define WINDOSE_GUI_H

#include "core.h"

// Window flags for win_create_ex:
#define WIN_FLAG_NO_TITLEBAR    1
#define WIN_FLAG_NO_TASKBAR     2
#define WIN_FLAG_POPUP          4
#define WIN_FLAG_BORDERLESS     8
#define WIN_FLAG_CLOSE_TO_TRAY  16
#define WIN_FLAG_OPEN_TO_TRAY   32
#define WIN_FLAG_MIN_TO_TRAY    64

// ---- Windows ----
static inline handle_t win_create(int w, int h, const char *title) {
    return __kchk(__k->create_window(__k->pid, w, h, title));
}
static inline handle_t win_create_ex(int x, int y, int w, int h, const char *title, int flags) {
    return __kchk(__k->create_window_ex(__k->pid, x, y, w, h, title, flags));
}
static inline int win_close(handle_t win) {
    return __kchk(__k->close(__k->pid, win));
}
static inline int win_resize(handle_t win, int w, int h) {
    return __kchk(__k->resize_window(__k->pid, win, w, h));
}
static inline int win_move(handle_t win, int x, int y) {
    return __kchk(__k->move_window(__k->pid, win, x, y));
}
static inline int win_set_icon(handle_t win, const char *icon_path) {
    return __kchk(__k->set_icon(__k->pid, win, icon_path));
}
static inline handle_t win_create_with_icon(int w, int h, const char *title, const char *icon_path) {
    handle_t win = win_create(w, h, title);
    if (win && icon_path) win_set_icon(win, icon_path);
    return win;
}
static inline int win_set_desktop(handle_t win, int desktop_idx) {
    return __kchk(__k->window_set_desktop(__k->pid, win, desktop_idx));
}
static inline int win_accept_drop(handle_t win, int enabled) {
    return __kchk(__k->drop_accept(__k->pid, win, enabled));
}

// ---- Events ----
static inline int event_poll(Event *e) {
    return __kchk(__k->poll_event(__k->pid, e));
}

// ---- Drawing & Canvas ----
static inline int draw_rect(handle_t win, int x, int y, int w, int h, unsigned argb) {
    return __kchk(__k->fill_rect(__k->pid, win, x, y, w, h, argb));
}
static inline handle_t canvas_create(handle_t parent, int x, int y, int w, int h) {
    return __kchk(__k->create_canvas(__k->pid, parent, x, y, w, h));
}
static inline int canvas_draw_pixels(handle_t canvas, int x, int y, int w, int h, const void *buf, int len) {
    return __kchk(__k->draw_pixels(__k->pid, canvas, x, y, w, h, buf, len));
}

// ---- Basic Controls ----
static inline handle_t button_create(handle_t parent, int x, int y, int w, int h, const char *text) {
    return __kchk(__k->create_button(__k->pid, parent, x, y, w, h, text));
}
static inline handle_t label_create(handle_t parent, int x, int y, const char *text) {
    return __kchk(__k->create_label(__k->pid, parent, x, y, text));
}
static inline int text_set(handle_t obj, const char *text) {
    return __kchk(__k->set_text(__k->pid, obj, text));
}
static inline int text_get(handle_t obj, char *buf, int max) {
    return __kchk(__k->get_text(__k->pid, obj, buf, max));
}
static inline int widget_set_enabled(handle_t obj, int enabled) {
    return __kchk(__k->widget_set_enabled(__k->pid, obj, enabled));
}
static inline int widget_get_enabled(handle_t obj) {
    return __kchk(__k->widget_get_enabled(__k->pid, obj));
}
static inline int widget_focus(handle_t obj) {
    return __kchk(__k->widget_focus(__k->pid, obj));
}
static inline int widget_screen_pos(handle_t obj) {
    return __kchk(__k->widget_screen_pos(__k->pid, obj));
}

// ---- Checkboxes & Radio Buttons ----
static inline handle_t checkbox_create(handle_t parent, int x, int y, const char *text) {
    return __kchk(__k->create_checkbox(__k->pid, parent, x, y, text));
}
static inline int checkbox_set(handle_t obj, int checked) {
    return __kchk(__k->set_checked(__k->pid, obj, checked));
}
static inline int checkbox_get(handle_t obj) {
    return __kchk(__k->get_checked(__k->pid, obj));
}
static inline handle_t radio_create(handle_t parent, int x, int y, const char *text, const char *group) {
    return __kchk(__k->create_radio(__k->pid, parent, x, y, text, group));
}

// ---- Sliders & Progress Bars ----
static inline handle_t slider_create(handle_t parent, int x, int y, int w, int horiz, int min, int max, int value) {
    return __kchk(__k->create_slider(__k->pid, parent, x, y, w, horiz, min, max, value));
}
static inline int slider_set(handle_t obj, int value) {
    return __kchk(__k->slider_set(__k->pid, obj, value));
}
static inline int slider_get(handle_t obj) {
    return __kchk(__k->slider_get(__k->pid, obj));
}
static inline handle_t progress_create(handle_t parent, int x, int y, int w, int h) {
    return __kchk(__k->create_progress(__k->pid, parent, x, y, w, h));
}
static inline int progress_setup(handle_t obj, int min, int max) {
    return __kchk(__k->progress_setup(__k->pid, obj, min, max));
}
static inline int progress_set(handle_t obj, int value) {
    return __kchk(__k->progress_set(__k->pid, obj, value));
}

// ---- Text Fields ----
static inline handle_t textfield_create(handle_t parent, int x, int y, int w) {
    return __kchk(__k->create_textfield(__k->pid, parent, x, y, w));
}
static inline int textfield_setup(handle_t obj, int maxlen, int password, int readonly_) {
    return __kchk(__k->textfield_setup(__k->pid, obj, maxlen, password, readonly_));
}
static inline int textfield_select(handle_t obj, int start, int len) {
    return __kchk(__k->textfield_select(__k->pid, obj, start, len));
}
static inline int textfield_get_caret(handle_t obj) {
    return __kchk(__k->textfield_get_caret(__k->pid, obj));
}
static inline int textfield_set_multiline(handle_t obj, int multiline) {
    return __kchk(__k->textfield_set_multiline(__k->pid, obj, multiline));
}

// ---- Combo Boxes ----
static inline handle_t combo_create(handle_t parent, int x, int y, int w) {
    return __kchk(__k->create_combo(__k->pid, parent, x, y, w));
}
static inline int combo_add_item(handle_t obj, const char *text) {
    return __kchk(__k->combo_add_item(__k->pid, obj, text));
}
static inline int combo_remove_item(handle_t obj, int index) {
    return __kchk(__k->combo_remove_item(__k->pid, obj, index));
}
static inline int combo_clear(handle_t obj) {
    return __kchk(__k->combo_clear(__k->pid, obj));
}
static inline int combo_get_selected(handle_t obj) {
    return __kchk(__k->combo_get_selected(__k->pid, obj));
}
static inline int combo_set_selected(handle_t obj, int index) {
    return __kchk(__k->combo_set_selected(__k->pid, obj, index));
}

// ---- List Boxes ----
static inline handle_t listbox_create(handle_t parent, int x, int y, int w, int h) {
    return __kchk(__k->create_listbox(__k->pid, parent, x, y, w, h));
}
static inline int listbox_add_item(handle_t obj, const char *text) {
    return __kchk(__k->list_add_item(__k->pid, obj, text));
}
static inline int listbox_remove_item(handle_t obj, int index) {
    return __kchk(__k->list_remove_item(__k->pid, obj, index));
}
static inline int listbox_clear(handle_t obj) {
    return __kchk(__k->list_clear(__k->pid, obj));
}
static inline int listbox_get_selected(handle_t obj) {
    return __kchk(__k->list_get_selected(__k->pid, obj));
}
static inline int listbox_select(handle_t obj, int index) {
    return __kchk(__k->list_select(__k->pid, obj, index));
}
static inline int listbox_add_column(handle_t obj, const char *title, int width) {
    return __kchk(__k->list_add_column(__k->pid, obj, title, width));
}
static inline int listbox_add_row(handle_t obj, const char *buf, int len) {
    return __kchk(__k->list_add_row(__k->pid, obj, buf, len));
}
static inline int listbox_hit_test(handle_t obj, int x, int y) {
    return __kchk(__k->list_hit_test(__k->pid, obj, x, y));
}

// ---- Tab Controls ----
static inline handle_t tabs_create(handle_t parent, int x, int y, int w, int h) {
    return __kchk(__k->create_tabs(__k->pid, parent, x, y, w, h));
}
static inline handle_t tabs_add_page(handle_t obj, const char *title) {
    return __kchk(__k->tab_add_page(__k->pid, obj, title));
}
static inline int tabs_select(handle_t obj, int index) {
    return __kchk(__k->tab_select(__k->pid, obj, index));
}
static inline int tabs_get_selected(handle_t obj) {
    return __kchk(__k->tab_get_selected(__k->pid, obj));
}

// ---- Menu Bars ----
static inline handle_t menubar_create(handle_t win) {
    return __kchk(__k->menubar_create(__k->pid, win));
}
static inline handle_t menu_add_item(handle_t obj, const char *page, const char *text) {
    return __kchk(__k->menu_add_item(__k->pid, obj, page, text));
}

// ---- Toolbars ----
static inline handle_t toolbar_create(handle_t parent, int x, int y, int w) {
    return __kchk(__k->toolbar_create(__k->pid, parent, x, y, w));
}
static inline handle_t toolbar_add_button(handle_t obj, const char *text) {
    return __kchk(__k->toolbar_add_button(__k->pid, obj, text));
}
static inline int toolbar_add_separator(handle_t obj) {
    return __kchk(__k->toolbar_add_separator(__k->pid, obj));
}

// ---- Status Bars ----
static inline handle_t statusbar_create(handle_t parent, int x, int y, int w) {
    return __kchk(__k->statusbar_create(__k->pid, parent, x, y, w));
}
static inline int status_add_panel(handle_t obj, const char *text, int width) {
    return __kchk(__k->status_add_panel(__k->pid, obj, text, width));
}
static inline int status_set_text(handle_t obj, int index, const char *text) {
    return __kchk(__k->status_set_text(__k->pid, obj, index, text));
}

// ---- Popup Menus ----
static inline handle_t popup_create(void) {
    return __kchk(__k->popup_create(__k->pid));
}
static inline handle_t popup_add_item(handle_t obj, const char *text) {
    return __kchk(__k->popup_add_item(__k->pid, obj, text));
}
static inline int popup_show_at(handle_t obj, int x, int y) {
    return __kchk(__k->popup_show_at(__k->pid, obj, x, y));
}

// ---- Tree Views ----
static inline handle_t tree_create(handle_t parent, int x, int y, int w, int h) {
    return __kchk(__k->tree_create(__k->pid, parent, x, y, w, h));
}
static inline handle_t tree_add_root(handle_t obj, const char *text, const char *icon) {
    return __kchk(__k->tree_add_root(__k->pid, obj, text, icon));
}
static inline handle_t tree_add_child(handle_t obj, handle_t item, const char *text, const char *icon) {
    return __kchk(__k->tree_add_child(__k->pid, obj, item, text, icon));
}
static inline int tree_clear(handle_t obj) {
    return __kchk(__k->tree_clear(__k->pid, obj));
}
static inline handle_t tree_get_selected(handle_t obj) {
    return __kchk(__k->tree_selected_item(__k->pid, obj));
}
static inline int tree_set_expanded(handle_t obj, handle_t item, int expanded) {
    return __kchk(__k->tree_set_expanded(__k->pid, obj, item, expanded));
}

// ---- Data Grids ----
static inline handle_t grid_create(handle_t parent, int x, int y, int w, int h) {
    return __kchk(__k->grid_create(__k->pid, parent, x, y, w, h));
}
static inline int grid_add_column(handle_t obj, const char *title, int width) {
    return __kchk(__k->grid_add_column(__k->pid, obj, title, width));
}
static inline int grid_add_row(handle_t obj, const char *buf, int len) {
    return __kchk(__k->grid_add_row(__k->pid, obj, buf, len));
}
static inline int grid_clear(handle_t obj) {
    return __kchk(__k->grid_clear(__k->pid, obj));
}
static inline int grid_get_selected(handle_t obj) {
    return __kchk(__k->grid_get_selected(__k->pid, obj));
}
static inline int grid_select(handle_t obj, int row) {
    return __kchk(__k->grid_select(__k->pid, obj, row));
}
static inline int grid_set_cell(handle_t obj, int row, int col, const char *text) {
    return __kchk(__k->grid_set_cell(__k->pid, obj, row, col, text));
}
static inline int grid_get_cell(handle_t obj, int row, int col, char *buf, int max) {
    return __kchk(__k->grid_get_cell(__k->pid, obj, row, col, buf, max));
}
static inline int grid_sort(handle_t obj, int col, int descending) {
    return __kchk(__k->grid_sort(__k->pid, obj, col, descending));
}

// ---- Specialized Dialogs & Pickers ----
static inline handle_t msgbox_show(const char *title, const char *text, const char *buttons, int len) {
    return __kchk(__k->msgbox(__k->pid, title, text, buttons, len));
}
static inline handle_t filedialog_show(const char *title, const char *initial, const char *filter, int save) {
    return __kchk(__k->filedialog_open(__k->pid, title, initial, filter, save));
}
static inline handle_t colorpicker_create(handle_t parent, int x, int y) {
    return __kchk(__k->colorpicker_create(__k->pid, parent, x, y));
}
static inline int colorpicker_get(handle_t obj) {
    return __kchk(__k->colorpicker_get(__k->pid, obj));
}
static inline int colorpicker_set(handle_t obj, int argb) {
    return __kchk(__k->colorpicker_set(__k->pid, obj, argb));
}
static inline handle_t numeric_create(handle_t parent, int x, int y, int w, int min, int max, int inc, int value) {
    return __kchk(__k->numeric_create(__k->pid, parent, x, y, w, min, max, inc, value));
}
static inline int numeric_set(handle_t obj, int value) {
    return __kchk(__k->numeric_set(__k->pid, obj, value));
}
static inline int numeric_get(handle_t obj) {
    return __kchk(__k->numeric_get(__k->pid, obj));
}
static inline handle_t calendar_create(handle_t parent, int x, int y) {
    return __kchk(__k->calendar_create(__k->pid, parent, x, y));
}
static inline int calendar_get(handle_t obj) {
    return __kchk(__k->calendar_get(__k->pid, obj));
}
static inline int calendar_set(handle_t obj, int y, int m, int d) {
    return __kchk(__k->calendar_set(__k->pid, obj, y, m, d));
}

// ---- Terminal Component ----
static inline handle_t terminal_create(handle_t parent, int x, int y, int w, int h) {
    return __kchk(__k->create_terminal(__k->pid, parent, x, y, w, h));
}

// Convenient aliases
#define btn_create button_create
#define lbl_create label_create

#endif // WINDOSE_GUI_H
