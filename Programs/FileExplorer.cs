using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Windose;

//Control Panel
public class FileExplorer : Window
{
    private DockPanel root;
    private MenuBar menuBar;
    private Toolbar toolbar;
    private AddressBar addressBar;
    private StatusBar statusBar;
    private GridPanel content;

    private ScrollView contentScroll;
    private ScrollView fileScroll;
    public FileExplorer(int x, int y, int width, int height, string title, bool useTitleBar = false) : base(x, y, width, height, title, useTitleBar)
    {
        root = new DockPanel(0, 0, Width, Height)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(28, 2, 2, 2),
            Padding = new Thickness(0),
            useBackground = true,
        };




        menuBar = new MenuBar(0, 0, Width);
        toolbar = new Toolbar(0, 0, Width);
        addressBar = new AddressBar(0, 0, Width);
        statusBar = new StatusBar(0, 0, Width);

        contentScroll = new ScrollView(0, 0, Width, Height);
        content = new GridPanel(0, 0, Width, Height)
        {
            useBackground = true,
            backgroundColor = Palette.ControlWhite
        };
        contentScroll.SetContent(content, Width - 16, 1200);
        root.AddDockChild(contentScroll, Dock.Fill);

        root.AddDockChild(menuBar, Dock.Top);
        root.AddDockChild(toolbar, Dock.Top);
        root.AddDockChild(addressBar, Dock.Top);
        root.AddDockChild(statusBar, Dock.Bottom);
        //root.AddDockChild(content, Dock.Fill);

        menuBar.AddMenu("File");
        menuBar.AddMenu("Edit");
        menuBar.AddMenu("View");
        menuBar.AddMenu("Go");
        menuBar.AddMenu("Help");

        toolbar.AddButton("Back");
        toolbar.AddButton("Forward");
        toolbar.AddButton("Up");

        toolbar.AddSeparator();
        toolbar.AddButton("Cut");
        toolbar.AddButton("Copy");
        toolbar.AddButton("Paste");

        toolbar.AddSeparator();
        toolbar.AddButton("Undo");

        toolbar.AddSeparator();
        toolbar.AddButton("Delete");
        toolbar.AddButton("Properties");

        statusBar.AddPanel("Objects");
        statusBar.AddPanel("Selected");

        TreeView tree = new TreeView(0, 0, 180, Height)
        {
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0),
        };

        TreeViewItem computer = tree.AddRoot("My Computer");
        TreeViewItem documents = tree.AddRoot("Documents");
        TreeViewItem cDrive = computer.AddChild("Windose (C:)");
        TreeViewItem floppyDrive = computer.AddChild("3.5 Floppy (A:)");

        TreeViewItem system = cDrive.AddChild("System");

        system.AddChild("Config");
        documents.AddChild("Docx");


        tree.selectedChanged = item =>
        {
            addressBar.text = item.text;
            addressBar.DrawLocal();
        };

        root.AddDockChild(tree, Dock.Left);

        AddChild(root);
    }
}
