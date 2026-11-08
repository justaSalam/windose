#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

// Terminal text file viewer / concatenator with line numbering (-n)
// and multi-file arguments.
// usage: cat [-n] <file1> [file2 ...]

static void print_file_content(const char *buf, int size, int show_line_numbers, int *line_counter)
{
    int i;
    int at_line_start = 1;

    for (i = 0; i < size; i++) {
        if (show_line_numbers && at_line_start) {
            printf("%6d  ", ++(*line_counter));
            at_line_start = 0;
        }
        putchar(buf[i]);
        if (buf[i] == '\n') {
            at_line_start = 1;
        }
    }
    if (size > 0 && buf[size - 1] != '\n') {
        putchar('\n');
    }
}

static int process_file(const char *path, int show_line_numbers, int *line_counter)
{
    char pwd[260];
    char full[520];
    int size = 0;
    char *buf;

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

    print_file_content(buf, size, show_line_numbers, line_counter);
    free(buf);
    return 0;
}

int main(int argc, char **argv)
{
    int show_lines = 0;
    int file_count = 0;
    int line_counter = 0;
    int errors = 0;
    int a;

    if (argc < 2) {
        printf("Usage: cat [-n] <filename> [filename2 ...]\n");
        printf("  -n, /n    Number all output lines\n");
        return 1;
    }

    for (a = 1; a < argc; a++) {
        const char *arg = argv[a];
        if (!strcmp(arg, "-n") || !strcmp(arg, "/n") || !strcmp(arg, "-N") || !strcmp(arg, "/N")) {
            show_lines = 1;
        } else if (!strcmp(arg, "-h") || !strcmp(arg, "--help") || !strcmp(arg, "/?")) {
            printf("Usage: cat [-n] <filename> [filename2 ...]\n");
            printf("  -n, /n    Number all output lines\n");
            return 0;
        } else {
            file_count++;
            if (process_file(arg, show_lines, &line_counter) != 0) {
                errors++;
            }
        }
    }

    if (file_count == 0) {
        printf("cat: no input file specified\n");
        return 1;
    }

    return errors > 0 ? 1 : 0;
}
