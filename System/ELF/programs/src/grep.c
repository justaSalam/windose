#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include "kernel.h"

// grep - search files or stdin for lines matching a pattern
// usage: grep [-i] [-v] [-n] [-c] [-l] <pattern> [file ...]

static int opt_ignore_case = 0;
static int opt_invert = 0;
static int opt_line_number = 0;
static int opt_count_only = 0;
static int opt_files_only = 0;

static int match_pattern(const char *line, const char *pat)
{
    if (!pat || !*pat) return 1;
    if (!line) return 0;

    if (!opt_ignore_case) {
        return strstr(line, pat) != NULL;
    }

    size_t pat_len = strlen(pat);
    size_t line_len = strlen(line);
    if (pat_len > line_len) return 0;

    for (size_t i = 0; i <= line_len - pat_len; i++) {
        size_t j = 0;
        while (j < pat_len) {
            char c1 = tolower((unsigned char)line[i + j]);
            char c2 = tolower((unsigned char)pat[j]);
            if (c1 != c2) break;
            j++;
        }
        if (j == pat_len) return 1;
    }
    return 0;
}

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

static int grep_stream(FILE *fp, const char *pat, const char *display_name, int multiple_files)
{
    char line[4096];
    int line_no = 0;
    int match_count = 0;

    while (fgets(line, sizeof(line), fp)) {
        line_no++;
        // Strip trailing newline for clean display if needed, but standard grep outputs full line
        size_t len = strlen(line);
        int matched = match_pattern(line, pat);
        if (opt_invert) matched = !matched;

        if (matched) {
            match_count++;
            if (opt_files_only) {
                printf("%s\n", display_name);
                return 1;
            }
            if (!opt_count_only) {
                if (multiple_files && display_name)
                    printf("%s:", display_name);
                if (opt_line_number)
                    printf("%d:", line_no);
                fputs(line, stdout);
                if (len == 0 || line[len - 1] != '\n')
                    putchar('\n');
            }
        }
    }

    if (opt_count_only) {
        if (multiple_files && display_name)
            printf("%s:%d\n", display_name, match_count);
        else
            printf("%d\n", match_count);
    }

    return match_count > 0 ? 1 : 0;
}

int main(int argc, char **argv)
{
    if (argc < 2) {
        fprintf(stderr, "usage: grep [-i] [-v] [-n] [-c] [-l] <pattern> [file ...]\n");
        return 2;
    }

    const char *pat = NULL;
    int arg_start = 1;

    for (int i = 1; i < argc; i++) {
        if (argv[i][0] == '-' && argv[i][1] != '\0' && !pat) {
            if (strcmp(argv[i], "--") == 0) {
                if (i + 1 < argc) pat = argv[++i];
                arg_start = i + 1;
                break;
            }
            for (int j = 1; argv[i][j]; j++) {
                if (argv[i][j] == 'i') opt_ignore_case = 1;
                else if (argv[i][j] == 'v') opt_invert = 1;
                else if (argv[i][j] == 'n') opt_line_number = 1;
                else if (argv[i][j] == 'c') opt_count_only = 1;
                else if (argv[i][j] == 'l') opt_files_only = 1;
                else {
                    fprintf(stderr, "grep: unrecognized option '-%c'\n", argv[i][j]);
                    return 2;
                }
            }
        } else if (!pat) {
            pat = argv[i];
            arg_start = i + 1;
        }
    }

    if (!pat) {
        fprintf(stderr, "grep: no pattern specified\n");
        return 2;
    }

    int files_count = argc - arg_start;
    int matched_any = 0;

    if (files_count <= 0) {
        // Read from stdin
        if (grep_stream(stdin, pat, NULL, 0))
            matched_any = 1;
    } else {
        int multiple = files_count > 1;
        for (int i = arg_start; i < argc; i++) {
            const char *fname = argv[i];
            if (strcmp(fname, "-") == 0) {
                if (grep_stream(stdin, pat, "(standard input)", multiple))
                    matched_any = 1;
            } else {
                char full[520];
                resolve_path(fname, full, sizeof(full));
                FILE *fp = fopen(full, "r");
                if (!fp) {
                    fprintf(stderr, "grep: %s: No such file or directory\n", fname);
                    continue;
                }
                if (grep_stream(fp, pat, fname, multiple))
                    matched_any = 1;
                fclose(fp);
            }
        }
    }

    return matched_any ? 0 : 1;
}
