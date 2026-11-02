using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using System.Drawing;
using Windose.Programs;
using Windose.System.Kernel;

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
        process.processType = ProcessType.System;

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

        InitDefaultIcons();
    }

    private void InitDefaultIcons()
    {
        // 1. Task Manager
        MenuPopup taskMgrContext = new MenuPopup(120, 48);
        taskMgrContext.AddItem("Task Manager", () => LaunchTracker.Start(() => new PerformanceMonitor(180, 120)));
        taskMgrContext.AddItem("Close", () => taskMgrContext.Hide());
        Button taskMgrBtn = new Button(new Png("/mnt/System/Icons/computer_taskmgr.png"), 0, 0, 32, 32);
        AddTrayIcon(taskMgrBtn, taskMgrContext, () => LaunchTracker.Start(() => new PerformanceMonitor(180, 120)), "Task Manager");

        // 2. Device Manager
        MenuPopup devContext = new MenuPopup(120, 48);
        devContext.AddItem("Device Manager", () => LaunchTracker.Start(() => new DeviceManager(100, 100, 600, 350)));
        devContext.AddItem("Close", () => devContext.Hide());
        Button devBtn = new Button(new Png("/mnt/System/Icons/mci_devices.png"), 0, 0, 32, 32);
        AddTrayIcon(devBtn, devContext, () => LaunchTracker.Start(() => new DeviceManager(100, 100, 600, 350)), "Device Manager");

        // 3. Display Settings
        MenuPopup dispContext = new MenuPopup(120, 48);
        dispContext.AddItem("Display Settings", () => LaunchTracker.Start(() => new DisplaySettings()));
        dispContext.AddItem("Close", () => dispContext.Hide());
        Button dispBtn = new Button(new Png("/mnt/System/Icons/monitor_gear.png"), 0, 0, 32, 32);
        AddTrayIcon(dispBtn, dispContext, () => LaunchTracker.Start(() => new DisplaySettings()), "Display Settings");

        // 4. DirectX Graphics Test
        MenuPopup gfxContext = new MenuPopup(120, 48);
        gfxContext.AddItem("Graphics Test", () => LaunchTracker.Start(() => new GraphicsEngine()));
        gfxContext.AddItem("Close", () => gfxContext.Hide());
        Button gfxBtn = new Button(new Png("/mnt/System/Icons/directx.png"), 0, 0, 32, 32);
        AddTrayIcon(gfxBtn, gfxContext, () => LaunchTracker.Start(() => new GraphicsEngine()), "DirectX Graphics Engine");
    }

    public static void AddTrayIcon(Component component, MenuPopup? context = null, Action? action = null, string tooltipText = "Tray Icon")
    {
        if (component == null) return;

        if (action != null)
        {
            component.leftClickAction += action;
        }

        if (context != null)
        {
            component.rightClickAction += () =>
            {
                int cx = Math.Min(MouseManager.X, Math.Max(0, Global.screenWidth - context.Width));
                int maxY = Explorer.taskbar != null ? Explorer.taskbar.Y : Global.screenHeight;
                int cy = Math.Min(MouseManager.Y, Math.Max(0, maxY - context.Height));
                context.ShowAt(cx, cy);
            };
        }

        if (!string.IsNullOrEmpty(tooltipText))
        {
            TooltipService.Attach(component, tooltipText);
        }

        panel?.AddGridChild(component);
    }

    public static void RemoveTrayIcon(Component component)
    {
        if (component == null || panel == null) return;
        panel.RemoveChild(component);
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
