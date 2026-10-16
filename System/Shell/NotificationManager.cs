using System.Drawing;

/// <summary>
/// System-wide notifications: toasts (auto-dismiss) plus a history center.
/// </summary>
public static class NotificationManager
{
    public sealed class Notification
    {
        public string Title = "";
        public string Text = "";
        public int Tick;
    }

    private static readonly List<Notification> history = new List<Notification>();
    private static readonly List<ToastWindow> toasts = new List<ToastWindow>();
    public const int MaxHistory = 50;

    public static int UnreadCount { get; private set; }

    public static event Action Changed;

    public static void Notify(string title, string text)
    {
        Notification n = new Notification
        {
            Title = title ?? "",
            Text = text ?? "",
            Tick = Environment.TickCount,
        };
        lock (history)
        {
            history.Insert(0, n);
            while (history.Count > MaxHistory) history.RemoveAt(history.Count - 1);
        }
        UnreadCount++;
        try { Changed?.Invoke(); } catch { }

        int y = 8;
        lock (toasts)
        {
            for (int i = 0; i < toasts.Count; i++)
                if (toasts[i] != null && toasts[i].Visible) y += toasts[i].Height + 8;
        }
        ToastWindow toast = ToastWindow.Show(Global.screenWidth - 308, y, n);
        lock (toasts) toasts.Add(toast);
    }

    public static Notification[] History()
    {
        lock (history) return history.ToArray();
    }

    public static void MarkRead()
    {
        UnreadCount = 0;
        try { Changed?.Invoke(); } catch { }
    }

    public static void Clear()
    {
        lock (history) history.Clear();
        MarkRead();
    }

    private static void ForgetToast(ToastWindow toast)
    {
        lock (toasts) toasts.Remove(toast);
    }

    private sealed class ToastWindow : Window
    {
        private readonly int bornTick;
        private const int LifetimeMs = 4000;

        public ToastWindow(int x, int y, Notification n) : base(x, y, 300, 76, n.Title, false)
        {
            canResize = false;
            canMaximize = false;
            canMove = false;
            showInTaskbar = false;
            zLayer = DrawLayer.Popup;
            bornTick = Environment.TickCount;

            Label body = new Label(8, 8, 284, 56)
            {
                text = n.Text,
                fontSize = 14,
                useBackground = false,
                clampSize = false,
                Margin = new Thickness(0),
                horizontalAlignment = HorizontalAlignment.Left,
                verticalAlignment = VerticalAlignment.Top,
            };
            AddChild(body);
        }

        public static ToastWindow Show(int x, int y, Notification n)
        {
            ToastWindow toast = new ToastWindow(x, y, n);
            WindowManager.Register(toast);
            return toast;
        }

        public override void Update()
        {
            base.Update();
            if (unchecked(Environment.TickCount - bornTick) >= LifetimeMs)
            {
                ForgetToast(this);
                WindowManager.Close(this);
            }
        }

        public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
        {
            if (mouse.left == MouseEvents.Press)
            {
                ForgetToast(this);
                WindowManager.Close(this);
                return true;
            }
            return base.HandleInput(mouseX, mouseY, mouse);
        }

        public override string GetComponentName() => "Toast";
    }
}
