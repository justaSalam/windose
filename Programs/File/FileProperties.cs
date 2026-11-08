using System;
using System.IO;
using Windose;
using Windose.System.Kernel;

public class FileProperties : Window
{
    private DockPanel root;
    private MenuBar menuBar;
    private GroupBox group;
    private StackPanel buttons;
    private TextField fileNameField;
    private Checkbox cbReadOnly;
    private Checkbox cbHidden;
    private FileEntry targetEntry;

    public FileProperties(int x, int y, FileEntry fileEntry) : base(x, y, 350, 420, $"{fileEntry.FileName} Properties", true)
    {
        canResize = false;
        targetEntry = fileEntry;

        menuBar = new MenuBar(0, 0, Width);
        MenuPage generalPage = menuBar.AddMenuPage("General");
        MenuPage securityPage = menuBar.AddMenuPage("Security");

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

        fileNameField = new TextField(0, 0, 240, 26)
        {
            text = fileEntry.FileName,
            useBackground = true,
        };
        group.AddGroupChild(fileNameField);

        group.AddGroupChild(new Separator(0, 0, Width, 1) { orientation = LayoutOrientation.Horizontal });

        group.AddGroupChild(new Label(0, 0, Width, 26)
        {
            text = $"Type: {fileEntry.FileType}",
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Stretch
        });

        group.AddGroupChild(new Label(0, 0, Width, 26)
        {
            text = $"Location: {fileEntry.AbsoluteLocation}",
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Stretch
        });

        if (fileEntry.FileType == FileType.Directory)
        {
            try
            {
                DirectoryInfo directoryInfo = new DirectoryInfo(fileEntry.AbsoluteLocation);
                foreach (FileInfo fileInfo in directoryInfo.GetFiles())
                {
                    fileEntry.SizeBytes += fileInfo.Length;
                }
                fileEntry.directoryCount = directoryInfo.GetDirectories().Length;
                fileEntry.fileCount = directoryInfo.GetFiles().Length;

                group.AddGroupChild(new Label(0, 0, Width, 26)
                {
                    text = $"Contains: {fileEntry.directoryCount} dir(s), {fileEntry.fileCount} file(s)",
                    useBackground = false,
                    horizontalAlignment = HorizontalAlignment.Stretch
                });
            }
            catch { }
        }

        group.AddGroupChild(new Label(0, 0, Width, 26)
        {
            text = $"Size: {ByteSize.Format(fileEntry.SizeBytes)}",
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Stretch
        });

        group.AddGroupChild(new Separator(0, 0, Width, 1) { orientation = LayoutOrientation.Horizontal });

        group.AddGroupChild(new Label(0, 0, Width, 26)
        {
            text = $"Created at: {fileEntry.CreatedAt}",
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Stretch
        });

        cbReadOnly = new Checkbox(0, 0) { text = "Read-only" };
        cbHidden = new Checkbox(120, 0) { text = "Hidden" };

        try
        {
            string attr = Registry.Get($"Software/Windose/Attributes{fileEntry.AbsoluteLocation}") as string;
            if (!string.IsNullOrEmpty(attr))
            {
                if (attr.Contains("R")) cbReadOnly.Checked = true;
                if (attr.Contains("H")) cbHidden.Checked = true;
            }
        }
        catch { }

        group.AddGroupChild(cbReadOnly);
        group.AddGroupChild(cbHidden);

        buttons.AddStackChild(new Button("Ok", 0, 0, 70, 24)
        {
            useBorders = true,
            clampSize = false,
            Margin = new Thickness(0),
            leftClickAction = () =>
            {
                Apply();
                Close();
            }
        });
        buttons.AddStackChild(new Button("Cancel", 0, 0, 70, 24)
        {
            useBorders = true,
            clampSize = false,
            Margin = new Thickness(0),
            leftClickAction = Close
        });
        buttons.AddStackChild(new Button("Apply", 0, 0, 70, 24)
        {
            useBorders = true,
            clampSize = false,
            Margin = new Thickness(0),
            leftClickAction = Apply
        });

        root.AddDockChild(menuBar, Dock.Top);
        root.AddDockChild(buttons, Dock.Bottom);
        root.AddDockChild(group, Dock.Fill);

        AddChild(root);
    }

    private void Close()
    {
        WindowManager.Close(this);
    }

    private void Apply()
    {
        try
        {
            if (!string.IsNullOrEmpty(targetEntry.AbsoluteLocation) && !string.IsNullOrEmpty(fileNameField.text) && fileNameField.text != targetEntry.FileName)
            {
                string dir = Path.GetDirectoryName(targetEntry.AbsoluteLocation);
                string newPath = Path.Combine(dir, fileNameField.text);
                if (targetEntry.FileType == FileType.Directory)
                {
                    if (Directory.Exists(targetEntry.AbsoluteLocation) && !Directory.Exists(newPath))
                    {
                        Directory.Move(targetEntry.AbsoluteLocation, newPath);
                        targetEntry.FileName = fileNameField.text;
                        targetEntry.AbsoluteLocation = newPath;
                        SetTitle($"{targetEntry.FileName} Properties");
                    }
                }
                else
                {
                    if (File.Exists(targetEntry.AbsoluteLocation) && !File.Exists(newPath))
                    {
                        File.Move(targetEntry.AbsoluteLocation, newPath);
                        targetEntry.FileName = fileNameField.text;
                        targetEntry.AbsoluteLocation = newPath;
                        SetTitle($"{targetEntry.FileName} Properties");
                    }
                }
            }

            if (!string.IsNullOrEmpty(targetEntry.AbsoluteLocation))
            {
                string attr = (cbReadOnly.Checked ? "R" : "") + (cbHidden.Checked ? "H" : "A");
                Registry.Set($"Software/Windose/Attributes{targetEntry.AbsoluteLocation}", attr);
                NotificationManager.Notify("Properties", $"Updated properties for {targetEntry.FileName}");
            }
        }
        catch { }
    }
}
