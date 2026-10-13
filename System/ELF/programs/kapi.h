// kapi.h - what the kernel hands to every program. MUST match KernelApi in kernel/ElfRunner.cs
// (same fields, same order). Add new functions at the END of both.
#pragma once

// If the program crashes on its first kernel call, switch to ms_abi
// (it must match the calling convention of [UnmanagedCallersOnly] in your Cosmos build).
#define KABI __attribute__((sysv_abi))

typedef int handle_t;           // opaque kernel handle; 0 = none/failed. Don't do math on it.

typedef struct Event {          // matches KEvent
    int type;                   // EV_*
    int handle;                 // which window/component
    int a, b;
} Event;

enum { EV_NONE = 0, EV_CLICK = 1, EV_CLOSE = 2, EV_KEY = 3 };

typedef struct KernelApi {
    void     (KABI *write)(const char *text, int len);
    handle_t (KABI *create_window)(int w, int h, const char *title);
    int      (KABI *close)(handle_t h);
    void     (KABI *sleep)(int ms);
    handle_t (KABI *create_button)(handle_t parent, int x, int y, int w, int h, const char *text);
    handle_t (KABI *create_label)(handle_t parent, int x, int y, const char *text);
    int      (KABI *set_text)(handle_t obj, const char *text);
    int      (KABI *fill_rect)(handle_t win, int x, int y, int w, int h, unsigned argb);
    int      (KABI *poll_event)(Event *out);
    int      (KABI *read_file)(const char *path, char *buf, int max);
    int      (KABI *write_file)(const char *path, const char *buf, int len);
} KernelApi;

// ---- tiny helpers (no libc in these programs) ----
static inline int kstrlen(const char *s) { int n = 0; while (s[n]) n++; return n; }
static inline void kputs(KernelApi *k, const char *s) { k->write(s, kstrlen(s)); }

// writes decimal 'v' into out (>= 12 bytes), returns length
static inline int kitoa(int v, char *out) {
    char tmp[12]; int n = 0, len = 0;
    unsigned u = v < 0 ? 0u - (unsigned)v : (unsigned)v;
    do { tmp[n++] = (char)('0' + u % 10); u /= 10; } while (u);
    if (v < 0) out[len++] = '-';
    while (n) out[len++] = tmp[--n];
    out[len] = 0;
    return len;
}
