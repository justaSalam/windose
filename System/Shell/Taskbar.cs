using System.Drawing;
using Cosmos.Kernel.System.Mouse;

public class Taskbar : Component
{
    public Color borderColor = Color.White;

    public List<Button> windows = new List<Button>();

    public StackPanel bar;
    public StackPanel trayPanel;
    private Button startButton;
    private Label timeLabel;

    private Button trayButton;

    public static Tray tray;
    private readonly MenuPopup contextMenu;


    public Taskbar(int x, int y, int width, int height) : base(x, y, width, height)
    {
        zLayer = DrawLayer.Taskbar;




        bar = new StackPanel(Palette.ControlFace, 0, 0, Width - 250, Height)
        {
            useBackground = false,
            useBorders = false,
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Stretch,
            orientation = StackOrientation.Horizontal,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            rightClickAction = ShowContextMenu
        };


        trayPanel = new StackPanel(Palette.ControlFace, 0, 0, 250, Height)
        {
            useBackground = false,
            useBorders = false,
            horizontalAlignment = HorizontalAlignment.Right,
            verticalAlignment = VerticalAlignment.Stretch,
            orientation = StackOrientation.Horizontal,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            rightClickAction = ShowContextMenu
        };

        AddChild(bar);
        AddChild(trayPanel);

        contextMenu = new MenuPopup(230, 24 * 3)
        {
            itemHeight = 18
        };
        contextMenu.AddItem("Task Manager", () => WindowManager.Register(new PerformanceMonitor(180, 120)));
        contextMenu.AddSeparator();
        contextMenu.AddItem("Minimize All Windows");
        contextMenu.AddItem("Properties");



        startButton = new Button("Start", 0, 0, 50, Height)
        {
            verticalAlignment = VerticalAlignment.Center,
            horizontalAlignment = HorizontalAlignment.Left,
            textColor = Color.White,
            useBorders = true,
            Margin = new Thickness(0),
            leftClickAction = () =>
            {
                Explorer.startMenu.Visible = !Explorer.startMenu.Visible;
            }
        };
        bar.AddStackChild(startButton);

        timeLabel = new Label(0, 0, 75, Height)
        {
            verticalAlignment = VerticalAlignment.Center,
            horizontalAlignment = HorizontalAlignment.Right,
            text = DateTime.Now.ToString("HH:mm:ss"),
            Margin = new Thickness(0),
            useBackground = false,
            useForeground = true
        };
        trayPanel.AddStackChild(timeLabel);

        trayButton = new Button("^", 0, 0, Height, Height)
        {
            horizontalAlignment = HorizontalAlignment.Right,
            verticalAlignment = VerticalAlignment.Center,
            leftClickAction = ToggleTray
        };

        trayPanel.AddStackChild(trayButton);


        int trayX = (int)Registry.GetInteger("System/Display/Width", 1920) - 250;
        int trayY = (int)Registry.GetInteger("System/Display/Heigth", 1080) - 160 - Height;
        tray = new Tray(trayX,trayY);
        WindowManager.Register(tray);

        
    }

    private void ToggleTray()
    {
        tray.Visible = !tray.Visible;

        trayButton.label!.text = tray.Visible ? "v" : "^";
        trayButton.MarkDirty();
    }

    public override void Update()
    {
        base.Update();
        timeLabel.text = DateTime.Now.ToString("HH:mm:ss");
        timeLabel.MarkDirty();
    }

    public override void Draw()
    {
        base.Draw();
        timeLabel.MarkDirty();

    }

    public override void DrawLocal()
    {

        DrawRaisedRectangle(0, 0, Width, Height);


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
        contextMenu.ShowAt(x, y);

        MarkDirty();
    }

    public override void Dispose()
    {
        base.Dispose();
    }

    public override string GetComponentName() => "Taskbar";

}
