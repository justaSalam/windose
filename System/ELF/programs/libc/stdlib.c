#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include <errno.h>
#include <math.h>
#include <stdio.h>
#include "kernel.h"

int errno = 0;

void __assert_fail(const char *expr, const char *file, int line, const char *func)
{
    char buf[256];
    snprintf(buf, sizeof(buf), "Assertion failed: (%s), function %s, file %s, line %d.\n",
             expr, func ? func : "unknown", file, line);
    k_write(buf, (int)strlen(buf));
    abort();
}

int abs(int v) { return v < 0 ? -v : v; }
long labs(long v) { return v < 0 ? -v : v; }
long long llabs(long long v) { return v < 0 ? -v : v; }

div_t div(int numer, int denom)
{
    div_t r;
    r.quot = numer / denom;
    r.rem = numer % denom;
    return r;
}

ldiv_t ldiv(long numer, long denom)
{
    ldiv_t r;
    r.quot = numer / denom;
    r.rem = numer % denom;
    return r;
}

lldiv_t lldiv(long long numer, long long denom)
{
    lldiv_t r;
    r.quot = numer / denom;
    r.rem = numer % denom;
    return r;
}

static unsigned long long parse_u64(const char *s, char **end, int base, int *neg, int *any, int *overflow)
{
    while (isspace((unsigned char)*s)) s++;
    *neg = 0; *any = 0; *overflow = 0;
    if (*s == '-') { *neg = 1; s++; } else if (*s == '+') s++;
    if ((base == 0 || base == 16) && s[0] == '0' && (s[1] == 'x' || s[1] == 'X') && isxdigit((unsigned char)s[2])) {
        s += 2; base = 16;
    } else if (base == 0 && s[0] == '0') {
        base = 8;
    } else if (base == 0) {
        base = 10;
    }
    unsigned long long v = 0;
    unsigned long long cutoff = 0xFFFFFFFFFFFFFFFFULL / (unsigned long long)base;
    int cutlim = (int)(0xFFFFFFFFFFFFFFFFULL % (unsigned long long)base);

    for (;; s++) {
        int d;
        if (isdigit((unsigned char)*s)) d = *s - '0';
        else if (isalpha((unsigned char)*s)) d = tolower((unsigned char)*s) - 'a' + 10;
        else break;
        if (d >= base) break;
        if (v > cutoff || (v == cutoff && d > cutlim)) {
            *overflow = 1;
        } else {
            v = v * (unsigned long long)base + (unsigned long long)d;
        }
        *any = 1;
    }
    if (end) *end = (char *)(*any ? s : (const char *)0);
    return v;
}

long long strtoll(const char *s, char **end, int base)
{
    int neg, any, overflow; const char *start = s;
    unsigned long long v = parse_u64(s, end, base, &neg, &any, &overflow);
    if (!any && end) *end = (char *)start;
    if (overflow || (!neg && v > 0x7FFFFFFFFFFFFFFFULL)) {
        errno = ERANGE;
        return 0x7FFFFFFFFFFFFFFLL;
    }
    if (overflow || (neg && v > 0x8000000000000000ULL)) {
        errno = ERANGE;
        return (-0x7FFFFFFFFFFFFFFLL - 1LL);
    }
    return neg ? -(long long)v : (long long)v;
}

unsigned long long strtoull(const char *s, char **end, int base)
{
    int neg, any, overflow; const char *start = s;
    unsigned long long v = parse_u64(s, end, base, &neg, &any, &overflow);
    if (!any && end) *end = (char *)start;
    if (overflow) {
        errno = ERANGE;
        return 0xFFFFFFFFFFFFFFFFULL;
    }
    return neg ? 0ULL - v : v;
}

long strtol(const char *s, char **end, int base)
{
    return (long)strtoll(s, end, base);
}

unsigned long strtoul(const char *s, char **end, int base)
{
    return (unsigned long)strtoull(s, end, base);
}

int atoi(const char *s) { return (int)strtoll(s, NULL, 10); }
long atol(const char *s) { return (long)strtoll(s, NULL, 10); }
long long atoll(const char *s) { return strtoll(s, NULL, 10); }

