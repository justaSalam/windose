using System.Text;

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
                    break;

                case ConsoleMessageType.Warning:
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write($"[");

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write(source);

                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"] {message}");

                    break;

                case ConsoleMessageType.Error:
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write($"[");

                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.Write(source);

                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"] {message}");
                    break;

            }

            logBuilder.AppendLine($"<{DateTime.Now.ToString("HH:mm:ss")}> [{type}] [{source}] {message}");

        }

        public static void Dump()
        {
            File.WriteAllText($"/mnt/console{DateTime.Now.ToString("yyyyMMdd-HHmmss")}.log", logBuilder.ToString());
        }

    }

    public enum ConsoleMessageType
    {
        Log,
        Warning,
        Error
    }
}
