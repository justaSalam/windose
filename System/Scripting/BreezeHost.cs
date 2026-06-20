using System.IO;
using Cosmos.Kernel.Core.IO;

public static class BreezeHost
{
    public static BreezeRuntime RunSource(string source)
    {
        BreezeRuntime runtime = null;
        try
        {
            BreezeApplicationProcess process = new BreezeApplicationProcess(source);
            runtime = process.Runtime;
            ProcessManger.Start(process);
            if (runtime.LastError != null)
            {
                runtime.TerminateApplication();
                QueueError(runtime.LastError);
                return null;
            }
            return runtime;
        }
        catch (Exception exception)
        {
            runtime?.TerminateApplication();
            QueueError(exception.Message);
            return null;
        }
    }

    public static BreezeRuntime RunFile(string path)
    {
        try
        {
            return RunSource(File.ReadAllText(path));
        }
        catch (Exception exception)
        {
            QueueError("Could not load " + path + ": " + exception.Message);
            return null;
        }
    }

    public static void ShowError(string message)
    {
        QueueError(message);
    }

    private static void QueueError(string message)
    {
        string safeMessage = message ?? "Unknown script error";
        TryWriteSerial("Breeze error: " + safeMessage);

        try
        {
            WindowManager.PostCommand("breeze.showError", () => ShowErrorNow(safeMessage));
        }
        catch (Exception queueException)
        {
            TryWriteSerial("Could not queue Breeze error: " + queueException.Message);
        }
    }

    private static void TryWriteSerial(string message)
    {
        try { Serial.WriteString(message + "\n"); }
        catch { }
    }

    private static void ShowErrorNow(string message)
    {
        string visibleMessage = message ?? "Unknown script error";
        if (visibleMessage.Length > 90) visibleMessage = visibleMessage.Substring(0, 87) + "...";

        Window error = new Window(140, 140, 640, 120, "Breeze Error", true)
        {
            canMaximize = false,
            canResize = false,
        };

        Panel text = new Panel(Palette.ControlFace, 8, 34, 624, 36)
        {
            text = visibleMessage,
            fontSize = 16,
            textColor = Palette.ControlBlack,
            useBackground = true,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(8, 34, 8, 8),
        };

        error.AddChild(text);
        WindowManager.Register(error);
    }
}
