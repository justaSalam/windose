/// <summary>
/// Small calendar popup above the taskbar clock. Toggled by clicking the clock.
/// </summary>
using System.Drawing;

public class CalendarFlyout : Window
{
    private static CalendarFlyout instance;

    private CalendarFlyout() : base(Global.screenWidth - 248, 0, 240, 250, "Calendar", false)
    {
        canResize = false;
        canMaximize = false;
        canMove = false;
        showInTaskbar = false;
        zLayer = DrawLayer.Popup;

        Calendar calendar = new Calendar(8, 8, 224, 200)
        {
            clampSize = false,
            Margin = new Thickness(0),
        };
        Button today = new Button("Today", 8, 214, 224, 26)
        {
            clampSize = false,
            Margin = new Thickness(0),
        };
        today.leftClickAction = () =>
        {
            calendar.SelectedDate = DateTime.UtcNow.Date;
        };
        AddChild(calendar);
        AddChild(today);
    }

    public static void Toggle()
    {
        if (instance == null)
        {
            instance = new CalendarFlyout();
            WindowManager.Register(instance);
        }
        int h = 250;
        int y = Math.Max(0, Global.screenHeight - (Explorer.taskbar != null ? Explorer.taskbar.Height : 30) - h - 4);
        instance.ApplyBounds(new Rectangle(Global.screenWidth - 248, y, 240, h));
        instance.Visible = !instance.Visible;
    }

    public override string GetComponentName() => "CalendarFlyout";
}
