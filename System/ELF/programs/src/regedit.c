/**
 * Windose OS Native Registry Editor (ELF)
 * System settings and registry key inspection, creation, and deletion.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

static handle_t main_win = 0;
static handle_t h_btn_flt_all = 0;
static handle_t h_btn_flt_sys = 0;
static handle_t h_btn_flt_soft = 0;
static handle_t h_list = 0;
static handle_t h_txt_key = 0;
static handle_t h_txt_val = 0;
static handle_t h_btn_set = 0;
static handle_t h_btn_new = 0;
static handle_t h_btn_del = 0;
static handle_t h_btn_refresh = 0;
static handle_t h_lbl_status = 0;

static char current_prefix[64] = "";
static char reg_keys[256][128];
static int key_count = 0;

static void refresh_keys(void) {
    char buf[8192];
    int n = k_reg_list(current_prefix, buf, (int)sizeof(buf));
    key_count = 0;
    k_list_clear(h_list);
    if (n <= 0) {
        k_set_text(h_lbl_status, "No registry keys found.");
        return;
    }
    buf[n] = '\0';

    char *line = buf;
    while (*line && key_count < 256) {
        char *eol = strchr(line, '\n');
        if (eol) *eol = '\0';

        if (*line) {
            strncpy(reg_keys[key_count], line, sizeof(reg_keys[key_count]) - 1);
            reg_keys[key_count][sizeof(reg_keys[key_count]) - 1] = '\0';
            k_list_add_item(h_list, reg_keys[key_count]);
            key_count++;
        }

        if (!eol) break;
        line = eol + 1;
    }

    char stat[64];
    if (current_prefix[0])
        snprintf(stat, sizeof(stat), "Filter: %s (%d keys)", current_prefix, key_count);
    else
        snprintf(stat, sizeof(stat), "All Keys: %d loaded", key_count);
    k_set_text(h_lbl_status, stat);
}

static void select_key_at(int idx) {
    if (idx >= 0 && idx < key_count) {
        k_set_text(h_txt_key, reg_keys[idx]);
        char val[512] = {0};
        k_reg_get(reg_keys[idx], val, (int)sizeof(val));
        k_set_text(h_txt_val, val);
        k_set_text(h_lbl_status, "Key selected.");
    }
}

static void save_current_key(void) {
    char kbuf[128] = {0};
    char vbuf[512] = {0};
    k_get_text(h_txt_key, kbuf, (int)sizeof(kbuf));
    k_get_text(h_txt_val, vbuf, (int)sizeof(vbuf));

    if (strlen(kbuf) == 0) {
        k_set_text(h_lbl_status, "Error: Key name is empty.");
        return;
    }

    int r = k_reg_set(kbuf, vbuf);
    if (r > 0) {
        k_set_text(h_lbl_status, "Key saved successfully.");
        int found = 0;
        for (int i = 0; i < key_count; i++) {
            if (strcmp(reg_keys[i], kbuf) == 0) {
                found = 1;
                break;
            }
        }
        if (!found) {
            refresh_keys();
        }
    } else {
        k_set_text(h_lbl_status, "Failed to save key.");
    }
}

static void delete_current_key(void) {
    char kbuf[128] = {0};
    k_get_text(h_txt_key, kbuf, (int)sizeof(kbuf));

    if (strlen(kbuf) == 0) {
        k_set_text(h_lbl_status, "Error: No key specified to delete.");
        return;
    }

    int r = k_reg_delete(kbuf);
    if (r == 0) {
        k_set_text(h_lbl_status, "Key deleted successfully.");
        k_set_text(h_txt_key, "");
        k_set_text(h_txt_val, "");
        refresh_keys();
    } else {
        k_set_text(h_lbl_status, "Failed to delete key (not found or protected).");
    }
}

static void new_key_prompt(void) {
    if (current_prefix[0]) {
        char initial[64];
        snprintf(initial, sizeof(initial), "%sNewKey", current_prefix);
        k_set_text(h_txt_key, initial);
    } else {
        k_set_text(h_txt_key, "Software/NewKey");
    }
    k_set_text(h_txt_val, "value");
    k_set_text(h_lbl_status, "Enter key name and value, then click Set Value.");
}

int main(int argc, char **argv) {
    main_win = k_create_window_with_icon(620, 440, "Registry Editor", "/mnt/System/Icons/regedit.png");
    if (!main_win) main_win = k_create_window(620, 440, "Registry Editor");
    if (!main_win) return 1;

    h_btn_flt_all = k_create_button(main_win, 10, 10, 60, 26, "All");
    k_widget_set_anchors(h_btn_flt_all, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_flt_sys = k_create_button(main_win, 75, 10, 80, 26, "System/");
    k_widget_set_anchors(h_btn_flt_sys, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_flt_soft = k_create_button(main_win, 160, 10, 90, 26, "Software/");
    k_widget_set_anchors(h_btn_flt_soft, ANCHOR_LEFT | ANCHOR_TOP);

    h_list = k_create_listbox(main_win, 10, 42, 240, 385);
    k_widget_set_anchors(h_list, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_BOTTOM);

    handle_t lbl_key = k_create_label(main_win, 265, 12, "Key Path:");
    k_widget_set_anchors(lbl_key, ANCHOR_LEFT | ANCHOR_TOP);

    h_txt_key = k_create_textfield(main_win, 265, 34, 340);
    k_widget_set_anchors(h_txt_key, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT);
    k_textfield_setup(h_txt_key, 256, 0, 0);

    handle_t lbl_val = k_create_label(main_win, 265, 72, "Value (String, Integer, or Boolean):");
    k_widget_set_anchors(lbl_val, ANCHOR_LEFT | ANCHOR_TOP);

    h_txt_val = k_create_textfield(main_win, 265, 94, 340);
    k_widget_set_anchors(h_txt_val, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT);
    k_textfield_setup(h_txt_val, 512, 0, 0);

    h_btn_set = k_create_button(main_win, 265, 138, 80, 30, "Set Value");
    k_widget_set_anchors(h_btn_set, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_new = k_create_button(main_win, 350, 138, 75, 30, "New Key");
    k_widget_set_anchors(h_btn_new, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_del = k_create_button(main_win, 430, 138, 70, 30, "Delete");
    k_widget_set_anchors(h_btn_del, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_refresh = k_create_button(main_win, 505, 138, 75, 30, "Refresh");
    k_widget_set_anchors(h_btn_refresh, ANCHOR_LEFT | ANCHOR_TOP);

    h_lbl_status = k_create_label(main_win, 265, 185, "Ready");
    k_widget_set_anchors(h_lbl_status, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT);

    handle_t lbl_hint = k_create_label(main_win, 265, 220, "Select a key to view or edit its value.\nUse 'New Key' to create, 'Delete' to remove.\nKeys persist across Windose sessions.");
    k_widget_set_anchors(lbl_hint, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT);

    refresh_keys();

    for (;;) {
        Event e;
        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE && e.handle == main_win) {
                k_close(main_win);
                return 0;
            }
            if (e.type == EV_CLICK) {
                if (e.handle == h_btn_flt_all) {
                    current_prefix[0] = '\0';
                    refresh_keys();
                } else if (e.handle == h_btn_flt_sys) {
                    strncpy(current_prefix, "System", sizeof(current_prefix) - 1);
                    refresh_keys();
                } else if (e.handle == h_btn_flt_soft) {
                    strncpy(current_prefix, "Software", sizeof(current_prefix) - 1);
                    refresh_keys();
                } else if (e.handle == h_btn_refresh) {
                    refresh_keys();
                } else if (e.handle == h_btn_set) {
                    save_current_key();
                } else if (e.handle == h_btn_new) {
                    new_key_prompt();
                } else if (e.handle == h_btn_del) {
                    delete_current_key();
                } else if (e.handle == h_list) {
                    select_key_at(e.a);
                }
            }
        }
        k_sleep(20);
    }
}
