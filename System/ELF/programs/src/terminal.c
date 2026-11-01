#include <stdio.h>
#include "kernel.h"

// Command Prompt - ELF implementation matching the C# CommandPrompt Window
int main(int argc, char **argv)
{
    (void)argc; (void)argv;

    handle_t win = k_create_window_with_icon(720, 460, "Command Prompt", "/mnt/System/Icons/console_prompt.png");
    if (!win) {
        puts("Failed to create Command Prompt window.");
        return 1;
    }

    handle_t term = k_create_terminal(win, 0, 0, 720, 460);
    if (!term) {
        puts("Failed to create terminal control.");
        k_close(win);
        return 1;
    }

    for (;;) {
        Event e;
        while (k_poll_event(&e)) {
            if (e.type == EV_CLOSE && e.handle == win) {
                k_close(win);
                return 0;
            }
        }
        k_sleep(20);
    }
}
