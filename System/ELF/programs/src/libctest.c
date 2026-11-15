// Self-test for the libc, relocations (PIE tables), argv, stdlib, math, stdio, string, and time.
// Exits 42 on success, 1 on failure.
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include <stdbool.h>
#include <assert.h>
#include <limits.h>
#include <errno.h>
#include <math.h>
#include <time.h>
#include "kernel.h"

static int fails;
#define CHECK(c) do { if (!(c)) { printf("FAIL line %d: %s\n", __LINE__, #c); fails++; } } while (0)

static int constructor_ran = 0;
__attribute__((constructor)) static void my_init(void)
{
    constructor_ran = 42;
}

static void test_init_array(void)
{
    CHECK(constructor_ran == 42);
}

static void test_core_headers(void)
{
    // stdbool.h
    bool b1 = true;
    bool b2 = false;
    CHECK(b1 && !b2);
    CHECK(sizeof(bool) >= 1);

    // assert.h
    assert(b1);
    assert(1 + 1 == 2);

    // limits.h
    CHECK(CHAR_BIT == 8);
    CHECK(INT_MAX == 2147483647);
    CHECK(LLONG_MAX == 9223372036854775807LL);
    CHECK(ULLONG_MAX == 18446744073709551615ULL);

    // errno.h
    errno = 0;
    CHECK(errno == 0);
    errno = EINVAL;
    CHECK(errno == EINVAL);
    errno = 0;
}

static void test_env_and_clipboard(void)
{
    CHECK(getenv("USER") != NULL && strcmp(getenv("USER"), "root") == 0);
    CHECK(setenv("TEST_KEY", "TEST_VAL", 1) == 0);
    CHECK(getenv("TEST_KEY") != NULL && strcmp(getenv("TEST_KEY"), "TEST_VAL") == 0);

    char clip[64];
    CHECK(k_clipboard_set("WindoseClip", 11) == 0);
    int clen = k_clipboard_get(clip, (int)sizeof clip);
    CHECK(clen == 11 && strcmp(clip, "WindoseClip") == 0);
}

// ---- things that need R_X86_64_RELATIVE relocations in a PIE ----
static int add(int a, int b) { return a + b; }
static int sub(int a, int b) { return a - b; }
static int mul(int a, int b) { return a * b; }
static const char *volatile names[] = { "alpha", "beta", "gamma" };
static int (*volatile ops[])(int, int) = { add, sub, mul };
typedef struct { const char *name; int (*fn)(int, int); const char *volatile *list; } Entry;
static const Entry table[] = { { "add", add, names }, { "sub", sub, names + 1 }, { "mul", mul, names + 2 } };
static const Entry *volatile table_ptr = table;
static int counter = 5;
static char bss_buf[4096];

static void test_relocs(void)
{
    CHECK(strcmp(names[0], "alpha") == 0 && strcmp(names[2], "gamma") == 0);
    CHECK(ops[0](7, 3) == 10 && ops[1](7, 3) == 4 && ops[2](7, 3) == 21);
    CHECK(strcmp(table_ptr[1].name, "sub") == 0);
    CHECK(table_ptr[1].fn(9, 4) == 5);
    CHECK(strcmp(*table_ptr[2].list, "gamma") == 0);
    CHECK(counter == 5); counter += 3; CHECK(counter == 8);
    int zero = 1; for (int i = 0; i < 4096; i++) if (bss_buf[i]) zero = 0;
    CHECK(zero);
}

static void test_string(void)
{
    char b[128];
    CHECK(strlen("hello") == 5);
    strcpy(b, "abc"); strcat(b, "def"); CHECK(strcmp(b, "abcdef") == 0);
    CHECK(strncmp("abcX", "abcY", 3) == 0 && strncmp("abcX", "abcY", 4) < 0);
    CHECK(strchr("hello", 'l') && strchr("hello", 'l') - "hello" != -1);
    CHECK(strrchr("hello", 'l') && *(strrchr("hello", 'l') + 1) == 'o');
    CHECK(strstr("find the needle here", "needle") != NULL && strstr("abc", "zz") == NULL);
    memset(b, 'x', 10); CHECK(b[0] == 'x' && b[9] == 'x');
    memcpy(b, "0123456789", 10); memmove(b + 2, b, 8); CHECK(memcmp(b, "0101234567", 10) == 0);
    char *d = strdup("dup"); CHECK(d && strcmp(d, "dup") == 0); free(d);
    char *d2 = strndup("duplicate", 3); CHECK(d2 && strcmp(d2, "dup") == 0); free(d2);
    CHECK(toupper('a') == 'A' && tolower('Q') == 'q' && isdigit('5') && !isdigit('x') && isspace('\t'));

    // string extensions
    CHECK(strcasecmp("Hello", "hELLO") == 0);
    CHECK(strncasecmp("Hello World", "hello there", 5) == 0);
    CHECK(strspn("12345abc", "0123456789") == 5);
    CHECK(strcspn("hello world", " \t") == 5);
    CHECK(strpbrk("apple", "xyzp") && *strpbrk("apple", "xyzp") == 'p');

    // strtok_r
    strcpy(b, "one,two,,three");
    char *save = NULL;
    char *t1 = strtok_r(b, ",", &save);
    char *t2 = strtok_r(NULL, ",", &save);
    char *t3 = strtok_r(NULL, ",", &save);
    char *t4 = strtok_r(NULL, ",", &save);
    CHECK(t1 && strcmp(t1, "one") == 0);
    CHECK(t2 && strcmp(t2, "two") == 0);
    CHECK(t3 && strcmp(t3, "three") == 0);
    CHECK(t4 == NULL);

    // strerror
    CHECK(strstr(strerror(ENOENT), "No such") != NULL);
    CHECK(strstr(strerror(0), "Success") != NULL);
}

