#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

// Calculator: buttons + keyboard, C-style precedence, doubles, no libc float printf.
static handle_t win, display;
static char expr[128];
static int fresh;

// ---------- doubles without printf %f ----------
static void fmt_double(double v, char *out)
{
    int neg = 0, i, len;
    long whole;
    double frac;
    if (v < 0) { neg = 1; v = -v; }
    if (!(v < 1e15)) { strcpy(out, neg ? "-big" : "big"); return; }
    whole = (long)v;
    frac = v - (double)whole;
    {
        char tmp[32];
        int n = 0;
        if (whole == 0) tmp[n++] = '0';
        else {
            char rev[24];
            int r = 0;
            long w = whole;
            while (w > 0 && r < 23) { rev[r++] = (char)('0' + w % 10); w /= 10; }
            while (r > 0) tmp[n++] = rev[--r];
        }
        tmp[n] = 0;
        i = 0;
        if (neg) out[i++] = '-';
        for (len = 0; tmp[len]; len++) out[i++] = tmp[len];
        if (frac > 0.0000005) {
            int d;
            out[i++] = '.';
            for (d = 0; d < 6; d++) {
                frac *= 10;
                {
                    int digit = (int)frac;
                    out[i++] = (char)('0' + digit);
                    frac -= digit;
                }
            }
            while (i > 0 && out[i - 1] == '0') i--;
            if (i > 0 && out[i - 1] == '.') i--;
        }
        out[i] = 0;
    }
}

// ---------- recursive descent: expr -> term -> factor ----------
static const char *pos;
static int parse_err;

static void skip_spaces(void) { while (*pos == ' ') pos++; }

static double parse_expr(void);

static double parse_number(void)
{
    double v = 0;
    skip_spaces();
    while (*pos >= '0' && *pos <= '9') { v = v * 10 + (*pos - '0'); pos++; }
    if (*pos == '.') {
        double base = 0.1;
        pos++;
        while (*pos >= '0' && *pos <= '9') { v += (*pos - '0') * base; base *= 0.1; pos++; }
    }
    return v;
}

static double parse_factor(void)
{
    double v;
    skip_spaces();
    if (*pos == '+') { pos++; return parse_factor(); }
    if (*pos == '-') { pos++; return -parse_factor(); }
    if (*pos == '(') {
        pos++;
        v = parse_expr();
        skip_spaces();
        if (*pos == ')') pos++;
        else parse_err = 1;
        return v;
    }
    if ((*pos >= '0' && *pos <= '9') || *pos == '.') return parse_number();
    parse_err = 1;
    return 0;
}

static double parse_term(void)
{
    double v = parse_factor();
    for (;;) {
        skip_spaces();
        if (*pos == '*') { pos++; v *= parse_factor(); }
        else if (*pos == '/') {
            pos++;
            {
                double d = parse_factor();
                if (d == 0) { parse_err = 1; return 0; }
                v /= d;
            }
        }
        else if (*pos == '%') {
            pos++;
            {
                long a = (long)v, b;
                double d = parse_factor();
                if (d == 0) { parse_err = 1; return 0; }
                b = (long)d;
                if ((double)b != d) { parse_err = 1; return 0; }
                v = (double)(a % b);
            }
        }
        else return v;
    }
}

static double parse_expr(void)
{
    double v = parse_term();
    for (;;) {
        skip_spaces();
        if (*pos == '+') { pos++; v += parse_term(); }
        else if (*pos == '-') { pos++; v -= parse_term(); }
        else return v;
    }
}

static int eval(const char *s, double *out)
{
    pos = s;
    parse_err = 0;
    *out = parse_expr();
    skip_spaces();
    if (parse_err || *pos != 0) return 0;
    return 1;
}

// ---------- ui ----------

static void show(const char *s) { k_set_text(display, s); }

static void press_key(char ch)
{
    char s[64];
    double v;
    if (ch == 'C') { expr[0] = 0; fresh = 0; show(""); return; }
    if (ch == '<') {
        int n = (int)strlen(expr);
        if (n > 0) expr[n - 1] = 0;
        fresh = 0;
        show(expr);
        return;
    }
    if (ch == '=') {
        if (!expr[0]) return;
        if (eval(expr, &v)) {
            fmt_double(v, s);
            strncpy(expr, s, sizeof expr - 1);
            fresh = 1;
            show(expr);
        } else show("Error");
        return;
    }
    if (fresh) { expr[0] = 0; fresh = 0; }
    {
        int n = (int)strlen(expr);
        if (n < (int)sizeof expr - 2) { expr[n] = ch; expr[n + 1] = 0; }
    }
    show(expr);
}

int main(int argc, char **argv)
{
    static const char *labels[] = {
        "C", "(", ")", "/",
        "7", "8", "9", "*",
        "4", "5", "6", "-",
        "1", "2", "3", "+",
        "0", ".", "<", "="
    };
    static const char keys[] = "C()/789*456-123+0.<=";
    handle_t btns[20];
    int i;
    (void)argc; (void)argv;

    win = k_create_window(280, 380, "Calculator");
    if (!win) { puts("window creation failed"); return 1; }
    display = k_create_label(win, 8, 8, "");
    if (!display) { puts("label failed"); k_close(win); return 1; }
    for (i = 0; i < 20; i++) {
        int col = i % 4, row = i / 4;
        btns[i] = k_create_button(win, 8 + col * 66, 56 + row * 60, 60, 52, labels[i]);
        if (!btns[i]) { puts("button failed"); k_close(win); return 1; }
    }

    expr[0] = 0;
    for (;;) {
        Event e;
        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE && e.handle == win) { k_close(win); return 0; }
            if (e.type == EV_KEY) {
                char ch = (char)(e.b & 0xFFFF);
                if (ch == 27) { k_close(win); return 0; }
                if (ch == '\r') ch = '=';
                if ((ch >= '0' && ch <= '9') || ch == '.' || ch == '+' || ch == '-' ||
                    ch == '*' || ch == '/' || ch == '%' || ch == '(' || ch == ')' ||
                    ch == '=' || ch == 'C' || ch == '\b')
                    press_key(ch == '\b' ? '<' : ch);
                continue;
            }
            if (e.type != EV_CLICK) continue;
            for (i = 0; i < 20; i++)
                if (e.handle == btns[i]) { press_key(keys[i]); break; }
        }
        k_sleep(10);
    }
}
