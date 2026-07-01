using System.Drawing;
using Cosmos.Kernel.Core.IO;
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
            Padding = new Thickness(0)

        };



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
                if (Explorer.startMenu.Visible)
                    UiAnimations.HideStartMenu(Explorer.startMenu);
                else
                    UiAnimations.ShowStartMenu(Explorer.startMenu);
            }
        };
        bar.AddStackChild(startButton);

        timeLabel = new Label(0, 0, 50, Height - 6)
        {
            verticalAlignment = VerticalAlignment.Center,
            horizontalAlignment = HorizontalAlignment.Right,
            text = DateTime.Now.ToString("H:mm:ss"),
            Margin = new Thickness(0)
        };
        AddChild(timeLabel);

        ApplyThemeStyle();
        Palette.ThemeChanged += ApplyThemeStyle;
    }

    public override void Update()
    {
        base.Update();
    }

    public override void Draw()
    {
        DrawLocal();
        DrawToScreen();
    }

    public override void DrawLocal()
    {
        if (Palette.FlatControls)
        {
            DrawFilledRectangle(Palette.TaskbarBackground, 0, 0, Width, Height);
            DrawLine(Palette.WindowBorder, 0, 0, Width, 0);
        }
        else
        {
            DrawRaisedRectangle(0, 0, Width, Height);
        }

        if (text != "") DrawString(text, 0, 0);

        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            DrawChild(child);
        }
    }

    public void ApplyThemeStyle()
    {
        if (Palette.FlatControls)
        {
            startButton.useCustomFace = true;
            startButton.customFaceColor = Palette.Highlight;
            startButton.textColor = Palette.HighlightText;
            startButton.borderColor = Palette.Highlight;
            timeLabel.textColor = Palette.HighlightText;
        }
        else
        {
            startButton.useCustomFace = false;
            startButton.textColor = Color.White;
            startButton.borderColor = Palette.ControlHighlight;
            timeLabel.textColor = Palette.ControlBlack;
        }

        startButton.MarkDirty();
        timeLabel.MarkDirty();
        MarkDirty();
    }

    public override void Dispose()
    {
        Palette.ThemeChanged -= ApplyThemeStyle;
        base.Dispose();
    }

    public override string GetName() => "Taskbar";

}
