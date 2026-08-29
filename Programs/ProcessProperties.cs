using Windose.System.GUI.Components;

public sealed class ProcessProperties : Window
{
    public ProcessProperties(int x, int y, Process process)
        : base(x, y, 480, 330, "Process Properties", true)
    {
        canResize = false;
        canMaximize = false;

        ProcessStartInfo info = process.startInfo ?? new ProcessStartInfo();
        DockPanel root = new DockPanel(0, 0, Width, Height)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(28, 8, 8, 8),
            Padding = new Thickness(8),
            useBackground = true,
            backgroundColor = Palette.ControlFace,
        };

        StackPanel details = new StackPanel(Palette.ControlFace, 0, 0, Width, Height)
        {
            orientation = StackOrientation.Vertical,
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            spacing = 4,
            Padding = new Thickness(4),
            Margin = new Thickness(0),
            useBackground = true,
        };

        AddDetail(details, "Name: " + process.name);
        AddDetail(details, "Process ID: " + process.id);
        AddDetail(details, "Type: " + process.processType);
        AddDetail(details, "Status: " + (process.Running ? "Running" : "Stopped"));
        AddDetail(details, "Started: " + process.startTime);
        AddDetail(details, "Executable: " + DisplayValue(info.ExecutablePath));
        AddDetail(details, "Arguments: " + DisplayValue(info.Arguments));
        AddDetail(details, "Working directory: " + DisplayValue(info.WorkingDirectory));

        Button close = new Button("Close", 0, 0, 80, 26)
        {
            horizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
            clampSize = false,
            leftClickAction = () => WindowManager.PostClose(this),
        };

        root.AddDockChild(close, Dock.Bottom);
        root.AddDockChild(details, Dock.Fill);
        AddChild(root);
    }

    private static void AddDetail(StackPanel panel, string value)
    {
        panel.AddStackChild(new Label(0, 0, 430, 24)
        {
            text = value,
            fontSize = 16,
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0),
        });
    }

    private static string DisplayValue(string value)
        => string.IsNullOrEmpty(value) ? "(none)" : value;
}
