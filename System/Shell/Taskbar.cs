using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using System.Drawing;
using Windose.System.Kernel;

public class Taskbar : Component
{
    private const int TrayWidth = 120;
    public Color borderColor = Color.White;

    public List<Button> windows = new List<Button>();

    public StackPanel bar;
    public StackPanel trayPanel;
    private Button startButton;
    private Label timeLabel;

    private Button trayButton;
    private int contextX;
    private int contextY;

    public static Tray tray;
    private readonly MenuPopup contextMenu;


    public Taskbar(int x, int y, int width, int height) : base(x, y, width, height)
    {
        zLayer = DrawLayer.Taskbar;
        if (Palette.GlassEnabled)
        {
            useGlass = true;
            glassStyle = GlassStyle.AeroTaskbar();
        }
        int trayWidth = Math.Min(TrayWidth, Width);
        bar = new StackPanel(Palette.ControlFace, 0, 0, Width - trayWidth, Height)
        {
            useBackground = false,
            useBorders = false,
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Stretch,
            orientation = StackOrientation.Horizontal,
            spacing = 4,
            Margin = new Thickness(0),
            Padding = new Thickness(3),
            rightClickAction = ShowContextMenu
        };


        trayPanel = new StackPanel(Palette.ControlFace, Width - trayWidth, 0, trayWidth, Height)
        {
            useBackground = false,
            useBorders = false,
            horizontalAlignment = HorizontalAlignment.Right,
            verticalAlignment = VerticalAlignment.Stretch,
            orientation = StackOrientation.Horizontal,
            spacing = 2,
            Margin = new Thickness(0),
            Padding = new Thickness(2),
            rightClickAction = ShowContextMenu
        };

        AddChild(bar);
        AddChild(trayPanel);

        contextMenu = new MenuPopup(230, 24 * 3)
        {
            itemHeight = 18
        };
        contextMenu.AddItem("Task Manager", () => LaunchTracker.Start(() => new PerformanceMonitor(180, 120)));
        contextMenu.AddSeparator();
        contextMenu.AddItem("Minimize All Windows", WindowManager.MinimizeAllWindows);
        contextMenu.AddItem("Properties", () => LaunchTracker.Start(() => new DisplaySettings(contextX, contextY)));



        bool modern = Palette.FlatControls || Palette.GlassEnabled;
        int startBtnWidth = modern ? 48 : 72;
        string startText = modern ? "" : "Start";
        int startIconSize = modern ? 28 : 18;

        startButton = new Button(new Png("/mnt/System/Icons/start_menu_xp.png"), startText, 0, 0, startBtnWidth, Height - 4)
        {
            verticalAlignment = VerticalAlignment.Center,
            horizontalAlignment = HorizontalAlignment.Left,
            contentAlignment = HorizontalAlignment.Center,
            iconSize = startIconSize,
            iconTextGap = 4,
            textColor = Color.White,
            useBorders = !modern,
            useBackground = !modern,
            Margin = new Thickness(modern ? 2 : 1, 1, 2, 2),
            Padding = new Thickness(modern ? 1 : 3),
            leftClickAction = () =>
            {
                Explorer.startMenu.Visible = !Explorer.startMenu.Visible;
            }
        };
        bar.AddStackChild(startButton);

        timeLabel = new Label(0, 0, 82, Height - 6)
        {
            verticalAlignment = VerticalAlignment.Center,
            horizontalAlignment = HorizontalAlignment.Right,
            horizontalTextAlignment = HorizontalAlignment.Center,
            verticalTextAlignment = VerticalAlignment.Center,
            text = DateTime.Now.ToString("hh:mm tt"),
            fontSize = modern ? 12 : 14,
            textColor = modern ? Color.White : Palette.ControlBlack,
            Margin = new Thickness(2),
            Padding = new Thickness(2),
            useBackground = !modern,
            useForeground = false,
            capturesInput = true,
            leftClickAction = CalendarFlyout.Toggle
        };
        trayPanel.AddStackChild(timeLabel);

        trayButton = new Button(new Png("/mnt/System/Icons/computer_sound.png"), 0, 0, 24, Height - 8)
        {
            horizontalAlignment = HorizontalAlignment.Right,
            verticalAlignment = VerticalAlignment.Center,
            useBorders = !modern,
            useBackground = !modern,
            iconSize = 18,
            leftClickAction = ToggleTray
        };

        trayPanel.AddStackChild(trayButton);

        Button notifyButton = new Button(new Png("/mnt/System/Icons/message_envelope_open.png"), 0, 0, 24, Height - 8)
        {
            horizontalAlignment = HorizontalAlignment.Right,
            verticalAlignment = VerticalAlignment.Center,
            useBorders = !modern,
            useBackground = !modern,
            iconSize = 18,
            leftClickAction = () => LaunchTracker.Start(() => new NotificationCenter())
        };
        trayPanel.AddStackChild(notifyButton);
        TooltipService.Attach(notifyButton, "Notifications");

        Button showDesktopButton = new Button("", 0, 0, modern ? 14 : 10, Height - 4)
        {
            horizontalAlignment = HorizontalAlignment.Right,
            verticalAlignment = VerticalAlignment.Center,
            useBorders = !modern,
            useBackground = !modern,
            leftClickAction = WindowManager.ToggleShowDesktop
        };
        trayPanel.AddStackChild(showDesktopButton);
        TooltipService.Attach(showDesktopButton, "Show desktop");


        int trayX = Math.Max(0, Global.screenWidth - 160);
        int trayY = Math.Max(0, Global.screenHeight - 160 - Height);
        tray = new Tray(trayX,trayY);
        LaunchTracker.Start(() => tray);

        
    }

