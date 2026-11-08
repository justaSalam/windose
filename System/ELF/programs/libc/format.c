// format.c - printf and sscanf engines.
// Supports: %d %i %u %x %X %o %c %s %p %f %F %g %G %e %E %%, flags - + space 0 #,
// width, .precision, * for both, length hh h l ll z t j.
#include <stddef.h>
#include <stdarg.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include <math.h>

typedef struct { char *buf; size_t cap; size_t len; } Out;

static void put(Out *o, char c) { if (o->len + 1 < o->cap) o->buf[o->len] = c; o->len++; }
static void pad(Out *o, char c, int n) { while (n-- > 0) put(o, c); }

static void fmt_int(Out *o, unsigned long long v, int neg, int base, int upper,
                    int width, int prec, int left, int zero, int plus, int space, int alt)
{
    char tmp[32];
    int n = 0;
    const char *digs = upper ? "0123456789ABCDEF" : "0123456789abcdef";
    if (v == 0 && prec == 0) { /* no digits */ }
    else if (v == 0) tmp[n++] = '0';
    else while (v) { tmp[n++] = digs[v % (unsigned)base]; v /= (unsigned)base; }

    char pre[3]; int pl = 0;
    if (neg) pre[pl++] = '-';
    else if (plus) pre[pl++] = '+';
    else if (space) pre[pl++] = ' ';
    int zeros = prec > n ? prec - n : 0;
    if (alt) {
        if (base == 16 && n > 0 && !(n == 1 && tmp[0] == '0')) { pre[pl++] = '0'; pre[pl++] = upper ? 'X' : 'x'; }
        else if (base == 8 && zeros == 0 && (n == 0 || tmp[n - 1] != '0')) zeros = 1;
    }
    if (zero && !left && prec < 0) {
        int total = pl + zeros + n;
        if (width > total) zeros += width - total;
    }
    int total = pl + zeros + n;
    if (!left) pad(o, ' ', width - total);
    for (int i = 0; i < pl; i++) put(o, pre[i]);
    pad(o, '0', zeros);
    while (n) put(o, tmp[--n]);
    if (left) pad(o, ' ', width - total);
}

static void fmt_float(Out *o, double v, char spec, int width, int prec, int left, int plus, int space, int alt)
{
    if (isnan(v)) {
        const char *s = (spec >= 'A' && spec <= 'Z') ? "NAN" : "nan";
        int len = 3;
        if (!left) pad(o, ' ', width - len);
        for (int i = 0; i < len; i++) put(o, s[i]);
        if (left) pad(o, ' ', width - len);
        return;
    }
    int neg = signbit(v) != 0;
    if (neg) v = -v;
    if (isinf(v)) {
        const char *s = (spec >= 'A' && spec <= 'Z') ? "INF" : "inf";
        int len = 3 + (neg || plus || space ? 1 : 0);
        if (!left) pad(o, ' ', width - len);
        if (neg) put(o, '-');
        else if (plus) put(o, '+');
        else if (space) put(o, ' ');
        for (int i = 0; i < 3; i++) put(o, s[i]);
        if (left) pad(o, ' ', width - len);
        return;
    }
    if (prec < 0) prec = 6;

    double round_adder = 0.5;
    for (int i = 0; i < prec; i++) round_adder /= 10.0;
    v += round_adder;

    unsigned long long int_part = (unsigned long long)v;
    double frac_part = v - (double)int_part;

    char buf[64];
    int pos = 0;
    if (prec > 0 || alt) {
        char frac_buf[32];
        for (int i = 0; i < prec; i++) {
            frac_part *= 10.0;
            int digit = (int)frac_part;
            if (digit > 9) digit = 9;
            frac_buf[i] = (char)('0' + digit);
            frac_part -= (double)digit;
        }
        if (spec == 'g' || spec == 'G') {
            if (!alt) {
                while (prec > 0 && frac_buf[prec - 1] == '0') prec--;
            }
        }
        if (prec > 0 || alt) {
            for (int i = prec - 1; i >= 0; i--) buf[pos++] = frac_buf[i];
            buf[pos++] = '.';
        }
    }

    if (int_part == 0) {
        buf[pos++] = '0';
    } else {
        while (int_part > 0) {
            buf[pos++] = (char)('0' + (int_part % 10));
            int_part /= 10;
        }
    }

    int prefix_len = neg ? 1 : (plus || space ? 1 : 0);
    int total = prefix_len + pos;
    if (!left) pad(o, ' ', width - total);
    if (neg) put(o, '-');
    else if (plus) put(o, '+');
    else if (space) put(o, ' ');
    while (pos > 0) put(o, buf[--pos]);
    if (left) pad(o, ' ', width - total);
}