static void test_stdlib(void)
{
    // Integer arithmetic
    CHECK(abs(-5) == 5 && labs(-5L) == 5L && llabs(-5LL) == 5LL);
    div_t d = div(17, 5);
    CHECK(d.quot == 3 && d.rem == 2);
    ldiv_t ld = ldiv(100L, 7L);
    CHECK(ld.quot == 14 && ld.rem == 2);
    lldiv_t lld = lldiv(100000000000LL, 3LL);
    CHECK(lld.quot == 33333333333LL && lld.rem == 1LL);

    // String conversion
    CHECK(atoi("  -123abc") == -123);
    CHECK(atol(" 456789") == 456789L);
    CHECK(atoll(" 9876543210123") == 9876543210123LL);
    CHECK(strtol("0x1F", NULL, 16) == 31 && strtol("0x1F", NULL, 0) == 31 && strtol("077", NULL, 0) == 63);
    CHECK(strtol("zz", NULL, 36) == 1295);
    char *end; long v = strtol("42rest", &end, 10); CHECK(v == 42 && strcmp(end, "rest") == 0);
    CHECK(strtoul("4294967295", NULL, 10) == 4294967295UL);
    CHECK(strtoull("18446744073709551615", NULL, 10) == 18446744073709551615ULL);

    // Floating string conversion
    double dval = strtod("3.14159265", NULL);
    CHECK(fabs(dval - 3.14159265) < 1e-6);
    CHECK(fabs(atof("-2.71828") - (-2.71828)) < 1e-5);
    CHECK(fabs(strtof("1.5", NULL) - 1.5f) < 1e-5);

    // Formatting itoa, ltoa, lltoa
    char buf[64];
    CHECK(strcmp(itoa(12345, buf, 10), "12345") == 0);
    CHECK(strcmp(itoa(-12345, buf, 10), "-12345") == 0);
    CHECK(strcmp(itoa(255, buf, 16), "ff") == 0);
    CHECK(strcmp(lltoa(9876543210123LL, buf, 10), "9876543210123") == 0);

    // Multibyte & wide char
    wchar_t wc;
    int mbl = mbtowc(&wc, "A", 1);
    CHECK(mbl == 1 && wc == 'A');
    char mb_buf[8];
    int wcl = wctomb(mb_buf, (wchar_t)0x20AC); // Euro sign € (3 bytes in UTF-8: E2 82 AC)
    CHECK(wcl == 3 && (unsigned char)mb_buf[0] == 0xE2);
    wchar_t wstr[16];
    size_t nconv = mbstowcs(wstr, "Windose", 16);
    CHECK(nconv == 7 && wstr[0] == 'W' && wstr[6] == 'e');
}

