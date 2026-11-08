// properties.c - Native Windows 7-style File & Folder Properties application.
// Usage: properties <path>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include "kernel.h"

#define MAXPATH 260

static char fullpath[MAXPATH];
static char dirpath[MAXPATH];
static char filename[MAXPATH];
static FileStat file_st;
static int is_directory = 0;
static uint64_t file_bytes = 0;
static int dir_files_count = 0;
static int dir_subdirs_count = 0;

static void split_path(const char *path, char *out_dir, char *out_name)
{
    int len = (int)strlen(path);
    while (len > 1 && (path[len - 1] == '/' || path[len - 1] == '\\')) len--;

    int last_slash = -1;
    for (int i = 0; i < len; i++) {
        if (path[i] == '/' || path[i] == '\\') last_slash = i;
    }

    if (last_slash < 0) {
        strcpy(out_dir, ".");
        strncpy(out_name, path, MAXPATH - 1);
        out_name[MAXPATH - 1] = '\0';
    } else if (last_slash == 0) {
        strcpy(out_dir, "/");
        int name_len = len - 1;
        if (name_len > MAXPATH - 1) name_len = MAXPATH - 1;
        strncpy(out_name, path + 1, name_len);
        out_name[name_len] = '\0';
    } else {
        int dir_len = last_slash;
        if (dir_len > MAXPATH - 1) dir_len = MAXPATH - 1;
        strncpy(out_dir, path, dir_len);
        out_dir[dir_len] = '\0';

        int name_len = len - (last_slash + 1);
        if (name_len > MAXPATH - 1) name_len = MAXPATH - 1;
        strncpy(out_name, path + last_slash + 1, name_len);
        out_name[name_len] = '\0';
    }
}

static void format_size(uint64_t bytes, char *out, size_t max)
{
    if (bytes < 1024) {
        snprintf(out, max, "%llu bytes", (unsigned long long)bytes);
    } else if (bytes < 1024 * 1024) {
        double kb = (double)bytes / 1024.0;
        snprintf(out, max, "%.1f KB (%llu bytes)", kb, (unsigned long long)bytes);
    } else if (bytes < 1024 * 1024 * 1024) {
        double mb = (double)bytes / (1024.0 * 1024.0);
        snprintf(out, max, "%.2f MB (%llu bytes)", mb, (unsigned long long)bytes);
    } else {
        double gb = (double)bytes / (1024.0 * 1024.0 * 1024.0);
        snprintf(out, max, "%.2f GB (%llu bytes)", gb, (unsigned long long)bytes);
    }
}

static const char *get_file_type(const char *name, int is_dir)
{
    if (is_dir) return "File folder";
    const char *dot = strrchr(name, '.');
    if (!dot) return "File";
    if (!strcasecmp(dot, ".c") || !strcasecmp(dot, ".h")) return "C Source File";
    if (!strcasecmp(dot, ".elf")) return "Windose Executable (.elf)";
    if (!strcasecmp(dot, ".txt") || !strcasecmp(dot, ".log")) return "Text Document";
    if (!strcasecmp(dot, ".png") || !strcasecmp(dot, ".bmp")) return "Image File";
    if (!strcasecmp(dot, ".lua")) return "Lua Script";
    if (!strcasecmp(dot, ".json") || !strcasecmp(dot, ".xml")) return "Data File";
    return "Application / File";
}

static const char *get_default_app(const char *name, int is_dir)
{
    if (is_dir) return "File Explorer";
    const char *dot = strrchr(name, '.');
    if (!dot) return "Unknown Application";
    if (!strcasecmp(dot, ".c") || !strcasecmp(dot, ".h") || !strcasecmp(dot, ".txt") || !strcasecmp(dot, ".log"))
        return "AeroEditor";
    if (!strcasecmp(dot, ".elf")) return "Windose Kernel (ELF Runner)";
    if (!strcasecmp(dot, ".png") || !strcasecmp(dot, ".bmp")) return "Paint";
    return "Windose Shell";
}

