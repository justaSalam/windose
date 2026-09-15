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
            switch (type)
            {
                case ConsoleMessageType.Log:
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"[{source}] {message}");
                    Log.WriteString($"[{source}] {message}\n");
                    break;

                case ConsoleMessageType.Warning:
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write($"[");

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write(source);

                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"] {message}");
                    Log.WriteString($"[{source}] {message}\n");

                    break;

                case ConsoleMessageType.Error:
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write($"[");

                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.Write(source);

                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"] {message}");
                    Log.WriteString($"[{source}] {message}\n");

                    break;

                case ConsoleMessageType.Fatal:
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write($"[");

                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.Write(source);

                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"] {message}");
                    Log.WriteString($"[{source}] {message}\n");

                    Dump();
                    break;

            }

            logBuilder.AppendLine($"<{DateTime.Now.ToString("HH:mm:ss")}> [{type}] [{source}] {message}");

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
