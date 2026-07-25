using System.Drawing;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Mouse;
using Windose;

public class Taskbar : Component
{

    public bool useBorders = false;
    private bool useGradient = false;
    public Color borderColor = Color.White;
    public string text = "";

    public List<Button> windows = new List<Button>();

    public StackPanel bar;
    private Button startButton;
    private Label timeLabel;

    private readonly MenuPopup contextMenu;


    public Taskbar(int x, int y, int width, int height) : base(x, y, width, height)
    {

        useGradient = false;
        zLayer = DrawLayer.Taskbar;


        bar = new StackPanel(Palette.ControlFace, 0, 0, Width, Height)
        {
            useBackground = false,
            useBorders = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            orientation = StackOrientation.Horizontal,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            rightClickAction = ShowContextMenu
        };

        contextMenu = new MenuPopup(230, 24 * 3)
        {
            itemHeight = 18
        };
        contextMenu.AddItem("Task Manager", () =>
        {
            WindowManager.Register(new PerformanceMonitor(180, 120));
        });
        contextMenu.AddSeparator();
        contextMenu.AddItem("Minimize All Windows");
        contextMenu.AddItem("Properties");




        AddChild(bar);
        startButton = new Button(0, 0, 50, Height)
        {
            verticalAlignment = VerticalAlignment.Center,
            horizontalAlignment = HorizontalAlignment.Left,
            text = "Start",
            textColor = Color.White,
            useBorders = true,
            Margin = new Thickness(0),
            leftMouseRelease = () =>
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
            Margin = new Thickness(0, 0, 20, 0),

        };
        AddChild(timeLabel);

        ApplyThemeStyle();
        Palette.ThemeChanged += ApplyThemeStyle;
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

    public void ApplyThemeStyle()
    {

        startButton.useCustomFace = false;
        startButton.textColor = Color.White;
        startButton.borderColor = Palette.ControlHighlight;
        timeLabel.textColor = Palette.ControlBlack;
        bar.color1 = Palette.ControlFace;


        startButton.MarkDirty();
        timeLabel.MarkDirty();
        MarkDirty();
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
        Palette.ThemeChanged -= ApplyThemeStyle;
        base.Dispose();
    }

    public override string GetName() => "Taskbar";

}
