// sysdm.c - Native Windows 7-style System Properties application.
// Usage: sysdm
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

static handle_t win;
static handle_t btnTabSys, btnTabName, btnTabAdv;

// Tab 1: System Widgets
static handle_t lblBanner, lblCopyright, lblServicePack;
static handle_t lblSystemHeader, lblRating, lblProcessor, lblRam, lblSysType, lblKernel;
static handle_t lblRegHeader, lblCompNameShort, lblWorkgroupShort;

// Tab 2: Computer Name Widgets
static handle_t lblNameInfo, lblDesc, tfDesc, lblFullName, tfName, lblWorkgroup, tfWorkgroup, lblRenameNote;

// Tab 3: Advanced Widgets
static handle_t lblPerfHeader, lblPerfDesc, lblProfileHeader, lblProfileDesc, lblStartupHeader, lblStartupDesc;
static handle_t btnEnvVars;

// Environment Variables Sub-Dialog Widgets
static handle_t envWin = 0;
static handle_t lblEnvPrompt, lblEnvList1, lblEnvList2, lblEnvList3, lblEnvList4, lblEnvList5, lblEnvList6;
static handle_t lblEnvName, tfEnvName, lblEnvVal, tfEnvVal;
static handle_t btnEnvSet, btnEnvDel, btnEnvClose;

// Action buttons
static handle_t btnOK, btnCancel, btnApply;

static int current_tab = 0;

static void set_system_visible(int v)
{
    k_widget_set_visible(lblBanner, v);
    k_widget_set_visible(lblCopyright, v);
    k_widget_set_visible(lblServicePack, v);
    k_widget_set_visible(lblSystemHeader, v);
    k_widget_set_visible(lblRating, v);
    k_widget_set_visible(lblProcessor, v);
    k_widget_set_visible(lblRam, v);
    k_widget_set_visible(lblSysType, v);
    k_widget_set_visible(lblKernel, v);
    k_widget_set_visible(lblRegHeader, v);
    k_widget_set_visible(lblCompNameShort, v);
    k_widget_set_visible(lblWorkgroupShort, v);
}

static void set_computer_name_visible(int v)
{
    k_widget_set_visible(lblNameInfo, v);
    k_widget_set_visible(lblDesc, v);
    k_widget_set_visible(tfDesc, v);
    k_widget_set_visible(lblFullName, v);
    k_widget_set_visible(tfName, v);
    k_widget_set_visible(lblWorkgroup, v);
    k_widget_set_visible(tfWorkgroup, v);
    k_widget_set_visible(lblRenameNote, v);
}

static void set_advanced_visible(int v)
{
    k_widget_set_visible(lblPerfHeader, v);
    k_widget_set_visible(lblPerfDesc, v);
    k_widget_set_visible(lblProfileHeader, v);
    k_widget_set_visible(lblProfileDesc, v);
    k_widget_set_visible(lblStartupHeader, v);
    k_widget_set_visible(lblStartupDesc, v);
    k_widget_set_visible(btnEnvVars, v);
}

static void switch_tab(int tab)
{
    current_tab = tab;
    set_system_visible(tab == 0);
    set_computer_name_visible(tab == 1);
    set_advanced_visible(tab == 2);
}

static void load_computer_info(char *desc, char *name, char *wg)
{
    if (k_reg_get("System/Computer/Description", desc, 128) <= 0) {
        strcpy(desc, "Windose Workstation");
    }
    if (k_reg_get("System/Computer/Name", name, 128) <= 0) {
        strcpy(name, "WINDOSE-PC");
    }
    if (k_reg_get("System/Computer/Workgroup", wg, 128) <= 0) {
        strcpy(wg, "WORKGROUP");
    }
}

static void save_computer_info(void)
{
    char desc[128], name[128], wg[128];
    if (k_get_text(tfDesc, desc, sizeof(desc)) > 0) {
        k_reg_set("System/Computer/Description", desc);
    }
    if (k_get_text(tfName, name, sizeof(name)) > 0) {
        k_reg_set("System/Computer/Name", name);
    }
    if (k_get_text(tfWorkgroup, wg, sizeof(wg)) > 0) {
        k_reg_set("System/Computer/Workgroup", wg);
    }
    k_notify_send("System Properties", "Computer and workgroup settings updated.");
}

