using Windose;

public partial class WindowManager
{
    private static readonly Queue<Action> uiActions = new Queue<Action>();
    private static readonly object uiQueueLock = new object();

    /// <summary>
    /// Enqueues an action to be executed safely on the WindowManager UI thread.
    /// </summary>
    public static void RunOnUI(Action action)
    {
        if (action == null) return;
        lock (uiQueueLock) uiActions.Enqueue(action);
    }

    /// <summary>
    /// Thread-safe post for registering a window.
    /// </summary>
    public static void PostRegister(Window window)
    {
        if (window == null) return;
        RunOnUI(() => RegisterNow(window));
    }

    /// <summary>
    /// Thread-safe post for closing a window.
    /// </summary>
    public static void PostClose(Window window)
    {
        if (window == null) return;
        RunOnUI(() => CloseNow(window));
    }

    /// <summary>
    /// Compatibility helper for running actions on the UI thread.
    /// </summary>
    public static void PostCommand(string command, Action? action = null, Component? target = null, object? data = null)
    {
        if (action != null)
        {
            RunOnUI(() =>
            {
                action();
                target?.MarkDirty();
            });
        }
    }

    private void DispatchMessages()
    {
        long startedAt = PerformanceMetrics.Now;

        while (true)
        {
            Action? action = null;
            lock (uiQueueLock)
            {
                if (uiActions.Count > 0)
                    action = uiActions.Dequeue();
            }

            if (action == null) break;

            try
            {
                action();
            }
            catch (Exception exception)
            {
                FailApplication(focusedWindow, "executing UI action", exception);
            }
        }

        PerformanceMetrics.AddMessages(startedAt);
    }

    public static T? FindWindow<T>() where T : Window
    {
        lock (windows)
        {
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                if (windows[i] is T window)
                    return window;
            }
        }

        return null;
    }
}
