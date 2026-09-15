using Cosmos.Kernel.System.Graphics;
using System.Drawing;
using Windose;

public class GraphicsEngine : Window
{
    private DockPanel root;

    public GraphicsEngine(int x = 180, int y = 120, int width = 660, int height = 460) : base(x, y, width, height, "SVGA3D Graphics Viewport Test", true)
    {
        root = new DockPanel(0, 0, width, height)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(28, 2, 2, 2),
            Padding = new Thickness(0),
            useBackground = true,
            backgroundColor = Color.Black,
        };
   

 

        AddChild(root);
    }


}
