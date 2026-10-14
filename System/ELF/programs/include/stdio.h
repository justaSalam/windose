#ifndef WINDOSE_STDIO_H
#define WINDOSE_STDIO_H
#include <stddef.h>
#include <stdarg.h>
int printf(const char *fmt, ...) __attribute__((format(printf, 1, 2)));
int sprintf(char *buf, const char *fmt, ...) __attribute__((format(printf, 2, 3)));
int snprintf(char *buf, size_t n, const char *fmt, ...) __attribute__((format(printf, 3, 4)));
int vprintf(const char *fmt, va_list ap);
int vsprintf(char *buf, const char *fmt, va_list ap);
int vsnprintf(char *buf, size_t n, const char *fmt, va_list ap);
int puts(const char *s);
int putchar(int c);
// Whole-file helper (max 1 MB). Returns a malloc'd buffer (NUL-terminated) or NULL; *size = byte count.
char *file_read(const char *path, int *size);
#endif
