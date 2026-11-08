#ifndef WINDOSE_STDLIB_H
#define WINDOSE_STDLIB_H

#include <stddef.h>

#define EXIT_SUCCESS 0
#define EXIT_FAILURE 1
#define RAND_MAX 32767
#define MB_CUR_MAX 4

#ifndef _WCHAR_T
#define _WCHAR_T
#if defined(__WCHAR_TYPE__)
typedef __WCHAR_TYPE__ wchar_t;
#else
typedef int wchar_t;
#endif
#endif

typedef struct {
    int quot;
    int rem;
} div_t;

typedef struct {
    long quot;
    long rem;
} ldiv_t;

typedef struct {
    long long quot;
    long long rem;
} lldiv_t;

/* Memory management */
void *malloc(size_t n);
void *calloc(size_t n, size_t sz);
void *realloc(void *p, size_t n);
void free(void *p);
void *aligned_alloc(size_t alignment, size_t size);

/* Process termination & hooks */
void exit(int code) __attribute__((noreturn));
void abort(void) __attribute__((noreturn));
void _Exit(int code) __attribute__((noreturn));
void quick_exit(int code) __attribute__((noreturn));
int atexit(void (*func)(void));
int at_quick_exit(void (*func)(void));
int system(const char *command);

/* Integer arithmetic */
int abs(int v);
long labs(long v);
long long llabs(long long v);
div_t div(int numer, int denom);
ldiv_t ldiv(long numer, long denom);
lldiv_t lldiv(long long numer, long long denom);

/* Numeric conversions */
int atoi(const char *s);
long atol(const char *s);
long long atoll(const char *s);
double atof(const char *s);

long strtol(const char *s, char **end, int base);
unsigned long strtoul(const char *s, char **end, int base);
long long strtoll(const char *s, char **end, int base);
unsigned long long strtoull(const char *s, char **end, int base);

double strtod(const char *s, char **end);
float strtof(const char *s, char **end);
long double strtold(const char *s, char **end);

char *itoa(int val, char *buf, int radix);
char *ltoa(long val, char *buf, int radix);
char *lltoa(long long val, char *buf, int radix);

/* Searching and sorting */
void qsort(void *base, size_t n, size_t size, int (*cmp)(const void *, const void *));
void *bsearch(const void *key, const void *base, size_t n, size_t size, int (*cmp)(const void *, const void *));

/* Pseudo-random numbers */
int rand(void);
void srand(unsigned seed);

/* Environment variables */
char *getenv(const char *name);
int setenv(const char *name, const char *value, int overwrite);
int unsetenv(const char *name);

/* Multibyte and wide characters */
int mblen(const char *s, size_t n);
int mbtowc(wchar_t *pwc, const char *s, size_t n);
int wctomb(char *s, wchar_t wc);
size_t mbstowcs(wchar_t *pwcs, const char *s, size_t n);
size_t wcstombs(char *s, const wchar_t *pwcs, size_t n);

#endif