static void update_env_display(void)
{
    if (!envWin) return;
    char b1[128], b2[128], b3[128], b4[128], b5[128], b6[128];
    snprintf(b1, sizeof(b1), "USER:     %s", getenv("USER") ? getenv("USER") : "root");
    snprintf(b2, sizeof(b2), "PATH:     %s", getenv("PATH") ? getenv("PATH") : "/mnt/System/programs");
    snprintf(b3, sizeof(b3), "HOME:     %s", getenv("HOME") ? getenv("HOME") : "/mnt");
    snprintf(b4, sizeof(b4), "PWD:      %s", getenv("PWD") ? getenv("PWD") : "/mnt");
    snprintf(b5, sizeof(b5), "WINDIR:   %s", getenv("WINDIR") ? getenv("WINDIR") : "/mnt/System");

    char custom_val[64];
    if (k_reg_get("System/Environment/EDITOR", custom_val, sizeof(custom_val)) > 0) {
        snprintf(b6, sizeof(b6), "EDITOR:   %s", custom_val);
    } else {
        snprintf(b6, sizeof(b6), "EDITOR:   aeroeditor");
    }

    k_set_text(lblEnvList1, b1);
    k_set_text(lblEnvList2, b2);
    k_set_text(lblEnvList3, b3);
    k_set_text(lblEnvList4, b4);
    k_set_text(lblEnvList5, b5);
    k_set_text(lblEnvList6, b6);
}

static void open_env_dialog(void)
{
    if (envWin) return;
    envWin = k_create_window(420, 390, "Environment Variables");
    if (!envWin) return;

    lblEnvPrompt = k_create_label(envWin, 16, 12, "User & System Environment Variables:");
    lblEnvList1  = k_create_label(envWin, 24, 38, "USER:");
    lblEnvList2  = k_create_label(envWin, 24, 60, "PATH:");
    lblEnvList3  = k_create_label(envWin, 24, 82, "HOME:");
    lblEnvList4  = k_create_label(envWin, 24, 104, "PWD:");
    lblEnvList5  = k_create_label(envWin, 24, 126, "WINDIR:");
    lblEnvList6  = k_create_label(envWin, 24, 148, "EDITOR:");

    lblEnvName = k_create_label(envWin, 16, 185, "Variable Name:");
    tfEnvName  = k_create_textfield(envWin, 16, 206, 380);

    lblEnvVal  = k_create_label(envWin, 16, 238, "Variable Value:");
    tfEnvVal   = k_create_textfield(envWin, 16, 259, 380);

    btnEnvSet   = k_create_button(envWin, 110, 310, 90, 26, "Set / New");
    btnEnvDel   = k_create_button(envWin, 210, 310, 90, 26, "Delete");
    btnEnvClose = k_create_button(envWin, 310, 310, 90, 26, "Close");

    update_env_display();
}

static void close_env_dialog(void)
{
    if (envWin) {
        k_close(envWin);
        envWin = 0;
    }
}

