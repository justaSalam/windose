// crt0.c - program entry. Kernel calls _start(KernelApi*); we set up globals, build argc/argv,
// run main(), and turn exit() from anywhere into a clean return to the kernel with an exit code.
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

KernelApi *__k;
static void *exit_buf[5];
static int exit_code;

int main(int argc, char **argv);

#define MAX_ATEXIT 32
static void (*atexit_handlers[MAX_ATEXIT])(void);
static int n_atexit = 0;

static void (*at_quick_exit_handlers[MAX_ATEXIT])(void);
static int n_at_quick_exit = 0;

int atexit(void (*func)(void))
{
    if (!func || n_atexit >= MAX_ATEXIT) return -1;
    atexit_handlers[n_atexit++] = func;
    return 0;
}

int at_quick_exit(void (*func)(void))
{
    if (!func || n_at_quick_exit >= MAX_ATEXIT) return -1;
    at_quick_exit_handlers[n_at_quick_exit++] = func;
    return 0;
}

void _Exit(int code)
{
    exit_code = code;
    __builtin_longjmp(exit_buf, 1);
}

void quick_exit(int code)
{
    while (n_at_quick_exit > 0) {
        void (*fn)(void) = at_quick_exit_handlers[--n_at_quick_exit];
        if (fn) fn();
    }
    _Exit(code);
}

void __attribute__((weak)) __stdio_cleanup(void);

void exit(int code)
{
    while (n_atexit > 0) {
        void (*fn)(void) = atexit_handlers[--n_atexit];
        if (fn) fn();
    }
    if (__stdio_cleanup) {
        __stdio_cleanup();
    }
    _Exit(code);
}

void abort(void)
{
    static const char m[] = "abort()\n";
    k_write(m, (int)sizeof m - 1);
    _Exit(134);
}

KABI int _start(KernelApi *k)
{
    __k = k;
    if ((int)k->version < KAPI_VERSION || (size_t)k->size < sizeof(KernelApi)) {
        static const char m[] = "ELF: kernel API too old - rebuild the program\n";
        k->write(k->pid, m, (int)sizeof m - 1);
        return 127;
    }
    if (__builtin_setjmp(exit_buf) == 0) {
        char buf[1024];
        char *argv[33];
        int argc = k->get_args(k->pid, buf, (int)sizeof buf);
        if (argc < 0) argc = 0;
        if (argc > 32) argc = 32;
        char *p = buf;
        for (int i = 0; i < argc; i++) { argv[i] = p; p += strlen(p) + 1; }
        argv[argc] = NULL;
        int ret = main(argc, argv);
        exit(ret);
    }
    return exit_code;
}
