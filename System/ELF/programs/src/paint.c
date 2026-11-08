/**
 * Windose OS Native Paint (ELF)
 * Interactive raster graphics editor with Brush, Eraser, Fill, Palettes, and BMP export.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

#define CW 630
#define CH 410

#define TOOL_BRUSH  0
#define TOOL_ERASER 1
#define TOOL_FILL   2

static handle_t win = 0;
static handle_t cv = 0;
static handle_t h_btn_brush = 0;
static handle_t h_btn_eraser = 0;
static handle_t h_btn_fill = 0;
static handle_t h_btn_clear = 0;
static handle_t h_btn_sz1 = 0;
static handle_t h_btn_sz2 = 0;
static handle_t h_btn_sz3 = 0;
static handle_t h_btn_sz4 = 0;
static handle_t h_btn_save = 0;
static handle_t h_lbl_status = 0;
static handle_t h_lbl_curcol = 0;

static handle_t color_btns[10];
static const unsigned color_values[10] = {
    0xFF000000, // Black
    0xFF7F7F7F, // Gray
    0xFFFF0000, // Red
    0xFFFF7F27, // Orange
    0xFFFFF200, // Yellow
    0xFF22B14C, // Green
    0xFF00A2E8, // Cyan
    0xFF3F48CC, // Blue
    0xFFA349A4, // Purple
    0xFFFFFFFF  // White
};
static const char *color_names[10] = {
    "Black", "Gray", "Red", "Orange", "Yellow",
    "Green", "Cyan", "Blue", "Purple", "White"
};

static unsigned *pixels = NULL;
static int current_tool = TOOL_BRUSH;
static unsigned current_color = 0xFF000000;
static int current_color_idx = 0;
static int brush_size = 3;
static int is_drawing = 0;
static int last_x = -1, last_y = -1;

static void update_status_ui(void) {
    char stat[64];
    const char *tool_str = (current_tool == TOOL_BRUSH) ? "Brush" :
                           (current_tool == TOOL_ERASER) ? "Eraser" : "Fill";
    snprintf(stat, sizeof(stat), "Tool: %s | %dpx", tool_str, brush_size);
    k_set_text(h_lbl_status, stat);

    char cstat[64];
    snprintf(cstat, sizeof(cstat), "Color: %s", color_names[current_color_idx]);
    k_set_text(h_lbl_curcol, cstat);
}

static void draw_brush_point(int cx, int cy, unsigned col, int sz) {
    int r = sz / 2;
    if (r < 1) {
        if (cx >= 0 && cx < CW && cy >= 0 && cy < CH)
            pixels[cy * CW + cx] = col;
        return;
    }
    for (int dy = -r; dy <= r; dy++) {
        int py = cy + dy;
        if (py < 0 || py >= CH) continue;
        for (int dx = -r; dx <= r; dx++) {
            int px = cx + dx;
            if (px < 0 || px >= CW) continue;
            if (dx * dx + dy * dy <= r * r + r) {
                pixels[py * CW + px] = col;
            }
        }
    }
}

static void draw_line(int x0, int y0, int x1, int y1) {
    unsigned col = (current_tool == TOOL_ERASER) ? 0xFFFFFFFF : current_color;
    int dx = (x1 > x0) ? (x1 - x0) : (x0 - x1);
    int sx = (x0 < x1) ? 1 : -1;
    int dy = (y1 > y0) ? (y0 - y1) : (y1 - y0);
    int sy = (y0 < y1) ? 1 : -1;
    int err = dx + dy, e2;
    for (;;) {
        draw_brush_point(x0, y0, col, brush_size);
        if (x0 == x1 && y0 == y1) break;
        e2 = 2 * err;
        if (e2 >= dy) { err += dy; x0 += sx; }
        if (e2 <= dx) { err += dx; y0 += sy; }
    }
}

static void flood_fill(int start_x, int start_y, unsigned fill_col) {
    if (start_x < 0 || start_x >= CW || start_y < 0 || start_y >= CH) return;
    unsigned target_col = pixels[start_y * CW + start_x];
    if (target_col == fill_col) return;

    #define QUEUE_MAX (64 * 1024)
    static int qx[QUEUE_MAX];
    static int qy[QUEUE_MAX];
    int head = 0, tail = 0;

    qx[tail] = start_x;
    qy[tail] = start_y;
    tail++;
    pixels[start_y * CW + start_x] = fill_col;

    while (head < tail && tail < QUEUE_MAX - 4) {
        int cx = qx[head];
        int cy = qy[head];
        head++;

        static const int dirs[4][2] = {{1,0}, {-1,0}, {0,1}, {0,-1}};
        for (int i = 0; i < 4; i++) {
            int nx = cx + dirs[i][0];
            int ny = cy + dirs[i][1];
            if (nx >= 0 && nx < CW && ny >= 0 && ny < CH) {
                if (pixels[ny * CW + nx] == target_col) {
                    pixels[ny * CW + nx] = fill_col;
                    qx[tail] = nx;
                    qy[tail] = ny;
                    tail++;
                    if (tail >= QUEUE_MAX) break;
                }
            }
        }
    }
}

static void clear_canvas(void) {
    for (int i = 0; i < CW * CH; i++) {
        pixels[i] = 0xFFFFFFFF;
    }
    k_draw_pixels(cv, 0, 0, CW, CH, pixels, CW * CH * 4);
    k_set_text(h_lbl_status, "Canvas cleared");
}

static void save_bmp(const char *path) {
    unsigned char header[54];
    memset(header, 0, sizeof(header));
    header[0] = 'B'; header[1] = 'M';
    int w = CW, h = CH;
    int img_size = w * h * 4;
    int file_size = 54 + img_size;
    memcpy(header + 2, &file_size, 4);
    int offset = 54;
    memcpy(header + 10, &offset, 4);
    int dib_size = 40;
    memcpy(header + 14, &dib_size, 4);
    memcpy(header + 18, &w, 4);
    int neg_h = -h;
    memcpy(header + 22, &neg_h, 4);
    short planes = 1;
    memcpy(header + 26, &planes, 2);
    short bpp = 32;
    memcpy(header + 28, &bpp, 2);
    memcpy(header + 34, &img_size, 4);

    char *buf = (char *)malloc(file_size);
    if (!buf) {
        k_set_text(h_lbl_status, "Error: Out of memory");
        return;
    }
    memcpy(buf, header, 54);
    memcpy(buf + 54, pixels, img_size);
    int r = k_write_file(path, buf, file_size);
    free(buf);

    if (r >= 0) {
        k_set_text(h_lbl_status, "Saved to /mnt/drawing.bmp");
    } else {
        k_set_text(h_lbl_status, "Error: Failed to save BMP");
    }
}

int main(int argc, char **argv) {
    win = k_create_window_ex(100, 100, 650, 500, "Paint", WIN_FLAG_NO_RESIZE);
    if (!win) return 1;
    k_set_icon(win, "/mnt/System/Icons/paint.png");

    /* Row 1 tools */
    h_btn_brush = k_create_button(win, 10, 8, 50, 28, "Brush");
    h_btn_eraser = k_create_button(win, 65, 8, 55, 28, "Eraser");
    h_btn_fill = k_create_button(win, 125, 8, 40, 28, "Fill");
    h_btn_clear = k_create_button(win, 170, 8, 50, 28, "Clear");

    h_btn_sz1 = k_create_button(win, 230, 8, 35, 28, "1px");
    h_btn_sz2 = k_create_button(win, 270, 8, 35, 28, "3px");
    h_btn_sz3 = k_create_button(win, 310, 8, 35, 28, "6px");
    h_btn_sz4 = k_create_button(win, 350, 8, 45, 28, "10px");

    h_btn_save = k_create_button(win, 405, 8, 50, 28, "Save");
    h_lbl_status = k_create_label(win, 465, 14, "Tool: Brush | 3px");

    /* Row 2 colors */
    for (int i = 0; i < 10; i++) {
        color_btns[i] = k_create_button(win, 10 + i * 52, 42, 48, 28, color_names[i]);
    }
    h_lbl_curcol = k_create_label(win, 535, 48, "Color: Black");

    /* Row 3 Canvas */
    cv = k_create_canvas(win, 10, 76, CW, CH);
    if (!cv) { k_close(win); return 1; }

    pixels = (unsigned *)malloc(CW * CH * sizeof(unsigned));
    if (!pixels) { k_close(win); return 1; }

    for (int i = 0; i < CW * CH; i++) pixels[i] = 0xFFFFFFFF;
    k_draw_pixels(cv, 0, 0, CW, CH, pixels, CW * CH * 4);

    for (;;) {
        Event e;
        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE && e.handle == win) {
                k_close(win);
                free(pixels);
                return 0;
            }

            if (e.type == EV_CLICK) {
                if (e.handle == h_btn_brush) {
                    current_tool = TOOL_BRUSH;
                    update_status_ui();
                } else if (e.handle == h_btn_eraser) {
                    current_tool = TOOL_ERASER;
                    update_status_ui();
                } else if (e.handle == h_btn_fill) {
                    current_tool = TOOL_FILL;
                    update_status_ui();
                } else if (e.handle == h_btn_clear) {
                    clear_canvas();
                } else if (e.handle == h_btn_sz1) {
                    brush_size = 1;
                    update_status_ui();
                } else if (e.handle == h_btn_sz2) {
                    brush_size = 3;
                    update_status_ui();
                } else if (e.handle == h_btn_sz3) {
                    brush_size = 6;
                    update_status_ui();
                } else if (e.handle == h_btn_sz4) {
                    brush_size = 10;
                    update_status_ui();
                } else if (e.handle == h_btn_save) {
                    save_bmp("/mnt/drawing.bmp");
                } else {
                    for (int i = 0; i < 10; i++) {
                        if (e.handle == color_btns[i]) {
                            current_color = color_values[i];
                            current_color_idx = i;
                            if (current_tool == TOOL_ERASER) current_tool = TOOL_BRUSH;
                            update_status_ui();
                            break;
                        }
                    }
                }
            }

            if (e.type == EV_MOUSEDOWN && (e.a >> 28) == 1 && e.handle == cv) {
                int x = e.a & 0x0FFFFFFF;
                int y = e.b;
                if (current_tool == TOOL_FILL) {
                    flood_fill(x, y, current_color);
                    k_draw_pixels(cv, 0, 0, CW, CH, pixels, CW * CH * 4);
                } else {
                    is_drawing = 1;
                    draw_line(x, y, x, y);
                    last_x = x;
                    last_y = y;
                    k_draw_pixels(cv, 0, 0, CW, CH, pixels, CW * CH * 4);
                }
            }

            if (e.type == EV_MOUSEMOVE && e.handle == cv && is_drawing) {
                int x = e.a;
                int y = e.b;
                draw_line(last_x, last_y, x, y);
                last_x = x;
                last_y = y;
                k_draw_pixels(cv, 0, 0, CW, CH, pixels, CW * CH * 4);
            }

            if (e.type == EV_MOUSEUP) {
                is_drawing = 0;
                last_x = -1;
                last_y = -1;
            }
        }
        k_sleep(10);
    }
}
