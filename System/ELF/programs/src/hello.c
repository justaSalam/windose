#include <stdio.h>
#include <windose.h>

int main(int argc, char **argv)
{
    printf("Hello from an ELF program! (pid %d)\n", proc_pid());
    for (int i = 0; i < argc; i++) printf("  argv[%d] = %s\n", i, argv[i]);
    return 0;
}