static void test_math(void)
{
    // Basic & rounding
    CHECK(fabs(-42.5) == 42.5);
    CHECK(floor(3.7) == 3.0 && floor(-3.7) == -4.0);
    CHECK(ceil(3.2) == 4.0 && ceil(-3.2) == -3.0);
    CHECK(round(3.5) == 4.0 && round(-3.5) == -4.0);
    CHECK(trunc(3.7) == 3.0 && trunc(-3.7) == -3.0);
    CHECK(fabs(fmod(5.5, 2.0) - 1.5) < 1e-9);
    CHECK(fmin(3.0, 7.0) == 3.0 && fmax(3.0, 7.0) == 7.0);

    // Powers and roots
    CHECK(fabs(sqrt(144.0) - 12.0) < 1e-9);
    CHECK(fabs(sqrt(2.0) - 1.41421356237) < 1e-7);
    CHECK(fabs(cbrt(27.0) - 3.0) < 1e-7);
    CHECK(fabs(hypot(3.0, 4.0) - 5.0) < 1e-7);
    CHECK(fabs(pow(2.0, 10.0) - 1024.0) < 1e-7);
    CHECK(fabs(pow(3.0, 3.0) - 27.0) < 1e-7);

    // Exponential & Logarithm
    CHECK(fabs(exp(0.0) - 1.0) < 1e-9);
    CHECK(fabs(exp(1.0) - M_E) < 1e-7);
    CHECK(fabs(log(M_E) - 1.0) < 1e-7);
    CHECK(fabs(log10(1000.0) - 3.0) < 1e-7);
    CHECK(fabs(log2(1024.0) - 10.0) < 1e-7);

    // Trigonometry
    CHECK(fabs(sin(0.0)) < 1e-9);
    CHECK(fabs(sin(M_PI_2) - 1.0) < 1e-7);
    CHECK(fabs(cos(0.0) - 1.0) < 1e-9);
    CHECK(fabs(cos(M_PI) + 1.0) < 1e-7);
    CHECK(fabs(tan(M_PI_4) - 1.0) < 1e-7);
    CHECK(fabs(atan2(1.0, 1.0) - M_PI_4) < 1e-7);
    CHECK(fabs(asin(1.0) - M_PI_2) < 1e-7);
    CHECK(fabs(acos(1.0)) < 1e-7);

    // NaN / Inf checks
    CHECK(isnan(NAN));
    CHECK(isinf(INFINITY));
    CHECK(!isnan(1.0) && isfinite(1.0));
}

static void test_stdio_streams(void)
{
    // Write formatted to file stream
    const char *path = "/mnt/libctest_tmp.txt";
    FILE *f = fopen(path, "w");
    CHECK(f != NULL);
    if (f) {
        int n = fprintf(f, "Line %d: %s %.2f\n", 1, "hello", 3.14);
        CHECK(n > 0);
        fputs("Second line\n", f);
        fputc('A', f);
        fputc('\n', f);
        CHECK(fclose(f) == 0);
    }

    // Read back from file stream
    f = fopen(path, "r");
    CHECK(f != NULL);
    if (f) {
        char line1[64];
        char line2[64];
        CHECK(fgets(line1, sizeof(line1), f) != NULL);
        CHECK(strstr(line1, "Line 1: hello 3.14") != NULL);
        CHECK(fgets(line2, sizeof(line2), f) != NULL);
        CHECK(strcmp(line2, "Second line\n") == 0);
        int c = fgetc(f);
        CHECK(c == 'A');
        CHECK(ungetc('Z', f) == 'Z');
        CHECK(fgetc(f) == 'Z');
        CHECK(fclose(f) == 0);
    }

    // Seeking and ftell
    f = fopen(path, "r");
    CHECK(f != NULL);
    if (f) {
        CHECK(fseek(f, 0, SEEK_END) == 0);
        long end_pos = ftell(f);
        CHECK(end_pos > 20);
        rewind(f);
        CHECK(ftell(f) == 0);
        fclose(f);
    }

    // sscanf testing
    int val_i = 0;
    float val_f = 0.0f;
    char val_s[32] = {0};
    int matched = sscanf("100 2.5 testing", "%d %f %s", &val_i, &val_f, val_s);
    CHECK(matched == 3);
    CHECK(val_i == 100);
    CHECK(fabs((double)val_f - 2.5) < 1e-4);
    CHECK(strcmp(val_s, "testing") == 0);

    // Clean up temporary file
    CHECK(remove(path) == 0);
}

static void test_time(void)
{
    clock_t clk = clock();
    CHECK(clk >= 0);

    time_t now = 0;
    time_t ret = time(&now);
    CHECK(ret > 1700000000LL && now == ret);

    CHECK(difftime(now + 60, now) == 60.0);

    struct tm tm_out;
    struct tm *ptm = gmtime_r(&now, &tm_out);
    CHECK(ptm != NULL);
    CHECK(ptm->tm_year >= 126); // >= 2026
    CHECK(ptm->tm_mon >= 0 && ptm->tm_mon <= 11);
    CHECK(ptm->tm_mday >= 1 && ptm->tm_mday <= 31);
    CHECK(ptm->tm_hour >= 0 && ptm->tm_hour <= 23);

    char str_buf[64];
    size_t slen = strftime(str_buf, sizeof(str_buf), "%Y-%m-%d %H:%M:%S", ptm);
    CHECK(slen > 0 && strlen(str_buf) == 19);

    char *asc = asctime(ptm);
    CHECK(asc && strlen(asc) >= 24);
}