    private void ToggleTray()
    {
        tray.Visible = !tray.Visible;

        trayButton.MarkDirty();
    }

    public override void Update()
    {
        base.Update();
        string time = DateTime.Now.ToString("hh:mm tt");
        if (timeLabel.text != time)
        {
            timeLabel.text = time;
        }
    }

    public override void DrawLocal()
    {
        if (useGlass)
        {
            // Windows 7 Aero glass Superbar: translucent dark slate blue with glossy top highlight
            FillRaw(Color.FromArgb(0, 0, 0, 0), 0, 0, Width, Height);
            DrawOverGradientVertical(Color.FromArgb(185, 30, 46, 68), Color.FromArgb(235, 14, 22, 34), 0, 0, Width, Height);
            DrawLine(Color.FromArgb(140, 255, 255, 255), 0, 0, Width - 1, 0);
            DrawLine(Color.FromArgb(50, 255, 255, 255), 0, 1, Width - 1, 1);
            DrawLine(Color.FromArgb(200, 10, 15, 22), 0, Height - 1, Width - 1, Height - 1);

            // Windows 7 Aero Peek divider line before Show Desktop button
            int peekX = Width - 16;
            DrawLine(Color.FromArgb(100, 255, 255, 255), peekX, 2, peekX, Height - 3);
            DrawLine(Color.FromArgb(120, 10, 15, 20), peekX + 1, 2, peekX + 1, Height - 3);
        }
        else
        {
            DrawRaisedRectangle(0, 0, Width, Height);
            DrawLine(Color.Black, 0, 0, Width - 1, 0);
            DrawLine(Palette.ControlWhite, 0, 1, Width - 1, 1);
        }

        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            DrawChild(child);
        }

        timeLabel.MarkDirty();
    }

    private void ShowContextMenu()
    {
        int x = Math.Min(MouseManager.X, Math.Max(0, Global.screenWidth - contextMenu.Width));
        int y = Math.Min(MouseManager.Y, Math.Max(0, Global.screenHeight - contextMenu.Height));
        contextX = x;
        contextY = y;
        contextMenu.ShowAt(x, y);

        MarkDirty();
    }

    public override void Dispose()
    {
        base.Dispose();
    }

    public override string GetComponentName() => "Taskbar";

}