static void count_directory_stats(const char *path)
{
    char listbuf[4096];
    int n = k_list_dir(path, listbuf, (int)sizeof(listbuf));
    if (n <= 0) return;

    int off = 0;
    for (int i = 0; i < n; i++) {
        char *entry = listbuf + off;
        off += (int)strlen(entry) + 1;
        if (!strcmp(entry, ".") || !strcmp(entry, "..")) continue;

        char subpath[512];
        if (strcmp(path, "/") == 0) snprintf(subpath, sizeof(subpath), "/%s", entry);
        else snprintf(subpath, sizeof(subpath), "%s/%s", path, entry);

        FileStat st;
        if (k_stat_file(subpath, &st) == 0) {
            if (st.is_dir) {
                dir_subdirs_count++;
                count_directory_stats(subpath);
            } else {
                dir_files_count++;
                uint64_t b = ((uint64_t)(unsigned)st.size_hi << 32) | (unsigned)st.size_lo;
                file_bytes += b;
            }
        }
    }
}

// Widget handles
static handle_t win;
static handle_t btnTabGen, btnTabSec, btnTabDet;
static handle_t tfName;
static handle_t lblType, lblOpensWith, lblLocation, lblSize, lblSizeOnDisk, lblContains;
static handle_t lblCreated, lblModified;
static handle_t cbReadOnly, cbHidden;

// Security tab
static handle_t lblSecGroups, lblUser1, lblUser2, lblUser3, lblSecPerms;
static handle_t cbPermFull, cbPermMod, cbPermExec, cbPermRead, cbPermWrite;

// Details tab
static handle_t lblDetHeader, lblDetName, lblDetType, lblDetFolder, lblDetSize, lblDetMod, lblDetAttr, lblDetOwner, lblDetComp;

// Action buttons
static handle_t btnOK, btnCancel, btnApply;

static int current_tab = 0; // 0=General, 1=Security, 2=Details

static void set_general_visible(int v)
{
    k_widget_set_visible(tfName, v);
    k_widget_set_visible(lblType, v);
    k_widget_set_visible(lblOpensWith, v);
    k_widget_set_visible(lblLocation, v);
    k_widget_set_visible(lblSize, v);
    k_widget_set_visible(lblSizeOnDisk, v);
    if (is_directory) k_widget_set_visible(lblContains, v);
    k_widget_set_visible(lblCreated, v);
    k_widget_set_visible(lblModified, v);
    k_widget_set_visible(cbReadOnly, v);
    k_widget_set_visible(cbHidden, v);
}

static void set_security_visible(int v)
{
    k_widget_set_visible(lblSecGroups, v);
    k_widget_set_visible(lblUser1, v);
    k_widget_set_visible(lblUser2, v);
    k_widget_set_visible(lblUser3, v);
    k_widget_set_visible(lblSecPerms, v);
    k_widget_set_visible(cbPermFull, v);
    k_widget_set_visible(cbPermMod, v);
    k_widget_set_visible(cbPermExec, v);
    k_widget_set_visible(cbPermRead, v);
    k_widget_set_visible(cbPermWrite, v);
}

static void set_details_visible(int v)
{
    k_widget_set_visible(lblDetHeader, v);
    k_widget_set_visible(lblDetName, v);
    k_widget_set_visible(lblDetType, v);
    k_widget_set_visible(lblDetFolder, v);
    k_widget_set_visible(lblDetSize, v);
    k_widget_set_visible(lblDetMod, v);
    k_widget_set_visible(lblDetAttr, v);
    k_widget_set_visible(lblDetOwner, v);
    k_widget_set_visible(lblDetComp, v);
}

static void switch_tab(int tab)
{
    current_tab = tab;
    set_general_visible(tab == 0);
    set_security_visible(tab == 1);
    set_details_visible(tab == 2);
}

static void load_attributes(const char *path)
{
    char reg_key[320];
    char attr[64];
    snprintf(reg_key, sizeof(reg_key), "Software/Windose/Attributes%s", path);
    if (k_reg_get(reg_key, attr, (int)sizeof(attr)) > 0) {
        if (strchr(attr, 'R')) k_set_checked(cbReadOnly, 1);
        if (strchr(attr, 'H')) k_set_checked(cbHidden, 1);
    }
}

static void save_attributes(const char *path)
{
    char reg_key[320];
    snprintf(reg_key, sizeof(reg_key), "Software/Windose/Attributes%s", path);

    int ro = k_get_checked(cbReadOnly);
    int hid = k_get_checked(cbHidden);
    char attr[16];
    int pos = 0;
    if (ro) attr[pos++] = 'R';
    if (hid) attr[pos++] = 'H';
    if (pos == 0) attr[pos++] = 'A'; // Normal Archive
    attr[pos] = '\0';
    k_reg_set(reg_key, attr);

    // Persist permissions
    snprintf(reg_key, sizeof(reg_key), "Software/Windose/Security%s", path);
    int p_full = k_get_checked(cbPermFull);
    int p_mod = k_get_checked(cbPermMod);
    int p_exec = k_get_checked(cbPermExec);
    int p_read = k_get_checked(cbPermRead);
    int p_write = k_get_checked(cbPermWrite);
    char pbuf[32];
    snprintf(pbuf, sizeof(pbuf), "%d,%d,%d,%d,%d", p_full, p_mod, p_exec, p_read, p_write);
    k_reg_set(reg_key, pbuf);
}

