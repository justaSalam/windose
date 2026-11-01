using Cosmos.Kernel.System;
using Cosmos.Kernel.System.Diagnostics;
using Windose.System.System_Calls;

namespace Windose.System.Features
{
    public static class MemoryCheck
    {
        public const ulong MinRequiredMemoryMb = 32;

        /// <summary>
        /// Checks if system has sufficient free memory to continue running safely.
        /// </summary>
        public static bool Pass()
        {
            ulong freeMemoryMb = MemoryDiagnostics.FreePages * MemoryDiagnostics.PageSizeBytes / ByteSize.Megabyte;

            if (freeMemoryMb < MinRequiredMemoryMb)
            {
                Console.Clear();
                Console.BackgroundColor = ConsoleColor.Red;
                Console.ForegroundColor = ConsoleColor.White;
                SystemLogger.WriteLine("KERNEL", "Panic", ConsoleMessageType.Warning, true);
                SystemLogger.WriteLine("KERNEL", "Low memory warning: " + freeMemoryMb + " MB free", ConsoleMessageType.Warning, true);
                SystemLogger.WriteLine("KERNEL", "Insufficient memory available", ConsoleMessageType.Fatal, true);
                Power.Halt();
                return false;
            }

            return true;
        }
    }
}
