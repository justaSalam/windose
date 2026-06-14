using System.Drawing;
using Windose;

public class SystemManager : Window
{
    public SystemManager(int x, int y, int width, int height, string title, bool useTitleBar = false) : base(x, y, width, height, title, useTitleBar)
    {
        AddChild(new Button(0, 0, 100, 30)
        {
            text = "Button",
            useBorders = true,
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Center,
            leftMouseRelease = () =>
            {
            }
        });

        AddChild(new TextField(0, 0, 250, 30)
        {
            fontSize = 16,
            Margin = new Thickness(35, 0, 0, 0),
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Center,
        });

        AddChild(new Checkbox(0, 0)
        {
            text = "Checkbox",
            fontSize = 16,
            Margin = new Thickness(70, 0, 0, 0),
            horizontalAlignment = HorizontalAlignment.Center,
            verticalAlignment = VerticalAlignment.Center,
        });

        AddChild(new GroupBox(0, 0, 100, 50)
        {
            horizontalAlignment = HorizontalAlignment.Right,
            verticalAlignment = VerticalAlignment.Center,
            text = "Colors"
        });

    }
}