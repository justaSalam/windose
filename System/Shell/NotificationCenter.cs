using Cosmos.Kernel.System.Graphics;

/// <summary>
/// Notification history window, opened from the tray icon.
/// </summary>
public class NotificationCenter : Window
{
    private readonly ListView list;

    public NotificationCenter() : base(120, 120, 420, 360, "Notifications", true)
    {
        list = new ListView(8, 8, Width - 16, Height - 80)
        {
            clampSize = false,
            Margin = new Thickness(0),
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Top,
        };
        list.AddColumn("Notification", Width - 16);
        Button clear = new Button("Clear All", 8, Height - 64, 100, 26)
        {
            clampSize = false,
            Margin = new Thickness(0),
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Top,
        };
        clear.leftClickAction = () => { NotificationManager.Clear(); Refresh(); };
        Button close = new Button("Close", 116, Height - 64, 100, 26)
        {
            clampSize = false,
            Margin = new Thickness(0),
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Top,
        };
        close.leftClickAction = () => WindowManager.Close(this);
        AddChild(list);
        AddChild(clear);
        AddChild(close);
        Refresh();
    }

    public void Refresh()
    {
        list.ClearItems();
        NotificationManager.Notification[] items = NotificationManager.History();
        for (int i = 0; i < items.Length; i++)
            list.AddItem(new string[] { items[i].Title + ": " + items[i].Text });
        NotificationManager.MarkRead();
    }

    public override string GetComponentName() => "NotificationCenter";
}
