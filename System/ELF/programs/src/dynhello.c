#include <stdio.h>
#include <windose.h>

int main(int argc, char **argv)
{
    printf("=== Dynamic Linking Demonstration ===\n");
    printf("Successfully linked and executed with shared libc.so!\n");
    printf("Process PID: %d\n", proc_pid());
    for (int i = 0; i < argc; i++) {
        printf("  argv[%d] = %s\n", i, argv[i]);
    }
    printf("Dynamic linking is fully working on Windose!\n");
    return 0;
}
