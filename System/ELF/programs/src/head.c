#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

// head - output the first part of files
// usage: head [-n lines] [file ...]

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

static void head_stream(FILE *fp, int max_lines)
{
    char buf[4096];
    int lines = 0;

    while (lines < max_lines && fgets(buf, sizeof(buf), fp)) {
        fputs(buf, stdout);
        size_t len = strlen(buf);
        if (len > 0 && buf[len - 1] == '\n')
            lines++;
    }
}

int main(int argc, char **argv)
{
    int max_lines = 10;
    int arg_start = 1;

    for (int i = 1; i < argc; i++) {
        if (argv[i][0] == '-' && argv[i][1] != '\0' && strcmp(argv[i], "-") != 0) {
            if (strcmp(argv[i], "-n") == 0 && i + 1 < argc) {
                max_lines = atoi(argv[++i]);
                arg_start = i + 1;
            } else if (argv[i][1] >= '0' && argv[i][1] <= '9') {
                max_lines = atoi(&argv[i][1]);
                arg_start = i + 1;
            } else {
                fprintf(stderr, "head: invalid option '%s'\n", argv[i]);
                return 1;
            }
        } else {
            break;
        }
    }

    if (max_lines <= 0) max_lines = 10;

    int files_count = argc - arg_start;
    if (files_count <= 0) {
        head_stream(stdin, max_lines);
        return 0;
    }

    int multiple = files_count > 1;
    for (int i = arg_start; i < argc; i++) {
        const char *fname = argv[i];
        if (multiple) {
            if (i > arg_start) printf("\n");
            printf("==> %s <==\n", fname);
        }

        if (strcmp(fname, "-") == 0) {
            head_stream(stdin, max_lines);
        } else {
            char full[520];
            resolve_path(fname, full, sizeof(full));
            FILE *fp = fopen(full, "r");
            if (!fp) {
                fprintf(stderr, "head: cannot open '%s' for reading\n", fname);
                continue;
            }
            head_stream(fp, max_lines);
            fclose(fp);
        }
    }

    return 0;
}
