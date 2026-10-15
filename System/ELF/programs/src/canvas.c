#include <stdio.h>
#include <stdlib.h>
#include "kernel.h"

// Animated plasma on a pixel canvas. Drag with the left button to paint.
// usage: canvas [width height]
#define MAXW 512
#define MAXH 384

static unsigned palette(int v)
{
    v &= 255;
    unsigned r = (unsigned)(v < 128 ? v * 2 : 511 - v * 2);
    unsigned b = (unsigned)(v < 128 ? 255 - v * 2 : (v - 128) * 2);
    return 0xFF000000u | (r << 16) | b;
}

int main(int argc, char **argv)
{
    int W = MAXW, H = MAXH;
    if (argc > 2) { W = atoi(argv[1]); H = atoi(argv[2]); }
    if (W < 32) W = 32; if (W > MAXW) W = MAXW;
    if (H < 32) H = 32; if (H > MAXH) H = MAXH;

    handle_t win = k_create_window(W + 16, H + 16, "ELF canvas");
    if (!win) { puts("window creation failed"); return 1; }
    handle_t cv = k_create_canvas(win, 8, 8, W, H);
    if (!cv) { puts("canvas creation failed"); k_close(win); return 1; }

    unsigned *px = malloc((size_t)W * (size_t)H * 4);
    if (!px) { puts("out of memory"); k_close(win); return 1; }

    int t = 0, painting = 0, mdown = 0;
    for (;;) {
        Event e;
        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE && e.handle == win) { k_close(win); free(px); return 0; }
            if (e.type == EV_MOUSEDOWN && (e.a >> 28) == 1) { painting = 1; mdown = 1; }
            if (e.type == EV_MOUSEUP && (e.a >> 28) == 1) painting = 0;
            if (e.type == EV_KEY && (e.a == 27)) { k_close(win); free(px); return 0; }  // Esc quits
        }

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                px[y * W + x] = palette((x * x + y * y + t * 8) >> 5);

        if (painting) {
            // white dot in the middle while the button is held
            for (int y = H / 2 - 4; y < H / 2 + 4; y++)
                for (int x = W / 2 - 4; x < W / 2 + 4; x++)
                    px[y * W + x] = 0xFFFFFFFFu;
            (void)mdown;
        }

        k_draw_pixels(cv, 0, 0, W, H, px, W * H * 4);
        
        t++;
        k_sleep(33);
    }
}
