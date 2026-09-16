using System.Text;
using Cosmos.Kernel.System.Diagnostics;

namespace Windose.System.System_Calls
{

    public static class SystemLogger
    {
        private static StringBuilder logBuilder = new StringBuilder();

        static SystemLogger()
        {
            logBuilder.AppendLine($"<System Log Started at {DateTime.Now.ToString("HH:mm:ss")}>");
        }

        public static void WriteLine(string source, string message, ConsoleMessageType type = ConsoleMessageType.Log)
        {
            Log.WriteString($"[{type}] [{source}] {message}\n");
            logBuilder.AppendLine($"<{DateTime.Now.ToString("HH:mm:ss")}> [{type}] [{source}] {message}");

            if (type == ConsoleMessageType.Fatal)
            {
                Dump();
            }
        }

        public static void Dump()
        {
            Directory.CreateDirectory("/mnt/console");
            File.WriteAllText($"/mnt/console/{DateTime.Now.ToString("yyyyMMdd-HHmmss")}.log", logBuilder.ToString());
        }

    }

    public enum ConsoleMessageType
    {
        Log,
        Warning,
        Error,
        Fatal
    }
}
