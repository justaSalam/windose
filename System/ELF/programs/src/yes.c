#include <stdio.h>
#include <string.h>

// yes - output a string repeatedly until specified count (default 100)
// usage: yes [string]

int main(int argc, char **argv)
{
    const char *msg = "y";
    if (argc > 1) {
        msg = argv[1];
    }

    // Default to 100 lines to prevent un-interruptible lockups in single-threaded terminal
    for (int i = 0; i < 100; i++) {
        puts(msg);
    }

    return 0;
}
