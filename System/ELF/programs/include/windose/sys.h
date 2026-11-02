// windose/sys.h - Windose Process, System, Environment, Clipboard, and Desktop APIs.
#ifndef WINDOSE_SYS_H
#define WINDOSE_SYS_H

#include "core.h"

// ---- Process & Execution ----
static inline int proc_pid(void) {
    return __k->pid;
}
static inline int proc_spawn(const char *path, const char *argbuf, int arglen) {
    return __kchk(__k->spawn(__k->pid, path, argbuf, arglen));
}
static inline int proc_kill(int target) {
    return __kchk(__k->kill(__k->pid, target));
}
static inline int proc_wait(int target, int *exit_code, int timeout_ms) {
    return __kchk(__k->wait_pid(__k->pid, target, exit_code, timeout_ms));
}
static inline int proc_restart(int target_pid) {
    return __kchk(__k->restart_proc(__k->pid, target_pid));
}

// ---- System Timing & Direct I/O ----
static inline int sys_sleep(int ms) {
    return __kchk(__k->sleep(__k->pid, ms));
}
static inline int sys_yield(void) {
    return __kchk(__k->yield(__k->pid));
}
static inline int sys_ticks(void) {
    return __kchk(__k->ticks(__k->pid));
}
static inline void sys_write(const char *s, int len) {
    __k->write(__k->pid, s, len);
}

// ---- Environment Variables ----
static inline int env_get(const char *name, char *buf, int max) {
    return __kchk(__k->get_env(__k->pid, name, buf, max));
}
static inline int env_set(const char *name, const char *val) {
    return __kchk(__k->set_env(__k->pid, name, val));
}

// ---- Clipboard & Drag-and-Drop ----
static inline int clipboard_get_text(char *buf, int max) {
    return __kchk(__k->clipboard_get(__k->pid, buf, max));
}
static inline int clipboard_set_text(const char *text, int len) {
    return __kchk(__k->clipboard_set(__k->pid, text, len));
}
static inline int dnd_get_text_payload(char *buf, int max) {
    return __kchk(__k->dnd_get_text(__k->pid, buf, max));
}

// ---- Desktop & Shell Management ----
static inline int desktop_get_count(void) {
    return __kchk(__k->desktop_count(__k->pid));
}
static inline int desktop_get_current(void) {
    return __kchk(__k->desktop_current(__k->pid));
}
static inline int desktop_switch_to(int index) {
    return __kchk(__k->desktop_switch(__k->pid, index));
}
static inline int screen_get_size(int *out_w, int *out_h) {
    return __kchk(__k->screen_size(__k->pid, out_w, out_h));
}
static inline int shell_restart(void) {
    return __kchk(__k->restart_shell(__k->pid));
}

// ---- Application Launch & Notifications ----
static inline int app_launch(const char *name, const char *arg) {
    return __kchk(__k->launch_app(__k->pid, name, arg));
}
static inline int app_open_file(const char *path) {
    return __kchk(__k->open_file(__k->pid, path));
}
static inline int notify_post(const char *title, const char *text) {
    return __kchk(__k->notify_send(__k->pid, title, text));
}

#endif // WINDOSE_SYS_H
