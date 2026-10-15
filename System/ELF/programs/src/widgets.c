#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

// Widget gallery: exercises every widget syscall. Esc quits.
int main(int argc, char **argv)
{
    (void)argc; (void)argv;
    handle_t win = k_create_window(460, 380, "Widgets");
    if (!win) { puts("window creation failed"); return 1; }

    handle_t bar = k_menubar_create(win);
    k_menu_add_item(bar, "File", "About");
    k_menu_add_item(bar, "File", "-");
    handle_t quit = k_menu_add_item(bar, "File", "Quit");

    handle_t tabs = k_create_tabs(win, 8, 34, 444, 300);
    handle_t p1 = k_tab_add_page(tabs, "Inputs");
    handle_t p2 = k_tab_add_page(tabs, "Lists");
    if (!tabs || !p1 || !p2) { puts("tabs failed"); k_close(win); return 1; }

    handle_t chk = k_create_checkbox(p1, 12, 8, "Enable feature");
    handle_t r1 = k_create_radio(p1, 12, 34, "Choice A", "grp");
    handle_t r2 = k_create_radio(p1, 12, 60, "Choice B", "grp");
    handle_t sl = k_create_slider(p1, 12, 92, 200, 1, 0, 100, 30);
    handle_t slv = k_create_label(p1, 220, 92, "30");
    handle_t pr = k_create_progress(p1, 12, 124, 200, 22);
    handle_t bump = k_create_button(p1, 220, 124, 90, 24, "+10");
    handle_t tf = k_create_textfield(p1, 12, 156, 200);
    handle_t getb = k_create_button(p1, 220, 154, 90, 24, "Read");
    handle_t tfo = k_create_label(p1, 12, 184, "");
    k_textfield_setup(tf, 24, 0, 0);

    handle_t cb = k_create_combo(p2, 12, 8, 200);
    k_combo_add_item(cb, "red");
    k_combo_add_item(cb, "green");
    k_combo_add_item(cb, "blue");
    handle_t lb = k_create_listbox(p2, 12, 40, 200, 150);
    k_list_add_item(lb, "alpha");
    k_list_add_item(lb, "beta");
    k_list_add_item(lb, "gamma");
    handle_t cbl = k_create_label(p2, 224, 8, "combo: -");
    handle_t lbl = k_create_label(p2, 224, 40, "list: -");
    handle_t msgb = k_create_button(p2, 224, 120, 120, 26, "Message...");

    handle_t p3 = k_tab_add_page(tabs, "More");
    handle_t cp = k_colorpicker_create(p3, 12, 8);
    handle_t cpl = k_create_label(p3, 84, 8, "color");
    handle_t nm = k_numeric_create(p3, 12, 40, 140, 0, 100, 5, 20);
    handle_t nml = k_create_label(p3, 160, 40, "20");
    handle_t cal = k_calendar_create(p3, 12, 72);
    handle_t call = k_create_label(p3, 244, 72, "date");
    handle_t fob = k_create_button(p3, 244, 120, 120, 26, "Open file...");
    handle_t dnd = k_create_button(p3, 244, 152, 120, 26, "Drop: off");
    int dndon = 0;
    k_drop_accept(win, 0);

    handle_t sb = k_statusbar_create(win, 8, 344, 444);
    int p0 = k_status_add_panel(sb, "ready", 220);
    int p1i = k_status_add_panel(sb, "v6", 100);

    static const char msgbtns[] = "OK\0Later\0";
    char text[64], row[96];
    static char dlgpath[300];
    handle_t penddlg = 0;
    int prog = 0;

    for (;;) {
        Event e;
        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE) { k_close(win); return 0; }
            if (e.type == EV_KEY && (e.b & 0xFFFF) == 27) { k_close(win); return 0; }
            if (e.type == EV_DROP) {
                if (k_dnd_get_text(dlgpath, (int)sizeof dlgpath) >= 0)
                    k_status_set_text(sb, p0, dlgpath);
                continue;
            }
            if (e.type != EV_CLICK) continue;
            if (e.handle == quit) { k_close(win); return 0; }
            if (e.handle == penddlg && penddlg) {
                if (e.a == 1 && k_get_text(penddlg, dlgpath, (int)sizeof dlgpath) >= 0)
                    k_status_set_text(sb, p0, dlgpath);
                else
                    k_status_set_text(sb, p0, "cancelled");
                penddlg = 0;
                continue;
            }
            if (e.handle == bump) {
                prog = (prog + 10) % 110;
                k_progress_set(pr, prog);
            } else if (e.handle == sl) {
                snprintf(row, sizeof row, "%d", k_slider_get(sl));
                k_set_text(slv, row);
            } else if (e.handle == getb) {
                if (k_get_text(tf, text, (int)sizeof text) >= 0) k_set_text(tfo, text);
            } else if (e.handle == cb) {
                snprintf(row, sizeof row, "combo: %d", e.a);
                k_set_text(cbl, row);
            } else if (e.handle == lb) {
                snprintf(row, sizeof row, "list: %d", e.a);
                k_set_text(lbl, row);
            } else if (e.handle == msgb) {
                k_msgbox("Hello", "Widgets work.", msgbtns, (int)sizeof msgbtns);
            } else if (e.handle == chk || e.handle == r1 || e.handle == r2) {
                snprintf(row, sizeof row, "checked=%d", e.a);
                k_status_set_text(sb, p0, row);
            } else if (e.handle == tabs) {
                snprintf(row, sizeof row, "tab %d", e.a);
                k_status_set_text(sb, p1i, row);
            } else if (e.handle == cp) {
                snprintf(row, sizeof row, "0x%X", (unsigned)e.a);
                k_set_text(cpl, row);
            } else if (e.handle == nm) {
                snprintf(row, sizeof row, "%d", e.a);
                k_set_text(nml, row);
            } else if (e.handle == cal) {
                snprintf(row, sizeof row, "%d", e.a);
                k_set_text(call, row);
            } else if (e.handle == fob) {
                penddlg = k_filedialog_open("Open", "/mnt", "*.*", 0);
                if (!penddlg) k_status_set_text(sb, p0, "dialog failed");
            } else if (e.handle == dnd) {
                dndon = !dndon;
                k_drop_accept(win, dndon);
                k_set_text(dnd, dndon ? "Drop: on" : "Drop: off");
            }
        }
        k_sleep(10);
    }
}
