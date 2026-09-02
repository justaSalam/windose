using Cosmos.Kernel.System.Keyboard;
using System;
using System.Collections.Generic;
using System.Text;

namespace Windose.System.Features
{
    public struct GlobalHotkey
    {
        public KeyEvent keyEvent;
        public Action action;
    }
}