int __vformat(char *buf, size_t cap, const char *fmt, va_list ap)
{
    Out o; o.buf = buf; o.cap = cap; o.len = 0;

    for (; *fmt; fmt++) {
        if (*fmt != '%') { put(&o, *fmt); continue; }
        fmt++;
        int left = 0, plus = 0, space = 0, zero = 0, alt = 0;
        for (;; fmt++) {
            if (*fmt == '-') left = 1;
            else if (*fmt == '+') plus = 1;
            else if (*fmt == ' ') space = 1;
            else if (*fmt == '0') zero = 1;
            else if (*fmt == '#') alt = 1;
            else break;
        }
        int width = 0, prec = -1;
        if (*fmt == '*') { width = va_arg(ap, int); if (width < 0) { left = 1; width = -width; } fmt++; }
        else while (*fmt >= '0' && *fmt <= '9') width = width * 10 + (*fmt++ - '0');
        if (*fmt == '.') {
            fmt++; prec = 0;
            if (*fmt == '*') { prec = va_arg(ap, int); fmt++; }
            else while (*fmt >= '0' && *fmt <= '9') prec = prec * 10 + (*fmt++ - '0');
        }
        int lng = 0;   // 0 int, -1 short, -2 char, 1 long, 2 long long (z/t/j are 64-bit => 2)
        for (;; fmt++) {
            if (*fmt == 'h') lng = lng == -1 ? -2 : -1;
            else if (*fmt == 'l') lng = lng == 1 ? 2 : 1;
            else if (*fmt == 'z' || *fmt == 't' || *fmt == 'j') lng = 2;
            else break;
        }
        char c = *fmt;
        if (!c) break;
        switch (c) {
        case '%': put(&o, '%'); break;
        case 'c': {
            char ch = (char)va_arg(ap, int);
            if (!left) pad(&o, ' ', width - 1);
            put(&o, ch);
            if (left) pad(&o, ' ', width - 1);
            break; }
        case 's': {
            const char *s = va_arg(ap, const char *);
            if (!s) s = "(null)";
            int len = 0;
            while (s[len] && (prec < 0 || len < prec)) len++;
            if (!left) pad(&o, ' ', width - len);
            for (int i = 0; i < len; i++) put(&o, s[i]);
            if (left) pad(&o, ' ', width - len);
            break; }
        case 'd': case 'i': {
            long long v;
            if (lng >= 1) v = lng == 1 ? va_arg(ap, long) : va_arg(ap, long long);
            else { v = va_arg(ap, int); if (lng == -1) v = (short)v; else if (lng == -2) v = (signed char)v; }
            int neg = v < 0;
            unsigned long long u = neg ? 0ULL - (unsigned long long)v : (unsigned long long)v;
            fmt_int(&o, u, neg, 10, 0, width, prec, left, zero, plus, space, 0);
            break; }
        case 'u': case 'x': case 'X': case 'o': {
            unsigned long long u;
            if (lng >= 1) u = lng == 1 ? va_arg(ap, unsigned long) : va_arg(ap, unsigned long long);
            else { u = va_arg(ap, unsigned); if (lng == -1) u = (unsigned short)u; else if (lng == -2) u = (unsigned char)u; }
            int base = c == 'u' ? 10 : c == 'o' ? 8 : 16;
            fmt_int(&o, u, 0, base, c == 'X', width, prec, left, zero, 0, 0, alt);
            break; }
        case 'p': {
            void *p = va_arg(ap, void *);
            if (!p) {
                const char *s = "(nil)"; int len = 5;
                if (!left) pad(&o, ' ', width - len);
                for (int i = 0; i < len; i++) put(&o, s[i]);
                if (left) pad(&o, ' ', width - len);
            } else fmt_int(&o, (unsigned long long)(size_t)p, 0, 16, 0, width, prec, left, zero, 0, 0, 1);
            break; }
        case 'f': case 'F': case 'e': case 'E': case 'g': case 'G': case 'a': case 'A': {
            double v = va_arg(ap, double);
            fmt_float(&o, v, c, width, prec, left, plus, space, alt);
            break; }
        default:
            put(&o, '%'); put(&o, c);
            break;
        }
    }
    if (cap > 0) o.buf[o.len < cap - 1 ? o.len : cap - 1] = 0;
    return (int)o.len;
}

