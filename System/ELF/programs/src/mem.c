#include <stdio.h>
#include <string.h>
#include "kernel.h"

int main(int argc, char **argv)
{
    char drivebuf[256];
    int n, off, i;
    (void)argc; (void)argv;

    printf("Memory and Storage Information:\n\n");
    n = k_list_drives(drivebuf, (int)sizeof(drivebuf));
    if (n > 0) {
        off = 0;
        printf("  Mount         Total        Free\n");
        printf("  -----------   ----------   ----------\n");
        for (i = 0; i < n; i++) {
            char *mount = drivebuf + off;
            DriveStat st;
            off += (int)strlen(mount) + 1;
            if (k_drive_stat(mount, &st) == 0) {
                unsigned long long tot = ((unsigned long long)(unsigned)st.total_hi << 32) | (unsigned)st.total_lo;
                unsigned long long fr = ((unsigned long long)(unsigned)st.free_hi << 32) | (unsigned)st.free_lo;
                printf("  %-11s   %6llu MB   %6llu MB\n", mount, tot / (1024 * 1024), fr / (1024 * 1024));
            } else {
                printf("  %-11s   [offline]\n", mount);
            }
        }
    } else {
        printf("  No mounted volumes found.\n");
    }
    return 0;
}
