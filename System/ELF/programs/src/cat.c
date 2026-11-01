#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

int main(int argc, char **argv)
{
    char path[260];
    char pwd[260];
    char full[520];
    int size = 0;
    char *buf;

    if (argc < 2) {
        printf("Usage: cat <filename>\n");
        return 1;
    }

    strncpy(path, argv[1], sizeof(path) - 1);
    path[sizeof(path) - 1] = 0;

    if (path[0] == '/') {
        strncpy(full, path, sizeof(full) - 1);
        full[sizeof(full) - 1] = 0;
    } else {
        if (k_get_env("PWD", pwd, (int)sizeof(pwd)) > 0 && pwd[0]) {
            if (strcmp(pwd, "/") == 0)
                snprintf(full, sizeof(full), "/%s", path);
            else
                snprintf(full, sizeof(full), "%s/%s", pwd, path);
        } else {
            snprintf(full, sizeof(full), "/mnt/%s", path);
        }
    }

    buf = file_read(full, &size);
    if (!buf) {
        printf("cat: cannot open file %s\n", full);
        return 1;
    }

    k_write(buf, size);
    if (size > 0 && buf[size - 1] != '\n')
        putchar('\n');

    free(buf);
    return 0;
}