double strtod(const char *s, char **end)
{
    const char *start = s;
    while (isspace((unsigned char)*s)) s++;
    int neg = 0;
    if (*s == '-') { neg = 1; s++; }
    else if (*s == '+') { s++; }

    if ((s[0] == 'i' || s[0] == 'I') && (s[1] == 'n' || s[1] == 'N') && (s[2] == 'f' || s[2] == 'F')) {
        s += 3;
        if ((s[0] == 'i' || s[0] == 'I') && (s[1] == 'n' || s[1] == 'N') && (s[2] == 'i' || s[2] == 'I') &&
            (s[3] == 't' || s[3] == 'T') && (s[4] == 'y' || s[4] == 'Y')) {
            s += 5;
        }
        if (end) *end = (char *)s;
        return neg ? -INFINITY : INFINITY;
    }
    if ((s[0] == 'n' || s[0] == 'N') && (s[1] == 'a' || s[1] == 'A') && (s[2] == 'n' || s[2] == 'N')) {
        s += 3;
        if (end) *end = (char *)s;
        return NAN;
    }

    int digits = 0;
    double val = 0.0;
    while (isdigit((unsigned char)*s)) {
        val = val * 10.0 + (*s - '0');
        s++;
        digits++;
    }
    if (*s == '.') {
        s++;
        double frac = 0.1;
        while (isdigit((unsigned char)*s)) {
            val += (*s - '0') * frac;
            frac *= 0.1;
            s++;
            digits++;
        }
    }
    if (digits == 0) {
        if (end) *end = (char *)start;
        return 0.0;
    }
    if (*s == 'e' || *s == 'E') {
        const char *ep = s + 1;
        int exp_neg = 0;
        if (*ep == '-') { exp_neg = 1; ep++; }
        else if (*ep == '+') { ep++; }
        if (isdigit((unsigned char)*ep)) {
            int exp = 0;
            while (isdigit((unsigned char)*ep)) {
                exp = exp * 10 + (*ep - '0');
                ep++;
            }
            s = ep;
            double p = 1.0;
            double base = 10.0;
            while (exp > 0) {
                if (exp & 1) p *= base;
                base *= base;
                exp >>= 1;
            }
            if (exp_neg) val /= p;
            else val *= p;
        }
    }
    if (end) *end = (char *)s;
    return neg ? -val : val;
}

float strtof(const char *s, char **end) { return (float)strtod(s, end); }
long double strtold(const char *s, char **end) { return (long double)strtod(s, end); }
double atof(const char *s) { return strtod(s, NULL); }

char *lltoa(long long val, char *buf, int radix)
{
    if (radix < 2 || radix > 36 || !buf) return NULL;
    char *p = buf;
    unsigned long long uval;
    if (val < 0 && radix == 10) {
        *p++ = '-';
        uval = (unsigned long long)(-val);
    } else {
        uval = (unsigned long long)val;
    }
    char tmp[66];
    int pos = 0;
    static const char digits[] = "0123456789abcdefghijklmnopqrstuvwxyz";
    if (uval == 0) {
        tmp[pos++] = '0';
    } else {
        while (uval > 0) {
            tmp[pos++] = digits[uval % (unsigned long long)radix];
            uval /= (unsigned long long)radix;
        }
    }
    while (pos > 0) {
        *p++ = tmp[--pos];
    }
    *p = '\0';
    return buf;
}

char *itoa(int val, char *buf, int radix) { return lltoa((long long)val, buf, radix); }
char *ltoa(long val, char *buf, int radix) { return lltoa((long long)val, buf, radix); }

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

int system(const char *command)
{
    if (!command) return 1;
    while (isspace((unsigned char)*command)) command++;
    if (!*command) return 0;

    char cmd[256];
    char argbuf[512];
    int arglen = 0;

    const char *p = command;
    int ci = 0;
    while (*p && !isspace((unsigned char)*p) && ci < (int)sizeof(cmd) - 1) {
        cmd[ci++] = *p++;
    }
    cmd[ci] = '\0';

    int clen = (int)strlen(cmd);
    if (arglen + clen + 1 < (int)sizeof(argbuf)) {
        memcpy(argbuf + arglen, cmd, clen + 1);
        arglen += clen + 1;
    }

    while (*p) {
        while (isspace((unsigned char)*p)) p++;
        if (!*p) break;
        int start = arglen;
        while (*p && !isspace((unsigned char)*p) && arglen < (int)sizeof(argbuf) - 2) {
            argbuf[arglen++] = *p++;
        }
        argbuf[arglen++] = '\0';
        (void)start;
    }

    int child = k_spawn(cmd, argbuf, arglen);
    if (child < 0 && cmd[0] != '/') {
        char fallback[300];
        if (strstr(cmd, ".elf")) {
            snprintf(fallback, sizeof(fallback), "/mnt/System/programs/%s", cmd);
        } else {
            snprintf(fallback, sizeof(fallback), "/mnt/System/programs/%s.elf", cmd);
        }
        child = k_spawn(fallback, argbuf, arglen);
    }

    if (child < 0) return -1;
    int exit_code = 0;
    k_wait_pid(child, &exit_code, -1);
    return exit_code;
}

