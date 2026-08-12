using Cosmos.Kernel.System.Graphics;
using System.Drawing;


public sealed class ImageViewer : Window
{

    private DockPanel root;
    private ImageView imageView;

    private MenuBar menuBar;

    public ImageViewer(string image, int x, int y, int width, int height) : base(x, y, width, height, "Image Viewer", true)
    {
        root = new DockPanel(0, 0, Width, Height)
        {
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(28, 10, 10, 10),
            Padding = new Thickness(0),
            useBackground = true
        };

        imageView = new ImageView(new Png(image), 0, 0, Width - 20, Height - 20);

        menuBar = new MenuBar(0, 0, Width);

        MenuPage file = menuBar.AddMenuPage("File");

        file.AddItem("Open", () => { });
        file.AddItem("Save", () => { });
        file.AddSeparator();
        file.AddItem("Copy", () => { });
        file.AddSeparator();

        file.AddItem("Properties", () => { });
        file.AddItem("Close", () => WindowManager.PostClose(this));

        MenuPage edit = menuBar.AddMenuPage("Edit");
        edit.AddItem("Rotate", () => { });
        edit.AddItem("Resize", () => { });
        edit.AddItem("Crop", () => { });



        root.AddDockChild(menuBar, Dock.Top);
        root.AddDockChild(imageView, Dock.Fill);


        AddChild(root);

    }

}