#include <stdio.h>
#include <stdlib.h>
#include <string.h>

// Allocates 1 MB blocks until the kernel quota says no.
int main(void)
{
    int n = 0;
    for (;;) {
        char *p = malloc(1 << 20);
        if (!p) break;
        memset(p, 1, 1 << 20);
        n++;
    }
    printf("allocated %d MB before malloc returned NULL\n", n);
    return 0;
}
