#include "kapi.h"

// A window with a label and a button; each click updates the label. Closing the window exits.
KABI void _start(KernelApi *k) {
    handle_t win = k->create_window(320, 200, "ELF demo");
    if (!win) { kputs(k, "window creation failed (is WindoseBridge filled in?)\n"); return; }

    handle_t label = k->create_label(win, 20, 20, "Press the button");
    handle_t btn   = k->create_button(win, 20, 60, 140, 32, "Click me");
    if (!label || !btn) { kputs(k, "component creation failed\n"); k->close(win); return; }

    int clicks = 0;
    char text[32];

    for (;;) {
        Event e;
        while (k->poll_event(&e)) {
            if (e.type == EV_CLOSE && e.handle == win) { k->close(win); return; }
            if (e.type == EV_CLICK && e.handle == btn) {
                clicks++;
                int n = 0;
                const char *p = "Clicks: ";
                while (*p) text[n++] = *p++;
                n += kitoa(clicks, text + n);
                text[n] = 0;
                k->set_text(label, text);
            }
        }
        k->sleep(10);       // don't spin at 100% CPU
    }
}
