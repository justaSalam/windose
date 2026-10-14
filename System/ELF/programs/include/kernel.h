// kernel.h - the Windose kernel API as seen from C. MUST match KernelApi in kernel/ElfRunner.cs.
// Programs call the k_* wrappers below, never the table directly: the transport (function table today,
// syscall instruction one day) can then change without touching program sources.
#ifndef WINDOSE_KERNEL_H
#define WINDOSE_KERNEL_H
#include <stddef.h>
#include <stdint.h>

// If a program crashes on its first kernel call, switch to ms_abi (must match the C# side's calling
// convention) and rebuild libc + programs.
#define KABI __attribute__((sysv_abi))
#define KAPI_VERSION 2
#define K_KILLED (-100)          // returned by every call after the program was killed; the wrappers exit(137)

typedef int handle_t;            // opaque; 0 = none/failed; never do arithmetic on it

typedef struct Event { int type, handle, a, b; } Event;
enum { EV_NONE = 0, EV_CLICK = 1, EV_CLOSE = 2, EV_KEY = 3 };

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
} KernelApi;

extern KernelApi *__k;
void exit(int code) __attribute__((noreturn));

static inline int __kchk(int r) { if (r == K_KILLED) exit(137); return r; }

static inline void     k_write(const char *s, int len)              { __k->write(__k->pid, s, len); }
static inline handle_t k_create_window(int w, int h, const char *title) { return __kchk(__k->create_window(__k->pid, w, h, title)); }
static inline int      k_close(handle_t h)                          { return __kchk(__k->close(__k->pid, h)); }
static inline int      k_sleep(int ms)                              { return __kchk(__k->sleep(__k->pid, ms)); }
static inline handle_t k_create_button(handle_t parent, int x, int y, int w, int h, const char *text) { return __kchk(__k->create_button(__k->pid, parent, x, y, w, h, text)); }
static inline handle_t k_create_label(handle_t parent, int x, int y, const char *text) { return __kchk(__k->create_label(__k->pid, parent, x, y, text)); }
static inline int      k_set_text(handle_t obj, const char *text)  { return __kchk(__k->set_text(__k->pid, obj, text)); }
static inline int      k_fill_rect(handle_t win, int x, int y, int w, int h, unsigned argb) { return __kchk(__k->fill_rect(__k->pid, win, x, y, w, h, argb)); }
static inline int      k_poll_event(Event *e)                       { return __kchk(__k->poll_event(__k->pid, e)); }
static inline int      k_read_file(const char *path, char *buf, int max) { return __kchk(__k->read_file(__k->pid, path, buf, max)); }
static inline int      k_write_file(const char *path, const char *buf, int len) { return __kchk(__k->write_file(__k->pid, path, buf, len)); }
static inline int      k_ticks(void)                                { return __kchk(__k->ticks(__k->pid)); }
static inline int      k_pid(void)                                  { return __k->pid; }

#endif