static int apply_changes(void)
{
    char new_name[MAXPATH];
    if (k_get_text(tfName, new_name, (int)sizeof(new_name)) > 0 && strcmp(new_name, filename) != 0) {
        char new_full[MAXPATH];
        if (strcmp(dirpath, "/") == 0) snprintf(new_full, sizeof(new_full), "/%s", new_name);
        else snprintf(new_full, sizeof(new_full), "%s/%s", dirpath, new_name);

        if (k_rename_path(fullpath, new_full) == 0) {
            strncpy(filename, new_name, sizeof(filename) - 1);
            strncpy(fullpath, new_full, sizeof(fullpath) - 1);
            char new_title[320];
            snprintf(new_title, sizeof(new_title), "%s Properties", filename);
            k_set_text(win, new_title);
        } else {
            k_notify_send("Properties Error", "Could not rename file. Target name may exist.");
            return -1;
        }
    }

    save_attributes(fullpath);
    k_notify_send("Properties", "Properties and attributes updated.");
    return 0;
}

int main(int argc, char **argv)
{
    if (argc < 2) {
        strcpy(fullpath, "/mnt");
    } else {
        strncpy(fullpath, argv[1], sizeof(fullpath) - 1);
        fullpath[sizeof(fullpath) - 1] = '\0';
    }

    split_path(fullpath, dirpath, filename);

    if (k_stat_file(fullpath, &file_st) == 0) {
        is_directory = file_st.is_dir;
        file_bytes = ((uint64_t)(unsigned)file_st.size_hi << 32) | (unsigned)file_st.size_lo;
    } else {
        is_directory = 0;
        file_bytes = 0;
    }

    if (is_directory) {
        dir_files_count = 0;
        dir_subdirs_count = 0;
        file_bytes = 0;
        count_directory_stats(fullpath);
    }

    char title[320];
    snprintf(title, sizeof(title), "%s Properties", filename);
    win = k_create_window(370, 480, title);
    if (!win) return 1;

    // Tabs
    btnTabGen = k_create_button(win, 12, 10, 110, 24, "General");
    btnTabSec = k_create_button(win, 128, 10, 110, 24, "Security");
    btnTabDet = k_create_button(win, 244, 10, 110, 24, "Details");

    // --- General Tab Widgets ---
    tfName = k_create_textfield(win, 20, 46, 325);
    k_set_text(tfName, filename);

    char type_buf[128];
    snprintf(type_buf, sizeof(type_buf), "Type of file:    %s", get_file_type(filename, is_directory));
    lblType = k_create_label(win, 20, 80, type_buf);

    char app_buf[128];
    snprintf(app_buf, sizeof(app_buf), "Opens with:     %s", get_default_app(filename, is_directory));
    lblOpensWith = k_create_label(win, 20, 106, app_buf);

    char loc_buf[320];
    snprintf(loc_buf, sizeof(loc_buf), "Location:        %s", dirpath);
    lblLocation = k_create_label(win, 20, 132, loc_buf);

    char size_str[64];
    format_size(file_bytes, size_str, sizeof(size_str));
    char size_buf[128];
    snprintf(size_buf, sizeof(size_buf), "Size:              %s", size_str);
    lblSize = k_create_label(win, 20, 158, size_buf);

    uint64_t disk_bytes = ((file_bytes + 4095) / 4096) * 4096;
    char disk_size_str[64];
    format_size(disk_bytes, disk_size_str, sizeof(disk_size_str));
    char disk_buf[128];
    snprintf(disk_buf, sizeof(disk_buf), "Size on disk:  %s", disk_size_str);
    lblSizeOnDisk = k_create_label(win, 20, 184, disk_buf);

    char contains_buf[128];
    if (is_directory) {
        snprintf(contains_buf, sizeof(contains_buf), "Contains:       %d Files, %d Folders", dir_files_count, dir_subdirs_count);
        lblContains = k_create_label(win, 20, 210, contains_buf);
    } else {
        lblContains = 0;
    }

    int date_y = is_directory ? 238 : 218;
    time_t now = time(NULL);
    struct tm tm_buf;
    localtime_r(&now, &tm_buf);
    char date_str[64];
    strftime(date_str, sizeof(date_str), "%B %d, %Y, %I:%M %p", &tm_buf);

    char created_buf[128];
    snprintf(created_buf, sizeof(created_buf), "Created:        %s", date_str);
    lblCreated = k_create_label(win, 20, date_y, created_buf);

    char mod_buf[128];
    snprintf(mod_buf, sizeof(mod_buf), "Modified:       %s", date_str);
    lblModified = k_create_label(win, 20, date_y + 24, mod_buf);

    int attr_y = date_y + 58;
    cbReadOnly = k_create_checkbox(win, 20, attr_y, "Read-only");
    cbHidden = k_create_checkbox(win, 150, attr_y, "Hidden");

    load_attributes(fullpath);

    // --- Security Tab Widgets ---
    lblSecGroups = k_create_label(win, 20, 48, "Group or user names:");
    lblUser1 = k_create_label(win, 30, 72, "1. SYSTEM");
    lblUser2 = k_create_label(win, 30, 94, "2. Administrators (WINDOSE\\root)");
    lblUser3 = k_create_label(win, 30, 116, "3. Users (WINDOSE\\Users)");

    lblSecPerms = k_create_label(win, 20, 146, "Permissions for Users:");
    cbPermFull = k_create_checkbox(win, 30, 170, "Full control");
    cbPermMod  = k_create_checkbox(win, 30, 194, "Modify");
    cbPermExec = k_create_checkbox(win, 30, 218, "Read & execute");
    cbPermRead = k_create_checkbox(win, 30, 242, "Read");
    cbPermWrite = k_create_checkbox(win, 30, 266, "Write");

    // Standard permissions enabled by default
    k_set_checked(cbPermRead, 1);
    k_set_checked(cbPermExec, is_directory || strstr(filename, ".elf") ? 1 : 0);
    k_set_checked(cbPermWrite, 1);

    // --- Details Tab Widgets ---
    lblDetHeader = k_create_label(win, 20, 48, "Property                         Value");
    char d_name[128]; snprintf(d_name, sizeof(d_name), "Name:                            %s", filename);
    lblDetName = k_create_label(win, 20, 74, d_name);

    char d_type[128]; snprintf(d_type, sizeof(d_type), "Type:                              %s", get_file_type(filename, is_directory));
    lblDetType = k_create_label(win, 20, 98, d_type);

    char d_path[320]; snprintf(d_path, sizeof(d_path), "Folder path:                  %s", dirpath);
    lblDetFolder = k_create_label(win, 20, 122, d_path);

    char d_size[128]; snprintf(d_size, sizeof(d_size), "Size:                              %s", size_str);
    lblDetSize = k_create_label(win, 20, 146, d_size);

    char d_mod[128]; snprintf(d_mod, sizeof(d_mod), "Date modified:             %s", date_str);
    lblDetMod = k_create_label(win, 20, 170, d_mod);

    lblDetAttr = k_create_label(win, 20, 194, "Attributes:                     Archive");
    lblDetOwner = k_create_label(win, 20, 218, "Owner:                          WINDOSE\\root");
    lblDetComp = k_create_label(win, 20, 242, "Computer:                     WINDOSE-PC");

    // --- Bottom Action Buttons ---
    btnOK     = k_create_button(win, 110, 435, 75, 26, "OK");
    btnCancel = k_create_button(win, 195, 435, 75, 26, "Cancel");
    btnApply  = k_create_button(win, 280, 435, 75, 26, "Apply");

    // Start with General Tab
    switch_tab(0);

    Event e;
    while (1) {
        if (!k_poll_event(&e)) {
            k_sleep(16);
            continue;
        }

        if (e.type == EV_CLOSE && e.handle == win) {
            break;
        }

        if (e.type == EV_CLICK) {
            if (e.handle == btnTabGen) {
                switch_tab(0);
            } else if (e.handle == btnTabSec) {
                switch_tab(1);
            } else if (e.handle == btnTabDet) {
                switch_tab(2);
            } else if (e.handle == btnApply) {
                apply_changes();
            } else if (e.handle == btnOK) {
                if (apply_changes() == 0) break;
            } else if (e.handle == btnCancel) {
                break;
            }
        }
    }

    return 0;
}
