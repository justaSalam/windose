#include <stdio.h>
#include <stdlib.h>
#include "kernel.h"

// Runs until killed. usage: spin [sleep_ms]
int main(int argc, char **argv)
{
    int ms = argc > 1 ? atoi(argv[1]) : 10;
    printf("spin started (sleep %d ms)\n", ms);
    for (;;) k_sleep(ms);      // a killed program gets K_KILLED here and exits with 137
}
