using Cosmos.Kernel.System.Graphics;
using System.Drawing;

public class Tray : Window
{

    private static GridPanel panel;
    public Tray(int x, int y) : base(x, y, 160, 160, "Taskbar Tray", false)
    {
        zLayer = DrawLayer.Popup;
        Visible = false;
        canResize = false;
        canMove = false;
        showInTaskbar = false;

        panel = new GridPanel(0, 0, Width, Height)
        {
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            cellHeight = 32,
            cellWidth = 32,
            spacing = 4,
            Padding = new Thickness(8)

        };
        AddChild(panel);


        AddTrayIcon(new Button(new Png("/mnt/System/Icons/cable.png"), 0, 0, 32, 32), TrayAction);
        AddTrayIcon(new Button(new Png("/mnt/System/Icons/calculator.png"), 0, 0, 32, 32), TrayAction);
        AddTrayIcon(new Button(new Png("/mnt/System/Icons/chart.png"), 0, 0, 32, 32), TrayAction);
        AddTrayIcon(new Button(new Png("/mnt/System/Icons/directx.png"), 0, 0, 32, 32), TrayAction);
        AddTrayIcon(new Button(new Png("/mnt/System/Icons/odbc.png"), 0, 0, 32, 32), TrayAction);
    }

    public static void AddTrayIcon(Component component, Action action)
    {
        if (component == null)
        {
            return;
        }

        component.leftClickAction += action;
        panel.AddGridChild(component);
    }

    private void TrayAction()
    {
        WindowManager.Register(new PerformanceMonitor(100, 100));
    }

    public override void DrawLocal()
    {
        DrawRaisedRectangle(0, 0, Width, Height);

        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            DrawChild(child);
        }
    }


    public override void OnLoseFocus()
    {
        Visible = false;
    }
    public override string GetComponentName() => "Tray";
}
