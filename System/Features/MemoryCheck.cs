using Cosmos.Kernel.System;
using Cosmos.Kernel.System.Diagnostics;
using Windose.System.System_Calls;

namespace Windose.System.Features
{
    public static class MemoryCheck
    {
        public const ulong MinRequiredMemoryMb = 32;
        private static int lastCheckTick;
        private static bool lastCheckResult = true;

        /// <summary>
        /// Checks if system has sufficient free memory to continue running safely.
        /// Throttled to execute at most once every 500ms to avoid per-frame heap diagnostics.
        /// </summary>
        public static bool Pass()
        {
            int now = Environment.TickCount;
            if (unchecked(now - lastCheckTick) < 500 && lastCheckTick != 0)
            {
                return lastCheckResult;
            }
            lastCheckTick = now;

            ulong freeMemoryMb = MemoryDiagnostics.FreePages * MemoryDiagnostics.PageSizeBytes / ByteSize.Megabyte;

            if (freeMemoryMb < MinRequiredMemoryMb)
            {
                lastCheckResult = false;
                Console.Clear();
                Console.BackgroundColor = ConsoleColor.Red;
                Console.ForegroundColor = ConsoleColor.White;
                SystemLogger.WriteLine("KERNEL", "Panic", ConsoleMessageType.Warning, true);
                SystemLogger.WriteLine("KERNEL", "Low memory warning: " + freeMemoryMb + " MB free", ConsoleMessageType.Warning, true);
                SystemLogger.WriteLine("KERNEL", "Insufficient memory available", ConsoleMessageType.Fatal, true);
                Power.Halt();
                return false;
            }

            lastCheckResult = true;
            return true;
        }
    }
}
