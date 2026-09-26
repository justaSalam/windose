using System;
using System.Collections.Generic;
using System.Text;
using Wacs.Core.Runtime;
using Windose.System.System_Calls;

namespace Windose.System.ABI.WIN
{
    public sealed class WinHost
    {
        private readonly WasmRuntime runtime;
        public HandleTable Handles { get; } = new();


        public WinHost(WasmRuntime runtime)
        {
            this.runtime = runtime;
        }

        public void Register()
        {
            runtime.BindHostFunction<Action<int>>(("windose", "core.log"), value =>
            {
                SystemLogger.WriteLine("WACS HOST", $"Recieved: {value}", ConsoleMessageType.Log, true);
            });
            // WACS host-function registration goes here.
        }

    }
}