int main(int argc, char **argv)
{
    (void)argc; (void)argv;

    win = k_create_window(440, 480, "System Properties");
    if (!win) return 1;

    // Tab buttons
    btnTabSys  = k_create_button(win, 12, 10, 130, 24, "System");
    btnTabName = k_create_button(win, 150, 10, 140, 24, "Computer Name");
    btnTabAdv  = k_create_button(win, 298, 10, 130, 24, "Advanced");

    // --- Tab 1: System Info ---
    lblBanner = k_create_label(win, 20, 46, "Windose 7 Ultimate");
    lblCopyright = k_create_label(win, 20, 70, "Copyright (c) 2026 Windose Project. All rights reserved.");
    lblServicePack = k_create_label(win, 20, 92, "Cosmos Advanced Agentic Kernel");

    lblSystemHeader = k_create_label(win, 20, 126, "System Information:");
    lblRating = k_create_label(win, 30, 150, "Windows Experience Index:    5.9");
    lblProcessor = k_create_label(win, 30, 174, "Processor:                              x86_64 Multiprocessor CPU");

    uint64_t tot_mem = 0, used_mem = 0, free_mem = 0;
    char ram_buf[128];
    if (k_sys_mem(&tot_mem, &used_mem, &free_mem) == 0 && tot_mem > 0) {
        unsigned long mb = (unsigned long)(tot_mem / (1024ULL * 1024ULL));
        snprintf(ram_buf, sizeof(ram_buf), "Installed memory (RAM):        %lu MB (%lu MB usable)", mb, mb);
    } else {
        snprintf(ram_buf, sizeof(ram_buf), "Installed memory (RAM):        1024 MB");
    }
    lblRam = k_create_label(win, 30, 198, ram_buf);

    lblSysType = k_create_label(win, 30, 222, "System type:                           64-bit Operating System, x64-based PC");
    lblKernel = k_create_label(win, 30, 246, "Kernel build:                            Cosmos 3.0.97 (Freestanding SysV)");

    lblRegHeader = k_create_label(win, 20, 282, "Computer identification:");
    char init_desc[128], init_name[128], init_wg[128];
    load_computer_info(init_desc, init_name, init_wg);

    char comp_buf[128]; snprintf(comp_buf, sizeof(comp_buf), "Computer name:                      %s", init_name);
    lblCompNameShort = k_create_label(win, 30, 306, comp_buf);

    char wg_buf[128]; snprintf(wg_buf, sizeof(wg_buf), "Workgroup:                             %s", init_wg);
    lblWorkgroupShort = k_create_label(win, 30, 330, wg_buf);

    // --- Tab 2: Computer Name ---
    lblNameInfo = k_create_label(win, 20, 48, "Windose identifies your computer on the network by its name.");
    lblDesc = k_create_label(win, 20, 78, "Computer description:");
    tfDesc  = k_create_textfield(win, 20, 100, 395);
    k_set_text(tfDesc, init_desc);

    lblFullName = k_create_label(win, 20, 136, "Full computer name:");
    tfName = k_create_textfield(win, 20, 158, 395);
    k_set_text(tfName, init_name);

    lblWorkgroup = k_create_label(win, 20, 194, "Workgroup:");
    tfWorkgroup = k_create_textfield(win, 20, 216, 395);
    k_set_text(tfWorkgroup, init_wg);

    lblRenameNote = k_create_label(win, 20, 260, "Click Apply or OK to save computer and workgroup identity.");

    // --- Tab 3: Advanced ---
    lblPerfHeader = k_create_label(win, 20, 48, "Performance:");
    lblPerfDesc = k_create_label(win, 30, 72, "Visual effects, Aero compositor, and virtual memory allocation.");

    lblProfileHeader = k_create_label(win, 20, 114, "User Profiles:");
    lblProfileDesc = k_create_label(win, 30, 138, "Desktop settings related to current logon account (WINDOSE\\root).");

    lblStartupHeader = k_create_label(win, 20, 180, "Environment Variables:");
    lblStartupDesc = k_create_label(win, 30, 204, "User and system environment variables for process execution.");

    btnEnvVars = k_create_button(win, 230, 240, 185, 26, "Environment Variables...");

    // --- Action buttons ---
    btnOK     = k_create_button(win, 170, 435, 80, 26, "OK");
    btnCancel = k_create_button(win, 260, 435, 80, 26, "Cancel");
    btnApply  = k_create_button(win, 350, 435, 80, 26, "Apply");

    switch_tab(0);

    Event e;
    while (1) {
        if (!k_poll_event(&e)) {
            k_sleep(16);
            continue;
        }

        if (e.type == EV_CLOSE) {
            if (envWin && e.handle == envWin) {
                close_env_dialog();
                continue;
            } else if (e.handle == win) {
                break;
            }
        }

        if (e.type == EV_CLICK) {
            if (e.handle == btnTabSys) {
                switch_tab(0);
            } else if (e.handle == btnTabName) {
                switch_tab(1);
            } else if (e.handle == btnTabAdv) {
                switch_tab(2);
            } else if (e.handle == btnEnvVars) {
                open_env_dialog();
            } else if (envWin && e.handle == btnEnvClose) {
                close_env_dialog();
            } else if (envWin && e.handle == btnEnvSet) {
                char k[64], v[128];
                if (k_get_text(tfEnvName, k, sizeof(k)) > 0 && k[0]) {
                    k_get_text(tfEnvVal, v, sizeof(v));
                    setenv(k, v, 1);
                    char reg_key[128];
                    snprintf(reg_key, sizeof(reg_key), "System/Environment/%s", k);
                    k_reg_set(reg_key, v);
                    update_env_display();
                    k_notify_send("Environment", "Variable set successfully.");
                }
            } else if (envWin && e.handle == btnEnvDel) {
                char k[64];
                if (k_get_text(tfEnvName, k, sizeof(k)) > 0 && k[0]) {
                    unsetenv(k);
                    char reg_key[128];
                    snprintf(reg_key, sizeof(reg_key), "System/Environment/%s", k);
                    k_reg_delete(reg_key);
                    update_env_display();
                    k_notify_send("Environment", "Variable removed.");
                }
            } else if (e.handle == btnApply) {
                save_computer_info();
            } else if (e.handle == btnOK) {
                save_computer_info();
                break;
            } else if (e.handle == btnCancel) {
                break;
            }
        }
    }

    return 0;
}
