#include <stdio.h>
#include <unistd.h>
#include <string.h>
#include "kernel.h"

// pwd - print name of current/working directory
// usage: pwd

int main(int argc, char **argv)
{
    (void)argc;
    (void)argv;

    char buf[512];
    if (getcwd(buf, sizeof(buf)) != NULL) {
        printf("%s\n", buf);
        return 0;
    }

    if (k_get_env("PWD", buf, (int)sizeof(buf)) > 0 && buf[0]) {
        printf("%s\n", buf);
        return 0;
    }

    printf("/mnt\n");
    return 0;
}
