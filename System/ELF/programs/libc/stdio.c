#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

int __vformat(char *buf, size_t cap, const char *fmt, va_list ap);

int vsnprintf(char *buf, size_t n, const char *fmt, va_list ap) { return __vformat(buf, n, fmt, ap); }
int vsprintf(char *buf, const char *fmt, va_list ap) { return __vformat(buf, (size_t)-1 >> 1, fmt, ap); }

int snprintf(char *buf, size_t n, const char *fmt, ...)
{ va_list ap; va_start(ap, fmt); int r = __vformat(buf, n, fmt, ap); va_end(ap); return r; }

int sprintf(char *buf, const char *fmt, ...)
{ va_list ap; va_start(ap, fmt); int r = __vformat(buf, (size_t)-1 >> 1, fmt, ap); va_end(ap); return r; }

int vprintf(const char *fmt, va_list ap)
{
    char small[512];
    va_list ap2; va_copy(ap2, ap);
    int len = __vformat(small, sizeof small, fmt, ap);
    if (len < (int)sizeof small) { k_write(small, len); va_end(ap2); return len; }
    char *big = malloc((size_t)len + 1);
    if (!big) { k_write(small, (int)sizeof small - 1); va_end(ap2); return len; }
    __vformat(big, (size_t)len + 1, fmt, ap2);
    va_end(ap2);
    k_write(big, len);
    free(big);
    return len;
}

int printf(const char *fmt, ...)
{ va_list ap; va_start(ap, fmt); int r = vprintf(fmt, ap); va_end(ap); return r; }

int puts(const char *s) { k_write(s, (int)strlen(s)); k_write("\n", 1); return 1; }
int putchar(int c) { char ch = (char)c; k_write(&ch, 1); return c; }

char *file_read(const char *path, int *size)
{
    int cap = 64 * 1024;
    for (;;) {
        char *buf = malloc((size_t)cap + 1);
        if (!buf) return NULL;
        int n = k_read_file(path, buf, cap);
        if (n < 0) { free(buf); return NULL; }
        if (n < cap || cap >= 1024 * 1024) { buf[n] = 0; if (size) *size = n; return buf; }
        free(buf);
        cap *= 2;
    }
}
