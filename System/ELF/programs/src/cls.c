#include <stdio.h>
#include "kernel.h"

int main(int argc, char **argv)
{
    (void)argc; (void)argv;
    // ANSI clear screen token handled by terminal
    printf("\033[2J");
    return 0;
}
