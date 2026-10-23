#ifndef WINDOSE_STDLIB_H
#define WINDOSE_STDLIB_H
#include <stddef.h>
#define RAND_MAX 32767
void *malloc(size_t n);
void *calloc(size_t n, size_t sz);
void *realloc(void *p, size_t n);
void free(void *p);
void exit(int code) __attribute__((noreturn));
void abort(void) __attribute__((noreturn));
int abs(int v);
long labs(long v);
int atoi(const char *s);
long atol(const char *s);
long strtol(const char *s, char **end, int base);
unsigned long strtoul(const char *s, char **end, int base);
void qsort(void *base, size_t n, size_t size, int (*cmp)(const void *, const void *));
void *bsearch(const void *key, const void *base, size_t n, size_t size, int (*cmp)(const void *, const void *));
int rand(void);
void srand(unsigned seed);
char *getenv(const char *name);
int setenv(const char *name, const char *value, int overwrite);
int unsetenv(const char *name);
#endif
