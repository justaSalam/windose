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
    private readonly DockPanel finalPage;

    private int selectedPage;

    public int pages { get; private set; }


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
            horizontalAlignment = HorizontalAlignment.Right,
            clampSize = false,
            useBackground = true,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0),
            spacing = 2,
        };




        tabs.AddStackChild(CreateTab("< Back", 112, PreviousPage));
        tabs.AddStackChild(CreateTab("Next >", 112, NextPage));
        tabs.AddStackChild(CreateTab("Cancel", 112, () => WindowManager.PostClose(this)));


        wizardPage = CreatePage();
        driveSizePage = CreatePage();
        driveLetterPage = CreatePage();
        formatDrivePage = CreatePage();
        finalPage = CreatePage();

        wizardPage.AddDockChild(new Label(0, 0, 0, 0)
        {
            text = "This wizard helps you create a volume on a disk.",
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Top,
            fontSize = 16
        }, Dock.Fill);
        wizardPage.AddDockChild(new Label(0, 0, 0, 0)
        {
            text = "A volume can only be on a single disk.",
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Top,
            fontSize = 16
        }, Dock.Fill);

        driveSizePage.AddDockChild(new Label(0, 0, 0, 0)
        {
            text = "Drive size page",
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Top,
            fontSize = 16
        }, Dock.Fill);

        driveLetterPage.AddDockChild(new Label(0, 0, 0, 0)
        {
            text = "Drive Letter Page",
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Top,
            fontSize = 16
        }, Dock.Fill);

        formatDrivePage.AddDockChild(new Label(0, 0, 0, 0)
        {
            text = "Format page",
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Top,
            fontSize = 16
        }, Dock.Fill);

        finalPage.AddDockChild(new Label(0, 0, 0, 0)
        {
            text = "Final page",
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Top,
            fontSize = 16
        }, Dock.Fill);


        root.AddDockChild(tabs, Dock.Bottom);

        root.AddDockChild(wizardPage, Dock.Fill);
        root.AddDockChild(driveSizePage, Dock.Fill);
        root.AddDockChild(driveLetterPage, Dock.Fill);
        root.AddDockChild(formatDrivePage, Dock.Fill);
        root.AddDockChild(finalPage, Dock.Fill);


        AddChild(root);
        ShowPage(0);
    }

    private void NextPage()
    {
        if (selectedPage < pages) selectedPage++;

        ShowPage(selectedPage);
    }

    private void PreviousPage()
    {
        if (selectedPage > 0) selectedPage--;
        ShowPage(selectedPage);
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
        pages++;
        selectedPage = page;
        wizardPage.Visible = page == 0;
        driveSizePage.Visible = page == 1;
        driveLetterPage.Visible = page == 2;
        formatDrivePage.Visible = page == 3;
        finalPage.Visible = page == 4;
        root.ResolveDockLayout();
        root.MarkDirty();
    }

    public override void Update()
    {
    }

    private static float BytesToMb(ulong bytes) => bytes / (1024f * 1024f);

    public override string GetName() => "Disk Manager New Volume";
}
