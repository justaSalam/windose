#include "kapi.h"

// Entry point: the kernel calls this like a normal function, KernelApi* in RDI.
KABI void _start(KernelApi *k) {
    kputs(k, "Hello from an ELF program!\n");

    static int counter = 5;          // proves .data works
    static char buf[16];             // proves .bss works
    for (int i = 0; i < 3; i++) counter += i;   // 5+0+1+2 = 8
    int n = 0;
    buf[n++] = 'c'; buf[n++] = '=';
    n += kitoa(counter, buf + n);
    buf[n++] = '\n';
    k->write(buf, n);
}
