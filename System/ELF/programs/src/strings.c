#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include "kernel.h"

// strings - print the sequences of printable characters in files
// usage: strings [-n min_len] [file ...]

static void resolve_path(const char *in, char *out, size_t out_cap)
{
    char pwd[260];
    if (in[0] == '/') {
        strncpy(out, in, out_cap - 1);
        out[out_cap - 1] = 0;
    } else {
        if (k_get_env("PWD", pwd, (int)sizeof(pwd)) > 0 && pwd[0]) {
            if (strcmp(pwd, "/") == 0)
                snprintf(out, out_cap, "/%s", in);
            else
                snprintf(out, out_cap, "%s/%s", pwd, in);
        } else {
            snprintf(out, out_cap, "/mnt/%s", in);
        }
    }
}

static void strings_stream(FILE *fp, int min_len)
{
    char buf[1024];
    int len = 0;
    int c;

    while ((c = fgetc(fp)) != EOF) {
        if (isprint(c) || c == '\t') {
            if (len < (int)sizeof(buf) - 1) {
                buf[len++] = (char)c;
            }
        } else {
            if (len >= min_len) {
                buf[len] = '\0';
                puts(buf);
            }
            len = 0;
        }
    }
    if (len >= min_len) {
        buf[len] = '\0';
        puts(buf);
    }
}

int main(int argc, char **argv)
{
    int min_len = 4;
    int arg_start = 1;

    for (int i = 1; i < argc; i++) {
        if (argv[i][0] == '-' && argv[i][1] != '\0' && strcmp(argv[i], "-") != 0) {
            if (strcmp(argv[i], "-n") == 0 && i + 1 < argc) {
                min_len = atoi(argv[++i]);
                arg_start = i + 1;
            } else if (argv[i][1] >= '0' && argv[i][1] <= '9') {
                min_len = atoi(&argv[i][1]);
                arg_start = i + 1;
            } else {
                fprintf(stderr, "strings: invalid option '%s'\n", argv[i]);
                return 1;
            }
        } else {
            break;
        }
    }

    if (min_len <= 0) min_len = 4;

    int files_count = argc - arg_start;
    if (files_count <= 0) {
        strings_stream(stdin, min_len);
        return 0;
    }

    for (int i = arg_start; i < argc; i++) {
        const char *fname = argv[i];
        if (strcmp(fname, "-") == 0) {
            strings_stream(stdin, min_len);
        } else {
            char full[520];
            resolve_path(fname, full, sizeof(full));
            FILE *fp = fopen(full, "rb");
            if (!fp) {
                fprintf(stderr, "strings: cannot open '%s'\n", fname);
                continue;
            }
            strings_stream(fp, min_len);
            fclose(fp);
        }
    }

    return 0;
}
