#include <string.h>
#include <stdlib.h>
#include <ctype.h>
#include <errno.h>

void *memcpy(void *d, const void *s, size_t n)
{ unsigned char *a = d; const unsigned char *b = s; while (n--) *a++ = *b++; return d; }

void *memmove(void *d, const void *s, size_t n)
{
    unsigned char *a = d; const unsigned char *b = s;
    if (a == b || n == 0) return d;
    if (a < b) while (n--) *a++ = *b++;
    else { a += n; b += n; while (n--) *--a = *--b; }
    return d;
}

void *memset(void *d, int c, size_t n)
{ unsigned char *a = d; while (n--) *a++ = (unsigned char)c; return d; }

int memcmp(const void *x, const void *y, size_t n)
{
    const unsigned char *a = x, *b = y;
    for (; n; n--, a++, b++) if (*a != *b) return *a - *b;
    return 0;
}

void *memchr(const void *s, int c, size_t n)
{
    const unsigned char *p = s;
    for (; n; n--, p++) if (*p == (unsigned char)c) return (void *)p;
    return NULL;
}

void *memccpy(void *d, const void *s, int c, size_t n)
{
    unsigned char *a = d;
    const unsigned char *b = s;
    unsigned char uc = (unsigned char)c;
    while (n--) {
        *a++ = *b;
        if (*b++ == uc) return a;
    }
    return NULL;
}

size_t strlen(const char *s) { size_t n = 0; while (s[n]) n++; return n; }
size_t strnlen(const char *s, size_t max) { size_t n = 0; while (n < max && s[n]) n++; return n; }

int strcmp(const char *a, const char *b)
{ while (*a && *a == *b) { a++; b++; } return (unsigned char)*a - (unsigned char)*b; }

int strncmp(const char *a, const char *b, size_t n)
{
    for (; n; n--, a++, b++) {
        if (*a != *b) return (unsigned char)*a - (unsigned char)*b;
        if (!*a) return 0;
    }
    return 0;
}

int strcasecmp(const char *s1, const char *s2)
{
    while (*s1 && tolower((unsigned char)*s1) == tolower((unsigned char)*s2)) {
        s1++;
        s2++;
    }
    return tolower((unsigned char)*s1) - tolower((unsigned char)*s2);
}

int strncasecmp(const char *s1, const char *s2, size_t n)
{
    for (; n; n--, s1++, s2++) {
        if (tolower((unsigned char)*s1) != tolower((unsigned char)*s2)) {
            return tolower((unsigned char)*s1) - tolower((unsigned char)*s2);
        }
        if (!*s1) return 0;
    }
    return 0;
}

char *strcpy(char *d, const char *s) { char *r = d; while ((*d++ = *s++)) {} return r; }

char *strncpy(char *d, const char *s, size_t n)
{ size_t i = 0; for (; i < n && s[i]; i++) d[i] = s[i]; for (; i < n; i++) d[i] = 0; return d; }

char *strcat(char *d, const char *s) { strcpy(d + strlen(d), s); return d; }

char *strncat(char *d, const char *s, size_t n)
{ char *e = d + strlen(d); size_t i = 0; for (; i < n && s[i]; i++) e[i] = s[i]; e[i] = 0; return d; }

char *strchr(const char *s, int c)
{ for (;; s++) { if (*s == (char)c) return (char *)s; if (!*s) return NULL; } }

char *strrchr(const char *s, int c)
{ const char *r = NULL; for (;; s++) { if (*s == (char)c) r = s; if (!*s) return (char *)r; } }

char *strstr(const char *h, const char *n)
{
    size_t nl = strlen(n);
    if (nl == 0) return (char *)h;
    for (; *h; h++) if (*h == *n && strncmp(h, n, nl) == 0) return (char *)h;
    return NULL;
}

char *strdup(const char *s)
{ size_t n = strlen(s) + 1; char *p = malloc(n); if (p) memcpy(p, s, n); return p; }

char *strndup(const char *s, size_t n)
{
    size_t len = strnlen(s, n);
    char *p = malloc(len + 1);
    if (p) {
        memcpy(p, s, len);
        p[len] = '\0';
    }
    return p;
}

size_t strspn(const char *s, const char *accept)
{
    size_t count = 0;
    while (*s && strchr(accept, *s)) {
        count++;
        s++;
    }
    return count;
}

size_t strcspn(const char *s, const char *reject)
{
    size_t count = 0;
    while (*s && !strchr(reject, *s)) {
        count++;
        s++;
    }
    return count;
}

char *strpbrk(const char *s, const char *accept)
{
    while (*s) {
        if (strchr(accept, *s)) return (char *)s;
        s++;
    }
    return NULL;
}

char *strtok_r(char *s, const char *delim, char **saveptr)
{
    if (!s) s = *saveptr;
    if (!s) return NULL;

    s += strspn(s, delim);
    if (!*s) {
        *saveptr = NULL;
        return NULL;
    }

    char *token = s;
    s = strpbrk(token, delim);
    if (!s) {
        *saveptr = NULL;
    } else {
        *s = '\0';
        *saveptr = s + 1;
    }
    return token;
}

static char *__strtok_last = NULL;
char *strtok(char *s, const char *delim)
{
    return strtok_r(s, delim, &__strtok_last);
}

char *strerror(int errnum)
{
    switch (errnum) {
    case 0: return "Success";
    case EPERM: return "Operation not permitted";
    case ENOENT: return "No such file or directory";
    case ESRCH: return "No such process";
    case EINTR: return "Interrupted system call";
    case EIO: return "Input/output error";
    case ENXIO: return "No such device or address";
    case E2BIG: return "Argument list too long";
    case ENOEXEC: return "Exec format error";
    case EBADF: return "Bad file descriptor";
    case ECHILD: return "No child processes";
    case EAGAIN: return "Resource temporarily unavailable";
    case ENOMEM: return "Cannot allocate memory";
    case EACCES: return "Permission denied";
    case EFAULT: return "Bad address";
    case EBUSY: return "Device or resource busy";
    case EEXIST: return "File exists";
    case EXDEV: return "Invalid cross-device link";
    case ENODEV: return "No such device";
    case ENOTDIR: return "Not a directory";
    case EISDIR: return "Is a directory";
    case EINVAL: return "Invalid argument";
    case ENFILE: return "Too many open files in system";
    case EMFILE: return "Too many open files";
    case ENOTTY: return "Inappropriate ioctl for device";
    case EFBIG: return "File too large";
    case ENOSPC: return "No space left on device";
    case ESPIPE: return "Illegal seek";
    case EROFS: return "Read-only file system";
    case EMLINK: return "Too many links";
    case EPIPE: return "Broken pipe";
    case EDOM: return "Numerical argument out of domain";
    case ERANGE: return "Numerical result out of range";
    case ENAMETOOLONG: return "File name too long";
    case ENOSYS: return "Function not implemented";
    case ENOTEMPTY: return "Directory not empty";
    case EOVERFLOW: return "Value too large for defined data type";
    case EILSEQ: return "Invalid or incomplete multibyte or wide character";
    default: return "Unknown error";
    }
}
