// windose/fs.h - Windose File System, Directory, Drive, and Trash APIs.
#ifndef WINDOSE_FS_H
#define WINDOSE_FS_H

#include "core.h"

// ---- File I/O & Attributes ----
static inline int file_read_raw(const char *path, char *buf, int max) {
    return __kchk(__k->read_file(__k->pid, path, buf, max));
}
static inline int file_write_raw(const char *path, const char *buf, int len) {
    return __kchk(__k->write_file(__k->pid, path, buf, len));
}
static inline int file_stat(const char *path, FileStat *out) {
    return __kchk(__k->stat_file(__k->pid, path, out));
}

// ---- Directory & Path Management ----
static inline int dir_list(const char *path, char *buf, int max) {
    return __kchk(__k->list_dir(__k->pid, path, buf, max));
}
static inline int dir_make(const char *path) {
    return __kchk(__k->make_dir(__k->pid, path));
}
static inline int path_delete(const char *path) {
    return __kchk(__k->delete_path(__k->pid, path));
}
static inline int path_rename(const char *oldp, const char *newp) {
    return __kchk(__k->rename_path(__k->pid, oldp, newp));
}

// ---- Storage & Drives ----
static inline int drives_list(char *buf, int max) {
    return __kchk(__k->list_drives(__k->pid, buf, max));
}
static inline int drive_get_stat(const char *mount, DriveStat *out) {
    return __kchk(__k->drive_stat(__k->pid, mount, out));
}

// ---- Trash Can ----
static inline int trash_delete_file(const char *path) {
    return __kchk(__k->trash_delete(__k->pid, path));
}
static inline int trash_list_items(char *buf, int max) {
    return __kchk(__k->trash_list(__k->pid, buf, max));
}
static inline int trash_restore_item(const char *name) {
    return __kchk(__k->trash_restore(__k->pid, name));
}
static inline int trash_empty_all(void) {
    return __kchk(__k->trash_empty(__k->pid));
}

#endif // WINDOSE_FS_H
