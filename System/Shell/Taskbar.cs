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



        startButton = new Button(new Png("/mnt/System/Icons/start_menu_xp.png"), "Start", 0, 0, 72, Height - 6)
        {
            verticalAlignment = VerticalAlignment.Center,
            horizontalAlignment = HorizontalAlignment.Left,
            contentAlignment = HorizontalAlignment.Left,
            iconSize = 18,
            iconTextGap = 4,
            textColor = Color.Black,
            useBorders = true,
            Margin = new Thickness(1, 1, 2, 2),
            Padding = new Thickness(3),
            leftClickAction = () =>
            {
                Explorer.startMenu.Visible = !Explorer.startMenu.Visible;
            }
        };
        bar.AddStackChild(startButton);

        timeLabel = new Label(0, 0, 82, Height - 8)
        {
            verticalAlignment = VerticalAlignment.Center,
            horizontalAlignment = HorizontalAlignment.Right,
            horizontalTextAlignment = HorizontalAlignment.Center,
            verticalTextAlignment = VerticalAlignment.Center,
            text = DateTime.Now.ToString("hh:mm tt"),
            fontSize = 14,
            Margin = new Thickness(2, 2, 2, 2),
            Padding = new Thickness(2),
            useBackground = true,
            useForeground = false
        };
        trayPanel.AddStackChild(timeLabel);

        trayButton = new Button(new Png("/mnt/System/Icons/computer_sound.png"), 0, 0, 24, Height - 8)
        {
            horizontalAlignment = HorizontalAlignment.Right,
            verticalAlignment = VerticalAlignment.Center,
            useBorders = true,
            iconSize = 18,
            leftClickAction = ToggleTray
        };

        trayPanel.AddStackChild(trayButton);


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

        DrawRaisedRectangle(0, 0, Width, Height);
        DrawLine(Color.Black, 0, 0, Width - 1, 0);
        DrawLine(Palette.ControlWhite, 0, 1, Width - 1, 1);

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
