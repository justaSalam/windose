#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include "kernel.h"

int abs(int v) { return v < 0 ? -v : v; }
long labs(long v) { return v < 0 ? -v : v; }

static unsigned long parse(const char *s, char **end, int base, int *neg, int *any)
{
    while (isspace((unsigned char)*s)) s++;
    *neg = 0; *any = 0;
    if (*s == '-') { *neg = 1; s++; } else if (*s == '+') s++;
    if ((base == 0 || base == 16) && s[0] == '0' && (s[1] == 'x' || s[1] == 'X') && isxdigit((unsigned char)s[2])) { s += 2; base = 16; }
    else if (base == 0 && s[0] == '0') base = 8;
    else if (base == 0) base = 10;
    unsigned long v = 0;
    for (;; s++) {
        int d;
        if (isdigit((unsigned char)*s)) d = *s - '0';
        else if (isalpha((unsigned char)*s)) d = tolower((unsigned char)*s) - 'a' + 10;
        else break;
        if (d >= base) break;
        v = v * (unsigned long)base + (unsigned long)d;
        *any = 1;
    }
    if (end) *end = (char *)(*any ? s : (const char *)0);
    return v;
}

long strtol(const char *s, char **end, int base)
{
    int neg, any; const char *start = s;
    unsigned long v = parse(s, end, base, &neg, &any);
    if (!any && end) *end = (char *)start;
    return neg ? -(long)v : (long)v;
}

unsigned long strtoul(const char *s, char **end, int base)
{
    int neg, any; const char *start = s;
    unsigned long v = parse(s, end, base, &neg, &any);
    if (!any && end) *end = (char *)start;
    return neg ? 0UL - v : v;
}

int atoi(const char *s) { return (int)strtol(s, NULL, 10); }
long atol(const char *s) { return strtol(s, NULL, 10); }

// heapsort: no recursion, no allocation
static void swapb(unsigned char *a, unsigned char *b, size_t n)
{ while (n--) { unsigned char t = *a; *a++ = *b; *b++ = t; } }

static void sift(unsigned char *base, size_t size, size_t start, size_t end, int (*cmp)(const void *, const void *))
{
    size_t root = start;
    while (root * 2 + 1 < end) {
        size_t child = root * 2 + 1;
        if (child + 1 < end && cmp(base + child * size, base + (child + 1) * size) < 0) child++;
        if (cmp(base + root * size, base + child * size) < 0) { swapb(base + root * size, base + child * size, size); root = child; }
        else return;
    }
}

void qsort(void *b, size_t n, size_t size, int (*cmp)(const void *, const void *))
{
    unsigned char *base = b;
    if (n < 2 || size == 0) return;
    for (size_t i = n / 2; i-- > 0;) sift(base, size, i, n, cmp);
    for (size_t end = n - 1; end > 0; end--) { swapb(base, base + end * size, size); sift(base, size, 0, end, cmp); }
}

void *bsearch(const void *key, const void *b, size_t n, size_t size, int (*cmp)(const void *, const void *))
{
    const unsigned char *base = b;
    size_t lo = 0, hi = n;
    while (lo < hi) {
        size_t mid = lo + (hi - lo) / 2;
        int c = cmp(key, base + mid * size);
        if (c == 0) return (void *)(base + mid * size);
        if (c < 0) hi = mid; else lo = mid + 1;
    }
    return NULL;
}

static unsigned long next = 1;
int rand(void) { next = next * 1103515245UL + 12345UL; return (int)((next >> 16) & 0x7fff); }
void srand(unsigned seed) { next = seed; }

static char getenv_buf[512];

char *getenv(const char *name)
{
    if (!name || !__k || !__k->get_env) return NULL;
    int len = __k->get_env(__k->pid, name, getenv_buf, (int)sizeof getenv_buf);
    if (len < 0) return NULL;
    return getenv_buf;
}

int setenv(const char *name, const char *value, int overwrite)
{
    if (!name || !__k || !__k->set_env) return -1;
    if (!overwrite) {
        char tmp[2];
        if (__k->get_env && __k->get_env(__k->pid, name, tmp, sizeof tmp) >= 0) return 0;
    }
    return __k->set_env(__k->pid, name, value);
}

int unsetenv(const char *name)
{
    if (!name || !__k || !__k->set_env) return -1;
    return __k->set_env(__k->pid, name, NULL);
}
