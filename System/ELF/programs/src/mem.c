#include <stdio.h>
#include <string.h>
#include <stdint.h>
#include "kernel.h"

// CLI utility: Displays physical system memory (RAM) statistics and
// mounted storage volumes with ASCII usage meters.

static void print_bar(int percent, int width)
{
    int filled = (percent * width) / 100;
    int i;
    printf("[");
    for (i = 0; i < width; i++) {
        if (i < filled) putchar('#');
        else if (i == filled && filled > 0 && filled < width) putchar('>');
        else putchar('.');
    }
    printf("] %d%%\n", percent);
}

int main(int argc, char **argv)
{
    uint64_t total = 0, used = 0, free = 0;
    char drivebuf[256];
    int n, off, i;
    (void)argc; (void)argv;

    printf("====================================================\n");
    printf("             System Memory & Storage                \n");
    printf("====================================================\n\n");

    // Physical RAM metrics via KernelApi v15
    if (k_sys_mem(&total, &used, &free) == 0 && total > 0) {
        unsigned long long tot_mb = total / (1024ULL * 1024ULL);
        unsigned long long usd_mb = used / (1024ULL * 1024ULL);
        unsigned long long fre_mb = free / (1024ULL * 1024ULL);
        int pct = (int)((used * 100ULL) / total);

        printf("Physical Memory (RAM):\n");
        printf("  Total:      %6llu MB  (%llu bytes)\n", tot_mb, (unsigned long long)total);
        printf("  In Use:     %6llu MB  (%llu bytes)\n", usd_mb, (unsigned long long)used);
        printf("  Available:  %6llu MB  (%llu bytes)\n", fre_mb, (unsigned long long)free);
        printf("  Usage:      ");
        print_bar(pct, 24);
        printf("\n");
    } else {
        printf("Physical Memory: Metrics unavailable\n\n");
    }

    // Storage Volumes
    printf("Mounted Volumes:\n");
    n = k_list_drives(drivebuf, (int)sizeof(drivebuf));
    if (n > 0) {
        off = 0;
        printf("  Mount         Total        Free         Used\n");
        printf("  -----------   ----------   ----------   ---------------------\n");
        for (i = 0; i < n; i++) {
            char *mount = drivebuf + off;
            DriveStat st;
            off += (int)strlen(mount) + 1;
            if (k_drive_stat(mount, &st) == 0) {
                unsigned long long tot = ((unsigned long long)(unsigned)st.total_hi << 32) | (unsigned)st.total_lo;
                unsigned long long fr = ((unsigned long long)(unsigned)st.free_hi << 32) | (unsigned)st.free_lo;
                unsigned long long usd = tot >= fr ? tot - fr : 0;
                int pct = tot > 0 ? (int)((usd * 100ULL) / tot) : 0;
                int b;

                printf("  %-11s   %6llu MB   %6llu MB   %3d%% [",
                       mount, tot / (1024 * 1024), fr / (1024 * 1024), pct);
                for (b = 0; b < 10; b++) {
                    putchar(b < (pct / 10) ? '=' : ' ');
                }
                printf("]\n");
            } else {
                printf("  %-11s   [offline]\n", mount);
            }
        }
    } else {
        printf("  No mounted volumes found.\n");
    }
    printf("\n");
    return 0;
}
