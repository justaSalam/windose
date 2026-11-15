#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include "kernel.h"

// wc - word, line, character, and byte count
// usage: wc [-l] [-w] [-c] [file ...]

static int opt_lines = 0;
static int opt_words = 0;
static int opt_bytes = 0;

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

static void count_stream(FILE *fp, long *out_lines, long *out_words, long *out_bytes)
{
    long l = 0, w = 0, b = 0;
    int in_word = 0;
    int c;

    while ((c = fgetc(fp)) != EOF) {
        b++;
        if (c == '\n') l++;

        if (isspace(c)) {
            in_word = 0;
        } else if (!in_word) {
            in_word = 1;
            w++;
        }
    }

    *out_lines = l;
    *out_words = w;
    *out_bytes = b;
}

static void print_counts(long l, long w, long b, const char *label)
{
    int printed = 0;
    if (opt_lines) {
        printf("%7ld", l);
        printed = 1;
    }
    if (opt_words) {
        if (printed) putchar(' ');
        printf("%7ld", w);
        printed = 1;
    }
    if (opt_bytes) {
        if (printed) putchar(' ');
        printf("%7ld", b);
        printed = 1;
    }
    if (label) {
        printf(" %s", label);
    }
    putchar('\n');
}

int main(int argc, char **argv)
{
    int arg_start = 1;

    for (int i = 1; i < argc; i++) {
        if (argv[i][0] == '-' && argv[i][1] != '\0' && strcmp(argv[i], "-") != 0) {
            for (int j = 1; argv[i][j]; j++) {
                if (argv[i][j] == 'l') opt_lines = 1;
                else if (argv[i][j] == 'w') opt_words = 1;
                else if (argv[i][j] == 'c' || argv[i][j] == 'm') opt_bytes = 1;
                else {
                    fprintf(stderr, "wc: unrecognized option '-%c'\n", argv[i][j]);
                    return 1;
                }
            }
            arg_start = i + 1;
        } else {
            break;
        }
    }

    if (!opt_lines && !opt_words && !opt_bytes) {
        opt_lines = 1;
        opt_words = 1;
        opt_bytes = 1;
    }

    int files_count = argc - arg_start;
    if (files_count <= 0) {
        long l, w, b;
        count_stream(stdin, &l, &w, &b);
        print_counts(l, w, b, NULL);
        return 0;
    }

    long total_l = 0, total_w = 0, total_b = 0;
    int processed_files = 0;

    for (int i = arg_start; i < argc; i++) {
        const char *fname = argv[i];
        long l = 0, w = 0, b = 0;

        if (strcmp(fname, "-") == 0) {
            count_stream(stdin, &l, &w, &b);
            print_counts(l, w, b, "-");
            total_l += l; total_w += w; total_b += b;
            processed_files++;
        } else {
            char full[520];
            resolve_path(fname, full, sizeof(full));
            FILE *fp = fopen(full, "r");
            if (!fp) {
                fprintf(stderr, "wc: %s: No such file or directory\n", fname);
                continue;
            }
            count_stream(fp, &l, &w, &b);
            fclose(fp);

            print_counts(l, w, b, fname);
            total_l += l; total_w += w; total_b += b;
            processed_files++;
        }
    }

    if (processed_files > 1) {
        print_counts(total_l, total_w, total_b, "total");
    }

    return 0;
}
