#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

// CLI utility: dir command with summary statistics, wide display (/w),
// recursive listing (/s), and bare filenames (/b).

#define FLAG_WIDE      1
#define FLAG_RECURSIVE 2
#define FLAG_BARE      4

#define MAX_STACK_DIRS 256
#define MAX_ENTRIES    256

static char dirstack[MAX_STACK_DIRS][260];
static char listbuf[8192];

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

static unsigned long long get_drive_free(const char *path)
{
    char drivebuf[256];
    int n, off, i;
    DriveStat dst;

    if (k_drive_stat(path, &dst) == 0) {
        return ((unsigned long long)(unsigned)dst.free_hi << 32) | (unsigned)dst.free_lo;
    }
    if (k_drive_stat("/mnt", &dst) == 0) {
        return ((unsigned long long)(unsigned)dst.free_hi << 32) | (unsigned)dst.free_lo;
    }
    n = k_list_drives(drivebuf, (int)sizeof(drivebuf));
    if (n > 0) {
        off = 0;
        for (i = 0; i < n; i++) {
            char *mount = drivebuf + off;
            off += (int)strlen(mount) + 1;
            if (k_drive_stat(mount, &dst) == 0) {
                return ((unsigned long long)(unsigned)dst.free_hi << 32) | (unsigned)dst.free_lo;
            }
        }
    }
    return 0;
}