int vsscanf(const char *s, const char *fmt, va_list ap)
{
    int assigned = 0;
    const char *orig_s = s;

    while (*fmt) {
        if (isspace((unsigned char)*fmt)) {
            while (isspace((unsigned char)*fmt)) fmt++;
            while (isspace((unsigned char)*s)) s++;
            continue;
        }
        if (*fmt != '%') {
            if (*s != *fmt) break;
            s++;
            fmt++;
            continue;
        }
        fmt++;
        if (*fmt == '%') {
            if (*s != '%') break;
            s++;
            fmt++;
            continue;
        }

        int suppress = 0;
        if (*fmt == '*') { suppress = 1; fmt++; }

        int width = 0;
        while (isdigit((unsigned char)*fmt)) {
            width = width * 10 + (*fmt++ - '0');
        }

        int lng = 0;
        if (*fmt == 'h') {
            fmt++;
            if (*fmt == 'h') { lng = -2; fmt++; }
            else lng = -1;
        } else if (*fmt == 'l') {
            fmt++;
            if (*fmt == 'l') { lng = 2; fmt++; }
            else lng = 1;
        } else if (*fmt == 'L') {
            lng = 2; fmt++;
        }

        char conv = *fmt++;
        if (!conv) break;

        if (conv == 'n') {
            if (!suppress) {
                int *p = va_arg(ap, int *);
                if (p) *p = (int)(s - orig_s);
            }
            continue;
        }

        if (conv == 'c') {
            if (!*s) break;
            int n = (width > 0) ? width : 1;
            char *p = suppress ? NULL : va_arg(ap, char *);
            for (int i = 0; i < n && *s; i++) {
                if (p) p[i] = *s;
                s++;
            }
            if (!suppress) assigned++;
            continue;
        }

        while (isspace((unsigned char)*s)) s++;
        if (!*s) break;

        if (conv == 's') {
            char *p = suppress ? NULL : va_arg(ap, char *);
            int count = 0;
            while (*s && !isspace((unsigned char)*s)) {
                if (width > 0 && count >= width) break;
                if (p) p[count] = *s;
                s++;
                count++;
            }
            if (count == 0) break;
            if (p) p[count] = '\0';
            if (!suppress) assigned++;
            continue;
        }

        if (conv == 'd' || conv == 'i' || conv == 'u' || conv == 'x' || conv == 'X' || conv == 'o' || conv == 'p') {
            int base = 10;
            if (conv == 'i') base = 0;
            else if (conv == 'x' || conv == 'X' || conv == 'p') base = 16;
            else if (conv == 'o') base = 8;

            char tmp[64];
            int max_w = (width > 0 && width < (int)sizeof(tmp) - 1) ? width : (int)sizeof(tmp) - 1;
            int ti = 0;
            const char *sp = s;
            if (*sp == '-' || *sp == '+') { tmp[ti++] = *sp++; }
            while (*sp && ti < max_w) {
                if (isxdigit((unsigned char)*sp) || *sp == 'x' || *sp == 'X') {
                    tmp[ti++] = *sp++;
                } else break;
            }
            tmp[ti] = '\0';
            char *endp = NULL;
            if (conv == 'd' || conv == 'i') {
                long long v = strtoll(tmp, &endp, base);
                if (endp == tmp) break;
                s += (endp - tmp);
                if (!suppress) {
                    if (lng == -2) *(signed char *)va_arg(ap, void *) = (signed char)v;
                    else if (lng == -1) *(short *)va_arg(ap, void *) = (short)v;
                    else if (lng == 1) *(long *)va_arg(ap, void *) = (long)v;
                    else if (lng == 2) *(long long *)va_arg(ap, void *) = v;
                    else *(int *)va_arg(ap, void *) = (int)v;
                    assigned++;
                }
            } else {
                unsigned long long u = strtoull(tmp, &endp, base);
                if (endp == tmp) break;
                s += (endp - tmp);
                if (!suppress) {
                    if (conv == 'p') *(void **)va_arg(ap, void **) = (void *)(size_t)u;
                    else if (lng == -2) *(unsigned char *)va_arg(ap, void *) = (unsigned char)u;
                    else if (lng == -1) *(unsigned short *)va_arg(ap, void *) = (unsigned short)u;
                    else if (lng == 1) *(unsigned long *)va_arg(ap, void *) = (unsigned long)u;
                    else if (lng == 2) *(unsigned long long *)va_arg(ap, void *) = u;
                    else *(unsigned int *)va_arg(ap, void *) = (unsigned int)u;
                    assigned++;
                }
            }
            continue;
        }

        if (conv == 'f' || conv == 'F' || conv == 'e' || conv == 'E' || conv == 'g' || conv == 'G') {
            char tmp[64];
            int max_w = (width > 0 && width < (int)sizeof(tmp) - 1) ? width : (int)sizeof(tmp) - 1;
            int ti = 0;
            const char *sp = s;
            while (*sp && !isspace((unsigned char)*sp) && ti < max_w) {
                tmp[ti++] = *sp++;
            }
            tmp[ti] = '\0';
            char *endp = NULL;
            double v = strtod(tmp, &endp);
            if (endp == tmp) break;
            s += (endp - tmp);
            if (!suppress) {
                if (lng >= 1) *(double *)va_arg(ap, void *) = v;
                else *(float *)va_arg(ap, void *) = (float)v;
                assigned++;
            }
            continue;
        }
    }

    if (assigned == 0 && *s == '\0') return -1;
    return assigned;
}

int sscanf(const char *s, const char *fmt, ...)
{
    va_list ap;
    va_start(ap, fmt);
    int r = vsscanf(s, fmt, ap);
    va_end(ap);
    return r;
}