int mblen(const char *s, size_t n)
{
    if (!s) return 0;
    if (n == 0) return -1;
    if (*s == '\0') return 0;
    unsigned char c = (unsigned char)*s;
    int len = 1;
    if (c < 0x80) len = 1;
    else if ((c & 0xE0) == 0xC0) len = 2;
    else if ((c & 0xF0) == 0xE0) len = 3;
    else if ((c & 0xF8) == 0xF0) len = 4;
    else { errno = EILSEQ; return -1; }

    if ((size_t)len > n) return -1;
    for (int i = 1; i < len; i++) {
        if (((unsigned char)s[i] & 0xC0) != 0x80) { errno = EILSEQ; return -1; }
    }
    return len;
}

int mbtowc(wchar_t *pwc, const char *s, size_t n)
{
    if (!s) return 0;
    int len = mblen(s, n);
    if (len <= 0) return len;
    if (pwc) {
        unsigned char c = (unsigned char)*s;
        wchar_t wc = 0;
        if (len == 1) {
            wc = c;
        } else if (len == 2) {
            wc = ((c & 0x1F) << 6) | ((unsigned char)s[1] & 0x3F);
        } else if (len == 3) {
            wc = ((c & 0x0F) << 12) | (((unsigned char)s[1] & 0x3F) << 6) | ((unsigned char)s[2] & 0x3F);
        } else if (len == 4) {
            wc = ((c & 0x07) << 18) | (((unsigned char)s[1] & 0x3F) << 12) | (((unsigned char)s[2] & 0x3F) << 6) | ((unsigned char)s[3] & 0x3F);
        }
        *pwc = wc;
    }
    return len;
}

int wctomb(char *s, wchar_t wc)
{
    if (!s) return 0;
    if (wc < 0 || wc > 0x10FFFF || (wc >= 0xD800 && wc <= 0xDFFF)) {
        errno = EILSEQ;
        return -1;
    }
    if (wc <= 0x7F) {
        s[0] = (char)wc;
        return 1;
    } else if (wc <= 0x7FF) {
        s[0] = (char)(0xC0 | ((wc >> 6) & 0x1F));
        s[1] = (char)(0x80 | (wc & 0x3F));
        return 2;
    } else if (wc <= 0xFFFF) {
        s[0] = (char)(0xE0 | ((wc >> 12) & 0x0F));
        s[1] = (char)(0x80 | ((wc >> 6) & 0x3F));
        s[2] = (char)(0x80 | (wc & 0x3F));
        return 3;
    } else {
        s[0] = (char)(0xF0 | ((wc >> 18) & 0x07));
        s[1] = (char)(0x80 | ((wc >> 12) & 0x3F));
        s[2] = (char)(0x80 | ((wc >> 6) & 0x3F));
        s[3] = (char)(0x80 | (wc & 0x3F));
        return 4;
    }
}

size_t mbstowcs(wchar_t *pwcs, const char *s, size_t n)
{
    if (!s) return 0;
    size_t count = 0;
    while (*s) {
        wchar_t wc;
        int len = mbtowc(&wc, s, 4);
        if (len < 0) {
            errno = EILSEQ;
            return (size_t)-1;
        }
        if (pwcs && count < n) {
            pwcs[count] = wc;
        }
        s += len;
        count++;
    }
    if (pwcs && count < n) {
        pwcs[count] = 0;
    }
    return count;
}

size_t wcstombs(char *s, const wchar_t *pwcs, size_t n)
{
    if (!pwcs) return 0;
    size_t total = 0;
    char tmp[4];
    while (*pwcs) {
        int len = wctomb(tmp, *pwcs++);
        if (len < 0) {
            errno = EILSEQ;
            return (size_t)-1;
        }
        if (s) {
            if (total + (size_t)len > n) break;
            for (int i = 0; i < len; i++) s[total + i] = tmp[i];
        }
        total += (size_t)len;
    }
    if (s && total < n) s[total] = '\0';
    return total;
}