static int cmp_int(const void *a, const void *b) { int x = *(const int *)a, y = *(const int *)b; return (x > y) - (x < y); }
static void test_qsort(void)
{
    static int arr[1000];
    srand(12345);
    for (int i = 0; i < 1000; i++) arr[i] = rand() % 500 - 250;
    qsort(arr, 1000, sizeof(int), cmp_int);
    int ok = 1; for (int i = 1; i < 1000; i++) if (arr[i - 1] > arr[i]) ok = 0;
    CHECK(ok);
    int key = arr[500];
    int *f = bsearch(&key, arr, 1000, sizeof(int), cmp_int);
    CHECK(f && *f == key);
}

static void test_printf(void)
{
    char b[128];
    snprintf(b, sizeof b, "%d|%5d|%-5d|%05d|%+d", 42, 42, 42, 42, 42);       CHECK(strcmp(b, "42|   42|42   |00042|+42") == 0);
    snprintf(b, sizeof b, "%x|%X|%#x|%o|%u", 255, 255, 255, 8, 4000000000u); CHECK(strcmp(b, "ff|FF|0xff|10|4000000000") == 0);
    snprintf(b, sizeof b, "%s|%10s|%-10s|%.2s", "abc", "abc", "abc", "abc"); CHECK(strcmp(b, "abc|       abc|abc       |ab") == 0);
    snprintf(b, sizeof b, "%c%c%%|%ld|%lld|%zu", 'o', 'k', -5L, 123456789012LL, (size_t)7); CHECK(strcmp(b, "ok%|-5|123456789012|7") == 0);
    int n = snprintf(b, 5, "%s", "0123456789"); CHECK(n == 10 && strcmp(b, "0123") == 0);

    // Float printing
    snprintf(b, sizeof b, "%.2f|%.3g", 3.14159, 2.5);
    CHECK(strcmp(b, "3.14|2.5") == 0);
}

static void test_malloc(void)
{
    enum { N = 200 };
    static unsigned char *p[N];
    static size_t sz[N];
    srand(777);
    for (int round = 0; round < 20; round++) {
        for (int i = 0; i < N; i++) {
            sz[i] = (size_t)(rand() % 4000) + 1;
            p[i] = malloc(sz[i]);
            CHECK(p[i] != NULL);
            if (!p[i]) return;
            CHECK(((size_t)p[i] & 15) == 0);
            memset(p[i], i & 0xFF, sz[i]);
        }
        for (int i = 0; i < N; i++) {
            int ok = 1;
            for (size_t j = 0; j < sz[i]; j++) if (p[i][j] != (i & 0xFF)) { ok = 0; break; }
            CHECK(ok);
        }
        for (int i = 0; i < N; i += 2) { free(p[i]); p[i] = NULL; }
        for (int i = N - 1; i >= 0; i--) if (p[i]) { free(p[i]); p[i] = NULL; }
    }
    unsigned char *z = calloc(1000, 4);
    int allz = z != NULL; for (int i = 0; z && i < 4000; i++) if (z[i]) allz = 0;
    CHECK(allz);
    char *r = malloc(10); strcpy(r, "123456789");
    r = realloc(r, 100000); CHECK(r && strcmp(r, "123456789") == 0);
    r = realloc(r, 5); CHECK(r && strncmp(r, "12345", 5) == 0);
    free(r); free(z);
    char *big = malloc(3 * 1024 * 1024); CHECK(big != NULL);
    if (big) { big[0] = 1; big[3 * 1024 * 1024 - 1] = 2; CHECK(big[0] == 1); free(big); }
    char *a = malloc(100), *b = malloc(100); free(a); free(b);
    char *c = malloc(190); CHECK(c == a); free(c);
    CHECK(malloc((size_t)-1) == NULL);

    // Aligned alloc
    void *p64 = aligned_alloc(64, 256);
    CHECK(p64 != NULL);
    CHECK(((size_t)p64 % 64) == 0);
    memset(p64, 0xAA, 256);
    free(p64);
}

static int atexit_called = 0;
static void my_atexit(void)
{
    atexit_called = 1;
}

static void deep(int n)
{
    if (n == 0) {
        printf("exit() from depth, fails=%d\n", fails);
        exit(fails ? 1 : 42);
    }
    char pad[64]; pad[0] = (char)n; deep(n - 1); pad[1] = pad[0];
}

int main(int argc, char **argv)
{
    printf("libctest: argc=%d pid=%d\n", argc, k_pid());
    if (argc >= 3) {
        CHECK(strcmp(argv[1], "foo") == 0 && strcmp(argv[2], "bar") == 0);
    } else {
        CHECK(argc >= 1 && argv[0] != NULL);
    }
    CHECK(argv[argc] == NULL);

    atexit(my_atexit);

    test_init_array();
    test_core_headers();
    test_env_and_clipboard();
    test_relocs();
    test_string();
    test_stdlib();
    test_math();
    test_stdio_streams();
    test_time();
    test_qsort();
    test_printf();
    test_malloc();

    CHECK(k_ticks() >= 0);
    deep(20);
    return 99;
}
