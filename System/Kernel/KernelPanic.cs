using System.Drawing;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics.Fonts;

public static class KernelPanic
{
    private static bool installed;
    private static bool active;

    public static void Install()
    {
        if (installed) return;
        installed = true;
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
    }

    private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        Show("UNHANDLED_KERNEL_EXCEPTION", args.ExceptionObject as Exception);
    }

    public static void Show(string stopCode, Exception exception = null)
    {
        string message = exception?.Message ?? "No additional information is available.";
        Show(stopCode, message);
    }

    public static void Show(string stopCode, string message)
    {
        if (active)
        {
            InternalCpu.DisableInterrupts();
            while (true) InternalCpu.Halt();
        }

        active = true;
        TryWriteSerial(stopCode, message);

        try
        {
            DrawCrashScreen(stopCode ?? "UNKNOWN_STOP", message ?? "Unknown fatal error");
        }
        catch (Exception drawException)
        {
            try { Serial.WriteString("Could not draw crash screen: " + drawException.Message + "\n"); }
            catch { }
        }

        InternalCpu.DisableInterrupts();
        while (true) InternalCpu.Halt();
    }

    private static void DrawCrashScreen(string stopCode, string message)
    {
        if (Windose.Kernel.canvas == null) return;

        var canvas = Windose.Kernel.canvas;
        Font font = PCScreenFont.DefaultFont;
        Color background = Color.FromArgb(0, 0, 170);
        Color foreground = Color.White;

        canvas.DrawFilledRectangle(background, 0, 0, canvas.Width, canvas.Height);
        canvas.DrawString("Windose", font, foreground, 48, 42);
        canvas.DrawString("A fatal system error has occurred.", font, foreground, 48, 82);
        canvas.DrawString("The system has been halted to prevent further damage.", font, foreground, 48, 106);
        canvas.DrawString("STOP: " + Trim(stopCode, 72), font, foreground, 48, 154);
        canvas.DrawString(Trim(message, 88), font, foreground, 48, 178);
        canvas.DrawString("Restart the computer to continue.", font, foreground, 48, 226);
        canvas.Display();
    }

    private static void TryWriteSerial(string stopCode, string message)
    {
        try
        {
            Serial.WriteString("\n=== WINDOSE KERNEL PANIC ===\n");
            Serial.WriteString("STOP: " + (stopCode ?? "UNKNOWN_STOP") + "\n");
            Serial.WriteString((message ?? "Unknown fatal error") + "\n");
        }
        catch { }
    }

    private static string Trim(string value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value)) return "Unknown fatal error";
        if (value.Length <= maximumLength) return value;
        return value.Substring(0, maximumLength - 3) + "...";
    }
}
