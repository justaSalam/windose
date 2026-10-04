using Cosmos.Kernel.System.Graphics;
using System.Drawing;


public sealed class ControlEnv : Window
{
    private DockPanel dock;
    public ControlEnv(int x, int y) : base(x, y, 400, 250, "Winver", true)
    {
        dock = new DockPanel(0,0,Width, Height)
        {
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0),
            Padding = new Thickness(4),
        };

        AddChild(dock);

        dock.AddDockChild(new Label(0, 0, 100, 20)
        {
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Center,
            verticalTextAlignment = VerticalAlignment.Center,
            verticalAlignment = VerticalAlignment.Stretch,
            text = "Windose NativeAoT",
        }, Dock.Top);
        dock.AddDockChild(new Label(0, 0, 100, 20)
        {
            useBackground = false,

            horizontalAlignment = HorizontalAlignment.Center,
            verticalTextAlignment = VerticalAlignment.Center,
            verticalAlignment = VerticalAlignment.Stretch,

            text = "Version 3.0.89",
        }, Dock.Top);
        dock.AddDockChild(new Label(0, 0, 100, 20)
        {
            useBackground = false,

            horizontalAlignment = HorizontalAlignment.Center,
            verticalTextAlignment = VerticalAlignment.Center,
            verticalAlignment = VerticalAlignment.Stretch,

            text = "Cyberialyr all rights reserved",
        }, Dock.Top);
        dock.AddDockChild(new Button("Ok", 0, 0, 100, 20)
        {
            horizontalAlignment = HorizontalAlignment.Right,
            
        }, Dock.Bottom);

    }

}