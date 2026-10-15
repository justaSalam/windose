using Windose;

public partial class WindowManager
{
    public static void PostMessage(UiMessage message)
    {
        if (message.Type == UiMessageType.None) return;
        lock (messageQueueLock) messageQueue.Add(message);
    }

    public static void PostCommand(string command, Action action = null, Component target = null, object data = null)
    {
        PostMessage(UiMessage.ForCommand(command, action, target, data));
    }

    public static void PostRegister(Window window)
    {
        PostMessage(UiMessage.ForWindow(UiMessageType.RegisterWindow, window));
    }

    public static void PostClose(Window window)
    {
        PostMessage(UiMessage.ForWindow(UiMessageType.CloseWindow, window));
    }

    private readonly List<UiMessage> dispatchScratch = new List<UiMessage>();

    private void DispatchMessages()
    {
        long startedAt = PerformanceMetrics.Now;
        dispatchScratch.Clear();
        lock (messageQueueLock)
        {
            int count = Math.Min(128, messageQueue.Count);
            if (count == 0)
            {
                PerformanceMetrics.AddMessages(startedAt);
                return;
            }
            for (int i = 0; i < count; i++)
                dispatchScratch.Add(messageQueue[i]);
            messageQueue.RemoveRange(0, count);
        }

        for (int i = 0; i < dispatchScratch.Count; i++)
        {
            UiMessage message = dispatchScratch[i];
            try { DispatchMessage(message); }
            catch (Exception exception)
            {
                Window owner = message.Window ?? message.Target?.GetOwningWindow();
                FailApplication(owner, "processing " + message.Type, exception);
            }
        }

        PerformanceMetrics.AddMessages(startedAt);
    }

    private void DispatchMessage(UiMessage message)
    {
        switch (message.Type)
        {
            case UiMessageType.Command:
                if (message.Target != null)
                    message.Target.HandleMessage(message);

                message.Action?.Invoke();
                if (message.Target != null)
                    message.Target.MarkDirty();
                break;

            case UiMessageType.RegisterWindow:
                RegisterNow(message.Window);
                break;

            case UiMessageType.CloseWindow:
                CloseNow(message.Window);
                break;
        }
    }

    public static T FindWindow<T>() where T : Window
    {
        for (int i = windows.Count - 1; i >= 0; i--)
        {
            if (windows[i] is T window)
                return window;
        }

        return null;
    }
}
