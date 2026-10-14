// format.c - printf engine. Depends only on compiler headers so it can be unit-tested on a host.
// Supports: %d %i %u %x %X %o %c %s %p %%, flags - + space 0 #, width, .precision, * for both,
// length hh h l ll z t j. No floating point (%f/%e/%g print the text "<float>").
#include <stddef.h>
#include <stdarg.h>

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
        case 'f': case 'F': case 'e': case 'E': case 'g': case 'G': case 'a': case 'A':
            (void)va_arg(ap, double);
            for (const char *s = "<float>"; *s; s++) put(&o, *s);
            break;
        default:
            put(&o, '%'); put(&o, c);
            break;
        }
    }
    if (cap > 0) o.buf[o.len < cap - 1 ? o.len : cap - 1] = 0;
    return (int)o.len;
}
