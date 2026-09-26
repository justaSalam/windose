using System;
using System.Collections.Generic;
using System.Text;

namespace Windose.System.ABI.WIN
{
    public static class Win64
    {
        // Core
        public const string CoreLog = "core.log";
        public const string CoreExit = "core.exit";

        // Windows
        public const string WindowCreate = "wnd.create";
        public const string WindowDestroy = "wnd.destroy";
        public const string WindowSetTitle = "wnd.set_title";
        public const string WindowShow = "wnd.show";

        // UI
        public const string UiCreate = "ui.create";
        public const string UiDestroy = "ui.destroy";
        public const string UiSetText = "ui.set_text";
        public const string UiSetBounds = "ui.set_bounds";
        public const string UiAddChild = "ui.add_child";

        // Events
        public const string EventPoll = "event.poll";
    }
}
