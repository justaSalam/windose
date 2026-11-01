#include <stdio.h>
#include <string.h>
#include "kernel.h"

int main(int argc, char **argv)
{
    char pwd[260], full[520];
    if (argc < 2) {
        printf("Usage: rm <path>\n");
        return 1;
    }
    if (argv[1][0] == '/') {
        strncpy(full, argv[1], sizeof(full) - 1);
        full[sizeof(full) - 1] = 0;
    } else {
        if (k_get_env("PWD", pwd, (int)sizeof(pwd)) > 0 && pwd[0]) {
            if (strcmp(pwd, "/") == 0) snprintf(full, sizeof(full), "/%s", argv[1]);
            else snprintf(full, sizeof(full), "%s/%s", pwd, argv[1]);
        } else {
            snprintf(full, sizeof(full), "/mnt/%s", argv[1]);
        }
    }
    if (k_delete_path(full) != 0) {
        printf("rm: failed to remove %s\n", full);
        return 1;
    }
    printf("Removed %s\n", full);
    return 0;
}
