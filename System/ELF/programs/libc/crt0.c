// crt0.c - program entry. Kernel calls _start(KernelApi*); we set up globals, build argc/argv,
// run main(), and turn exit() from anywhere into a clean return to the kernel with an exit code.
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

KernelApi *__k;
static void *exit_buf[5];
static int exit_code;

int main(int argc, char **argv);

void exit(int code) { exit_code = code; __builtin_longjmp(exit_buf, 1); }
void abort(void) { static const char m[] = "abort()\n"; k_write(m, (int)sizeof m - 1); exit(134); }

KABI int _start(KernelApi *k)
{
    __k = k;
    if (k->version != KAPI_VERSION || (size_t)k->size < sizeof(KernelApi)) {
        static const char m[] = "ELF: kernel API version mismatch - rebuild the program\n";
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
        exit_code = main(argc, argv);
    }
    return exit_code;
}