int main(int argc, char **argv)
{
    char start_path[260];
    char pwd[260];
    char target[260] = "";
    int flags = 0;
    int a;
    int stack_top = 0;
    int dirs_processed = 0;
    int grand_files = 0;
    int grand_dirs = 0;
    unsigned long long grand_bytes = 0;
    unsigned long long free_bytes = 0;

    // Parse options & arguments
    for (a = 1; a < argc; a++) {
        const char *arg = argv[a];
        if (arg[0] == '/' || arg[0] == '-') {
            char opt = arg[1];
            if (opt >= 'A' && opt <= 'Z') opt += 32;
            if (opt == 'w') flags |= FLAG_WIDE;
            else if (opt == 's') flags |= FLAG_RECURSIVE;
            else if (opt == 'b') flags |= FLAG_BARE;
            else if (opt == '?' || opt == 'h') {
                printf("Displays a list of files and subdirectories in a directory.\n\n");
                printf("DIR [drive:][path] [/W] [/S] [/B]\n\n");
                printf("  /W          Uses wide list format.\n");
                printf("  /S          Displays files in specified directory and all subdirectories.\n");
                printf("  /B          Uses bare format (no heading information or summary).\n");
                return 0;
            } else {
                if (arg[0] == '/' && !target[0]) {
                    strncpy(target, arg, sizeof(target) - 1);
                }
            }
        } else {
            if (!target[0]) {
                strncpy(target, arg, sizeof(target) - 1);
            }
        }
    }

    // Resolve starting path
    if (target[0]) {
        if (target[0] == '/') {
            strncpy(start_path, target, sizeof(start_path) - 1);
            start_path[sizeof(start_path) - 1] = 0;
        } else {
            if (k_get_env("PWD", pwd, (int)sizeof(pwd)) > 0 && pwd[0]) {
                if (strcmp(pwd, "/") == 0)
                    snprintf(start_path, sizeof(start_path), "/%s", target);
                else
                    snprintf(start_path, sizeof(start_path), "%s/%s", pwd, target);
            } else {
                snprintf(start_path, sizeof(start_path), "/mnt/%s", target);
            }
        }
    } else {
        if (k_get_env("PWD", pwd, (int)sizeof(pwd)) > 0 && pwd[0]) {
            strncpy(start_path, pwd, sizeof(start_path) - 1);
            start_path[sizeof(start_path) - 1] = 0;
        } else {
            strcpy(start_path, "/mnt");
        }
    }

    free_bytes = get_drive_free(start_path);

    strncpy(dirstack[stack_top++], start_path, sizeof(dirstack[0]) - 1);

    while (stack_top > 0) {
        char curr_path[260];
        int n, off, i;
        int folder_files = 0;
        int folder_dirs = 0;
        unsigned long long folder_bytes = 0;
        char subdirs[64][260];
        int nsubdirs = 0;
        int wide_col = 0;

        strncpy(curr_path, dirstack[--stack_top], sizeof(curr_path) - 1);
        curr_path[sizeof(curr_path) - 1] = 0;

        n = k_list_dir(curr_path, listbuf, (int)sizeof(listbuf));
        if (n < 0) {
            if (!(flags & FLAG_BARE)) {
                printf("Directory of %s\n\n", curr_path);
                printf("File not found or cannot access path.\n\n");
            }
            continue;
        }

        dirs_processed++;

        if (!(flags & FLAG_BARE)) {
            printf("Directory of %s\n\n", curr_path);
        }

        // Pass 1: Directories
        off = 0;
        for (i = 0; i < n; i++) {
            char *name = listbuf + off;
            char full[520];
            FileStat st;
            off += (int)strlen(name) + 1;

            if (!strcmp(name, ".") || !strcmp(name, "..")) continue;

            if (strcmp(curr_path, "/") == 0)
                snprintf(full, sizeof(full), "/%s", name);
            else
                snprintf(full, sizeof(full), "%s/%s", curr_path, name);

            if (k_stat_file(full, &st) == 0 && st.is_dir) {
                folder_dirs++;
                grand_dirs++;

                if (flags & FLAG_RECURSIVE) {
                    if (strcmp(name, ".trash") != 0 && nsubdirs < 64) {
                        strncpy(subdirs[nsubdirs++], full, sizeof(subdirs[0]) - 1);
                    }
                }

                if (flags & FLAG_BARE) {
                    if (flags & FLAG_RECURSIVE) printf("%s\n", full);
                    else printf("%s\n", name);
                } else if (flags & FLAG_WIDE) {
                    char item[24];
                    snprintf(item, sizeof(item), "[%s]", name);
                    printf("^yellow%-18s^white", item);
                    if (++wide_col == 4) { printf("\n"); wide_col = 0; }
                } else {
                    printf("^cyan2026-11-01 12:00 ^yellow<DIR>          ^white%s\n", name);
                }
            }
        }

        // Pass 2: Files
        off = 0;
        for (i = 0; i < n; i++) {
            char *name = listbuf + off;
            char full[520];
            FileStat st;
            off += (int)strlen(name) + 1;

            if (!strcmp(name, ".") || !strcmp(name, "..")) continue;

            if (strcmp(curr_path, "/") == 0)
                snprintf(full, sizeof(full), "/%s", name);
            else
                snprintf(full, sizeof(full), "%s/%s", curr_path, name);

            if (k_stat_file(full, &st) == 0 && !st.is_dir) {
                unsigned long long sz = ((unsigned long long)(unsigned)st.size_hi << 32) | (unsigned)st.size_lo;
                folder_files++;
                grand_files++;
                folder_bytes += sz;
                grand_bytes += sz;

                if (flags & FLAG_BARE) {
                    if (flags & FLAG_RECURSIVE) printf("%s\n", full);
                    else printf("%s\n", name);
                } else if (flags & FLAG_WIDE) {
                    printf("^white%-18s", name);
                    if (++wide_col == 4) { printf("\n"); wide_col = 0; }
                } else {
                    char szbuf[32];
                    format_byte_size(sz, szbuf, sizeof(szbuf));
                    printf("^cyan2026-11-01 12:00 ^gray%14s ^white%s\n", szbuf, name);
                }
            }
        }

        if ((flags & FLAG_WIDE) && wide_col > 0) {
            printf("\n");
        }

        // Summary footer
        if (!(flags & FLAG_BARE)) {
            printf("  %14d File(s) %14llu bytes\n", folder_files, folder_bytes);
            printf("  %14d Dir(s)  %14llu bytes free\n\n", folder_dirs, free_bytes);
        }

        // Push subdirectories in reverse order so first subfolder is popped first
        if (flags & FLAG_RECURSIVE) {
            int s;
            for (s = nsubdirs - 1; s >= 0; s--) {
                if (stack_top < MAX_STACK_DIRS) {
                    strncpy(dirstack[stack_top++], subdirs[s], sizeof(dirstack[0]) - 1);
                }
            }
        }
    }

    // Grand total for recursive listing
    if ((flags & FLAG_RECURSIVE) && !(flags & FLAG_BARE) && dirs_processed > 1) {
        printf("     Total Files Listed:\n");
        printf("  %14d File(s) %14llu bytes\n", grand_files, grand_bytes);
        printf("  %14d Dir(s)  %14llu bytes free\n\n", grand_dirs, free_bytes);
    }

    return 0;
}
