using System;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using System.Drawing;
using Windose;
using Windose.System.Kernel.Attributes;
using Windose.System.Kernel;

[FileAssociation(".png")]
[FileAssociation(".webp")]
[FileAssociation(".jpg")]
[FileAssociation(".jpeg")]
[FileAssociation(".gif")]
[FileAssociation(".bmp")]
[FileAssociation(".qoi")]
[FileAssociation(".tga")]
[FileAssociation(".ico")]
[FileAssociation(".ppm")]
public sealed class ImageViewer : Window
{
    private DockPanel root;
    private ImageView imageView;
    private MenuBar menuBar;
    private string currentImagePath;

    public ImageViewer(string image = "/mnt/System/Wallpapers/Lithium.png", int x = 120, int y = 120, int width = 640, int height = 480)
        : base(x, y, width, height, "Image Viewer", true)
    {
        currentImagePath = image;
        root = new DockPanel(0, 0, Width, Height)
        {
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(28, 10, 10, 10),
            Padding = new Thickness(0),
            useBackground = true
        };

        Image loadedImage;
        try
        {
            loadedImage = ImageLoader.Load(image);
        }
        catch
        {
            loadedImage = new Bitmap(Math.Max(16, Width - 20), Math.Max(16, Height - 20), ColorDepth.ColorDepth32);
        }

        imageView = new ImageView(loadedImage, 0, 0, Width - 20, Height - 20);

        menuBar = new MenuBar(0, 0, Width);
        MenuPage file = menuBar.AddMenuPage("File");

        file.AddItem("Open", () =>
        {
            var options = new FileDialogOptions { Title = "Open Image", Mode = FileDialogMode.Open, InitialPath = "/mnt" };
            LaunchTracker.Start(() => new FileDialog(options, (path) =>
            {
                if (ImageLoader.TryLoad(path, out Image? loaded))
                {
                    currentImagePath = path;
                    root.RemoveChild(imageView);
                    imageView = new ImageView(loaded, 0, 0, Width - 20, Height - 20);
                    root.AddDockChild(imageView, Dock.Fill);
                    text = $"Image Viewer - {Path.GetFileName(path)}";
                }
            }));
        });
        file.AddItem("Save As...", () => { });
        file.AddSeparator();
        file.AddItem("Set Wallpaper", () => { Desktop.SetWallpaper(currentImagePath); });
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
