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
        bar.AddStackChild(new Button(0, 0, 50, Height) //Start button
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
                    Explorer.startMenu.HideMenu();
                else
                {
                    Explorer.startMenu.Visible = true;
                    Explorer.startMenu.MarkDirty();
                }
            }
        });

        AddChild(new Label(0, 0, 50, Height - 6) //Time Label
        {
            verticalAlignment = VerticalAlignment.Center,
            horizontalAlignment = HorizontalAlignment.Right,
            text = DateTime.Now.ToString("H:mm:ss"),
            Margin = new Thickness(0)

        });

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
        DrawRaisedRectangle(0, 0, Width, Height);
        if (text != "") DrawString(text, 0, 0);

        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            child.DrawLocal();
            buffer.DrawImageAlpha(child.GetBuffer(), child.X, child.Y);
            child.MarkCleaned();
        }
    }

    public override string GetName() => "Taskbar";

}
