using System.Drawing;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Windose;

public class DiskmgrNewVolume : Window
{
    private readonly DockPanel root;

    private readonly DockPanel wizardPage;
    private readonly DockPanel driveSizePage;
    private readonly DockPanel driveLetterPage;
    private readonly DockPanel formatDrivePage;

    private readonly GroupBox wizardGroup;
    private readonly GroupBox driveGroup;
    private readonly GroupBox letterGroup;
    private readonly GroupBox formatGroup;

    private DockPanel bottomRow;
    private int selectedPage;

    public DiskmgrNewVolume(int x, int y, IBlockDevice device, int width = 720, int height = 560) : base(x, y, width, height, "New Simple Volume Wizard", true)
    {
        root = new DockPanel(0, 0, Width, Height)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(28, 4, 4, 4),
            Padding = new Thickness(4),
            useBackground = true,
            backgroundColor = Palette.ControlFace,
        };

        StackPanel tabs = new StackPanel(Palette.ControlFace, 0, 0, Width, 28)
        {
            orientation = StackOrientation.Horizontal,
            clampSize = false,
            useBackground = true,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0),
            spacing = 2,
        };

        wizardGroup = new GroupBox(0, 0, Width, Height)
        {
            text = "New Volume Wizard",
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            useBackground = true,
            Margin = new Thickness(10, 35, 10, 10),
        };

        driveGroup = new GroupBox(0, 0, Width, Height)
        {
            text = "Specify Volume Size",
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            useBackground = true,
            Margin = new Thickness(10, 35, 10, 10),
        };
        letterGroup = new GroupBox(0, 0, Width, Height)
        {
            text = "Assign Drive Letter",
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            useBackground = true,
            Margin = new Thickness(10, 35, 10, 10),
        };
        formatGroup = new GroupBox(0, 0, Width, Height)
        {
            text = "Format partition",
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            useBackground = true,
            Margin = new Thickness(10, 35, 10, 10),
        };


        tabs.AddStackChild(CreateTab("< Back", 112, () => ShowPage(0)));
        tabs.AddStackChild(CreateTab("Next >", 112, () =>
        {
            selectedPage++;
            ShowPage(selectedPage);
        }));
        tabs.AddStackChild(CreateTab("Cancel", 112, null));


        wizardPage = CreatePage();
        driveSizePage = CreatePage();
        driveLetterPage = CreatePage();
        formatDrivePage = CreatePage();

        wizardGroup.AddGroupChild(new Label(0, 0, width, 30)
        {
            text = "New Volume Wizard",
            fontSize = 16
        });

        wizardGroup.AddGroupChild(new Label(0, 0, width, height)
        {
            text = "A volume can only be on a single disk\nTo continue click next",
            fontSize = 12

        });

        wizardPage.AddDockChild(wizardGroup, Dock.Fill);
        driveSizePage.AddDockChild(driveGroup, Dock.Fill);
        driveLetterPage.AddDockChild(letterGroup, Dock.Fill);
        formatDrivePage.AddDockChild(formatGroup, Dock.Fill);

        wizardGroup.AddGroupChild(wizardPage);
        driveGroup.AddGroupChild(driveSizePage);
        letterGroup.AddGroupChild(driveLetterPage);
        formatGroup.AddGroupChild(formatDrivePage);


        root.AddDockChild(tabs, Dock.Top);
        root.AddDockChild(driveSizePage, Dock.Fill);
        root.AddDockChild(driveGroup, Dock.Fill);
        root.AddDockChild(driveLetterPage, Dock.Fill);
        root.AddDockChild(formatDrivePage, Dock.Fill);

        AddChild(root);
        ShowPage(0);
    }

    private static DockPanel CreatePage()
    {
        return new DockPanel(0, 0, 100, 100)
        {
            clampSize = false,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            useBackground = true,
            backgroundColor = Palette.ControlFace,
        };
    }

    private static Button CreateTab(string text, int width, Action action)
    {
        return new Button(0, 0, width, 26)
        {
            text = text,
            fontSize = 14,
            useBorders = true,
            clampSize = false,
            Margin = new Thickness(0),
            leftMouseRelease = action,
        };
    }

    private void ShowPage(int page)
    {
        selectedPage = page;
        wizardPage.Visible = page == 0;
        driveSizePage.Visible = page == 1;
        driveLetterPage.Visible = page == 2;
        formatDrivePage.Visible = page == 3;
        root.ResolveDockLayout();
        root.MarkDirty();
    }

    public override void Update()
    {
    }

    private static float BytesToMb(ulong bytes) => bytes / (1024f * 1024f);

    public override string GetName() => "Disk Manager New Volume";
}
