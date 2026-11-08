/**
 * Windose OS Native Registry Editor (ELF)
 * System settings and registry key inspection and modification.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

static handle_t main_win = 0;
static handle_t h_list = 0;
static handle_t h_txt_key = 0;
static handle_t h_txt_val = 0;
static handle_t h_btn_set = 0;
static handle_t h_btn_refresh = 0;
static handle_t h_lbl_status = 0;

static char reg_keys[256][128];
static int key_count = 0;

static void refresh_keys(void) {
    char buf[8192];
    int n = k_reg_list("", buf, (int)sizeof(buf));
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
    snprintf(stat, sizeof(stat), "Loaded %d keys", key_count);
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

int main(int argc, char **argv) {
    main_win = k_create_window(560, 420, "Registry Editor");
    if (!main_win) return 1;

    h_list = k_create_listbox(main_win, 10, 10, 220, 360);
    k_widget_set_anchors(h_list, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_BOTTOM);

    handle_t lbl_key = k_create_label(main_win, 240, 12, "Key Name:");
    k_widget_set_anchors(lbl_key, ANCHOR_LEFT | ANCHOR_TOP);

    h_txt_key = k_create_textfield(main_win, 240, 34, 305);
    k_widget_set_anchors(h_txt_key, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT);
    k_textfield_setup(h_txt_key, 256, 0, 0);

    handle_t lbl_val = k_create_label(main_win, 240, 72, "Value:");
    k_widget_set_anchors(lbl_val, ANCHOR_LEFT | ANCHOR_TOP);

    h_txt_val = k_create_textfield(main_win, 240, 94, 305);
    k_widget_set_anchors(h_txt_val, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT);
    k_textfield_setup(h_txt_val, 512, 0, 0);

    h_btn_set = k_create_button(main_win, 240, 138, 110, 30, "Set Value");
    k_widget_set_anchors(h_btn_set, ANCHOR_LEFT | ANCHOR_TOP);

    h_btn_refresh = k_create_button(main_win, 360, 138, 100, 30, "Refresh");
    k_widget_set_anchors(h_btn_refresh, ANCHOR_LEFT | ANCHOR_TOP);

    h_lbl_status = k_create_label(main_win, 240, 180, "Ready");
    k_widget_set_anchors(h_lbl_status, ANCHOR_LEFT | ANCHOR_TOP | ANCHOR_RIGHT);

    refresh_keys();

    for (;;) {
        Event e;
        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE && e.handle == main_win) {
                k_close(main_win);
                return 0;
            }
            if (e.type == EV_CLICK) {
                if (e.handle == h_btn_refresh) {
                    refresh_keys();
                } else if (e.handle == h_btn_set) {
                    save_current_key();
                } else if (e.handle == h_list) {
                    select_key_at(e.a);
                }
            }
        }
        k_sleep(20);
    }
}
