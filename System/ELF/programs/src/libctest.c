// Self-test for the libc, relocations (PIE tables), argv and exit(). Run with args: libctest foo bar
// Exits 42 on success (via exit() from a deep call), 1 on failure.
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
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
static int counter = 5;                 // .data
static char bss_buf[4096];              // .bss

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
    char b[64];
    CHECK(strlen("hello") == 5);
    strcpy(b, "abc"); strcat(b, "def"); CHECK(strcmp(b, "abcdef") == 0);
    CHECK(strncmp("abcX", "abcY", 3) == 0 && strncmp("abcX", "abcY", 4) < 0);
    CHECK(strchr("hello", 'l') && strchr("hello", 'l') - "hello" != -1);
    CHECK(strrchr("hello", 'l') && *(strrchr("hello", 'l') + 1) == 'o');
    CHECK(strstr("find the needle here", "needle") != NULL && strstr("abc", "zz") == NULL);
    memset(b, 'x', 10); CHECK(b[0] == 'x' && b[9] == 'x');
    memcpy(b, "0123456789", 10); memmove(b + 2, b, 8); CHECK(memcmp(b, "0101234567", 10) == 0);
    char *d = strdup("dup"); CHECK(d && strcmp(d, "dup") == 0); free(d);
    CHECK(toupper('a') == 'A' && tolower('Q') == 'q' && isdigit('5') && !isdigit('x') && isspace('\t'));
}

static void test_stdlib(void)
{
    CHECK(atoi("  -123abc") == -123);
    CHECK(strtol("0x1F", NULL, 16) == 31 && strtol("0x1F", NULL, 0) == 31 && strtol("077", NULL, 0) == 63);
    CHECK(strtol("zz", NULL, 36) == 1295);
    char *end; long v = strtol("42rest", &end, 10); CHECK(v == 42 && strcmp(end, "rest") == 0);
    CHECK(strtoul("4294967295", NULL, 10) == 4294967295UL);
    CHECK(abs(-5) == 5 && labs(-5L) == 5);
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
        for (int i = 0; i < N; i++) {                       // nobody stomped on anybody
            int ok = 1;
            for (size_t j = 0; j < sz[i]; j++) if (p[i][j] != (i & 0xFF)) { ok = 0; break; }
            CHECK(ok);
        }
        for (int i = 0; i < N; i += 2) { free(p[i]); p[i] = NULL; }   // free evens, then odds in reverse
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
    char *a = malloc(100), *b = malloc(100); free(a); free(b);      // coalescing: a 190-byte block must now fit in the same spot
    char *c = malloc(190); CHECK(c == a); free(c);
    CHECK(malloc((size_t)-1) == NULL);
}

static void deep(int n)
{
    if (n == 0) { printf("exit() from depth, fails=%d\n", fails); exit(fails ? 1 : 42); }
    char pad[64]; pad[0] = (char)n; deep(n - 1); pad[1] = pad[0];   // not a tail call
}

int main(int argc, char **argv)
{
    printf("libctest: argc=%d pid=%d\n", argc, k_pid());
    CHECK(argc == 3 && strcmp(argv[1], "foo") == 0 && strcmp(argv[2], "bar") == 0);
    CHECK(argv[argc] == NULL);
    test_init_array(); test_env_and_clipboard();
    test_relocs(); test_string(); test_stdlib(); test_qsort(); test_printf(); test_malloc();
    CHECK(k_ticks() >= 0);
    deep(20);
    return 99;      // unreachable
}
