#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

static void format_byte_size(unsigned long long bytes, char *buf, size_t buflen)
{
    if (bytes < 1024ULL) {
        snprintf(buf, buflen, "%llu bytes", bytes);
    } else if (bytes < 1024ULL * 1024ULL) {
        unsigned long long whole = bytes / 1024ULL;
        unsigned long long frac = (bytes % 1024ULL) * 100 / 1024ULL;
        snprintf(buf, buflen, "%llu.%02llu KB", whole, frac);
    } else if (bytes < 1024ULL * 1024ULL * 1024ULL) {
        unsigned long long whole = bytes / (1024ULL * 1024ULL);
        unsigned long long frac = (bytes % (1024ULL * 1024ULL)) * 100 / (1024ULL * 1024ULL);
        snprintf(buf, buflen, "%llu.%02llu MB", whole, frac);
    } else {
        unsigned long long whole = bytes / (1024ULL * 1024ULL * 1024ULL);
        unsigned long long frac = (bytes % (1024ULL * 1024ULL * 1024ULL)) * 100 / (1024ULL * 1024ULL * 1024ULL);
        snprintf(buf, buflen, "%llu.%02llu GB", whole, frac);
    }
}

int main(int argc, char **argv)
{
    char path[260];
    char pwd[260];
    char listbuf[8192];
    int n, off, i;

    // Resolve target path from args or PWD
    if (argc > 1 && argv[1][0]) {
        if (argv[1][0] == '/') {
            strncpy(path, argv[1], sizeof(path) - 1);
            path[sizeof(path) - 1] = 0;
        } else {
            if (k_get_env("PWD", pwd, (int)sizeof(pwd)) > 0 && pwd[0]) {
                if (strcmp(pwd, "/") == 0)
                    snprintf(path, sizeof(path), "/%s", argv[1]);
                else
                    snprintf(path, sizeof(path), "%s/%s", pwd, argv[1]);
            } else {
                snprintf(path, sizeof(path), "/mnt/%s", argv[1]);
            }
        }
    } else {
        if (k_get_env("PWD", pwd, (int)sizeof(pwd)) > 0 && pwd[0]) {
            strncpy(path, pwd, sizeof(path) - 1);
            path[sizeof(path) - 1] = 0;
        } else {
            strcpy(path, "/mnt");
        }
    }

    n = k_list_dir(path, listbuf, (int)sizeof(listbuf));
    if (n < 0) {
        printf("Directory of %s\n\n", path);
        printf("File not found or cannot access path.\n");
        return 1;
    }

    printf("Directory of %s\n\n", path);

    // Pass 1: Directories (just like DirectoryInfo GetDirectories() in C#)
    off = 0;
    for (i = 0; i < n; i++) {
        char *name = listbuf + off;
        char full[520];
        FileStat st;
        off += (int)strlen(name) + 1;

        if (strcmp(path, "/") == 0)
            snprintf(full, sizeof(full), "/%s", name);
        else
            snprintf(full, sizeof(full), "%s/%s", path, name);

        if (k_stat_file(full, &st) == 0 && st.is_dir) {
            printf("^cyan2026-11-01 12:00 ^yellow<DIR> ^white%s\n", name);
        }
    }

    // Pass 2: Files (just like DirectoryInfo GetFiles() in C#)
    off = 0;
    for (i = 0; i < n; i++) {
        char *name = listbuf + off;
        char full[520];
        FileStat st;
        char szbuf[32];
        off += (int)strlen(name) + 1;

        if (strcmp(path, "/") == 0)
            snprintf(full, sizeof(full), "/%s", name);
        else
            snprintf(full, sizeof(full), "%s/%s", path, name);

        if (k_stat_file(full, &st) == 0 && !st.is_dir) {
            unsigned long long sz = ((unsigned long long)(unsigned)st.size_hi << 32) | (unsigned)st.size_lo;
            format_byte_size(sz, szbuf, sizeof(szbuf));
            printf("^cyan2026-11-01 12:00 ^gray%s ^white%s\n", szbuf, name);
        }
    }

    return 0;
}
