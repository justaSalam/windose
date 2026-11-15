#include <stdio.h>
#include <string.h>
#include <math.h>
#include <stdlib.h>
#include "kernel.h"

// donut - 3D rotating ASCII donut demo
// usage: donut [frames] (default: continuous / 2000 frames)

int main(int argc, char **argv)
{
    float A = 0, B = 0;
    float i, j;
    int k;
    float z[1760];
    char b[1760];
    int max_frames = 2000;

    if (argc > 1) {
        max_frames = atoi(argv[1]);
        if (max_frames <= 0) max_frames = 2000;
    }

    for (int frame = 0; frame < max_frames; frame++) {
        memset(b, 32, 1760);
        memset(z, 0, sizeof(z));

        for (j = 0; j < 6.28f; j += 0.07f) {
            for (i = 0; i < 6.28f; i += 0.02f) {
                float c = (float)sin(i);
                float d = (float)cos(j);
                float e = (float)sin(A);
                float f = (float)sin(j);
                float g = (float)cos(A);
                float h = d + 2.0f;
                float D = 1.0f / (c * h * e + f * g + 5.0f);
                float l = (float)cos(i);
                float m = (float)cos(B);
                float n = (float)sin(B);
                float t = c * h * g - f * e;
                int x = (int)(40 + 30 * D * (l * h * m - t * n));
                int y = (int)(12 + 15 * D * (l * h * n + t * m));
                int o = x + 80 * y;
                int N = (int)(8 * ((f * e - c * d * g) * m - c * d * e - f * g - l * d * n));

                if (22 > y && y > 0 && x > 0 && 80 > x && D > z[o]) {
                    z[o] = D;
                    b[o] = ".,-~:;=!*#$@"[N > 0 ? (N < 12 ? N : 11) : 0];
                }
            }
        }

        // Clear terminal screen so the new frame animates cleanly in place
        printf("\033[2J");
        for (k = 0; k < 1760; k++) {
            putchar(k % 80 ? b[k] : 10);
        }
        putchar(10);

        A += 0.08f;
        B += 0.04f;

        if (frame + 1 < max_frames) {
            k_sleep(40);
        }
    }

    return 0;
}
