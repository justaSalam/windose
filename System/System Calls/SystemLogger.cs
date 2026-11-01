using System.Text;
using Cosmos.Kernel.System.Diagnostics;

namespace Windose.System.System_Calls
{
    public static class SystemLogger
    {
        private const int MaxLogCapacity = 65536; // 64 KB rolling log buffer to prevent unbounded RAM usage
        private static readonly StringBuilder logBuilder = new StringBuilder();

        static SystemLogger()
        {
            logBuilder.AppendLine($"<System Log Started at {DateTime.Now.ToString("HH:mm:ss")}>");
        }

        public static void WriteLine(string source, string message, ConsoleMessageType type = ConsoleMessageType.Log, bool consoleOutput = false)
        {
            if (consoleOutput)
            {
                Console.WriteLine($"[{source}] {message}");
            }
            Log.WriteString($"[{type}] [{source}] {message}\n");

            lock (logBuilder)
            {
                if (logBuilder.Length > MaxLogCapacity)
                {
                    // Discard the older half to stay bounded in memory
                    logBuilder.Remove(0, MaxLogCapacity / 2);
                }
                logBuilder.AppendLine($"<{DateTime.Now.ToString("HH:mm:ss")}> [{type}] [{source}] {message}");
            }

            if (type == ConsoleMessageType.Fatal)
            {
                Dump();
            }
        }

        public static void Dump()
        {
            try
            {
                Directory.CreateDirectory("/mnt/console");
                string content;
                lock (logBuilder) content = logBuilder.ToString();
                File.WriteAllText($"/mnt/console/{DateTime.Now.ToString("yyyyMMdd-HHmmss")}.log", content);
            }
            catch { }
        }

        public static void Dump(string name)
        {
            try
            {
                Directory.CreateDirectory("/mnt/console");
                string content;
                lock (logBuilder) content = logBuilder.ToString();
                File.WriteAllText($"/mnt/console/{name}.log", content);
            }
            catch { }
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
