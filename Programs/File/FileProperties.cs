public class FileProperties : Window
{
    private DockPanel root;
    private MenuBar menuBar;
    private GroupBox group;
    private StackPanel buttons;
    public FileProperties(int x, int y, FileEntry fileEntry) : base(x, y, 340, 380, "Properties", true)
    {
        canResize = false;
        menuBar = new MenuBar(0, 0, Width);



        MenuPage General = menuBar.AddMenuPage("General");

        MenuPage Security = menuBar.AddMenuPage("Security");

        root = new DockPanel(0, 0, Width, Height)
        {
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(28, 10, 10, 10),
            Padding = new Thickness(0),
            useBackground = true
        };

        group = new GroupBox(0, 0, Width, Height)
        {
            text = "File Properties",
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            useBackground = true,
            Margin = new Thickness(10, 35, 10, 10),

        };
        buttons = new StackPanel(Palette.ControlFace, 0, 0, Width, 28)
        {
            orientation = StackOrientation.Horizontal,
            verticalAlignment = VerticalAlignment.Bottom,
            horizontalAlignment = HorizontalAlignment.Right,
            spacing = 6,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 10, 10, 0),
            clampSize = false,
            useBackground = false,
        };

        group.AddGroupChild(new Label(0, 0, 200, 30)
        {
            text = $"{fileEntry.FileName}",
            useBackground = false,
            fontSize = 16
        });



        group.AddGroupChild(new Separator(0, 0, Width, 1) { orientation = LayoutOrientation.Horizontal });

        group.AddGroupChild(new Label(0, 0, 200, 30)
        {
            text = $"Type: {fileEntry.FileType}",
            useBackground = false,
            fontSize = 17
        });

        group.AddGroupChild(new Label(0, 0, 200, 30)
        {
            text = $"Location: {fileEntry.AbsoluteLocation}",
            useBackground = false,
            fontSize = 17
        });
        group.AddGroupChild(new Label(0, 0, 200, 30)
        {
            text = $"Size: {fileEntry.SizeBytes / 1024} kB",
            useBackground = false,
            fontSize = 17
        });
        group.AddGroupChild(new Label(0, 0, 200, 30)
        {
            text = $"Contains: {fileEntry.Contains}",
            useBackground = false,
            fontSize = 17
        });
        group.AddGroupChild(new Separator(0, 0, Width, 1) { orientation = LayoutOrientation.Horizontal });

        group.AddGroupChild(new Label(0, 0, 200, 30)
        {
            text = $"Created {fileEntry.CreatedAt}",
            useBackground = false,
            fontSize = 17
        });

        buttons.AddStackChild(new Button(0, 0, 70, 24)
        {
            text = "OK",
            useBorders = true,
            clampSize = false,
            Margin = new Thickness(0),
            leftMouseRelease = () =>
            {
                Apply();
                Close();
            }
        });
        buttons.AddStackChild(new Button(0, 0, 70, 24)
        {
            text = "Cancel",
            useBorders = true,
            clampSize = false,
            Margin = new Thickness(0),
            leftMouseRelease = () =>
            {
                Close();
            }
        });

        buttons.AddStackChild(new Button(0, 0, 70, 24)
        {
            text = "Apply",
            useBorders = true,
            clampSize = false,
            Margin = new Thickness(0),
            leftMouseRelease = () =>
            {
                Apply();
            }
        });


        root.AddDockChild(menuBar, Dock.Top);

        root.AddDockChild(group, Dock.Fill);

        root.AddDockChild(buttons, Dock.Bottom);
        AddChild(root);
    }

    private void Close()
    {
        WindowManager.Close(this);
    }
    private void Apply()
    {

    }
}
