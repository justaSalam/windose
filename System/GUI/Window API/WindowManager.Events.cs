using System.Drawing;
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

    public static void PostInvalidate(Component component)
    {
        PostMessage(UiMessage.ForInvalidate(component));
    }

    public static void PostInvalidate(Rectangle rectangle)
    {
        PostMessage(UiMessage.ForInvalidate(rectangle));
    }

    public static void PostLayoutChanged(Component component)
    {
        PostMessage(new UiMessage
        {
            Type = UiMessageType.LayoutChanged,
            Target = component
        });
    }

    public static void PostFocus(Window window)
    {
        PostMessage(UiMessage.ForWindow(UiMessageType.FocusWindow, window));
    }

    private void DispatchMessages()
    {
        long startedAt = PerformanceMetrics.Now;
        List<UiMessage> pending;
        lock (messageQueueLock)
        {
            int count = Math.Min(128, messageQueue.Count);
            if (count == 0)
            {
                PerformanceMetrics.AddMessages(startedAt);
                return;
            }
            pending = messageQueue.GetRange(0, count);
            messageQueue.RemoveRange(0, count);
        }

        for (int i = 0; i < pending.Count; i++)
        {
            UiMessage message = pending[i];
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

            case UiMessageType.InvalidateComponent:
                if (message.Target != null)
                    Invalidate(message.Target);
                break;

            case UiMessageType.InvalidateRectangle:
                Invalidate(message.Rectangle);
                break;

            case UiMessageType.LayoutChanged:
                if (message.Target != null)
                {
                    message.Target.ResolveChildren();
                    message.Target.MarkDirty();
                }
                break;

            case UiMessageType.FocusWindow:
                if (message.Window != null)
                {
                    BringToFront(message.Window);
                    SetFocusedWindow(message.Window);
                }
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

    public static void PostCommand<T>(string command, object data = null) where T : Window
    {
        T window = FindWindow<T>();
        if (window == null) return;

        PostCommand(command, target: window, data: data);
    }
}
