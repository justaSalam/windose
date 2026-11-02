#include <stdio.h>
#include <string.h>
#include <windose.h>

// Window with a label and a button; each click updates the label. Closing the window exits.
// usage: window [title]
int main(int argc, char **argv)
{
    const char *title = argc > 1 ? argv[1] : "ELF demo";
    handle_t win = win_create(320, 200, title);
    if (!win) { puts("window creation failed"); return 1; }

    handle_t label = label_create(win, 20, 20, "Press the button");
    handle_t btn   = button_create(win, 20, 60, 140, 32, "Click me");
    if (!label || !btn) { puts("component creation failed"); win_close(win); return 1; }

    int clicks = 0;
    char text[32];
    for (;;) {
        Event e;
        while (event_poll(&e)) {
            if (e.type == EV_CLOSE && e.handle == win) { win_close(win); return 0; }
            if (e.type == EV_CLICK && e.handle == btn) {
                clicks++;
                snprintf(text, sizeof text, "Clicks: %d", clicks);
                text_set(label, text);
            }
        }
        sys_sleep(10); // don't spin at 100% CPU
    }
}
