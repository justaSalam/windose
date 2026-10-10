using Cosmos.Kernel.System;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using System.Drawing;
using Windose;
using Windose.System.Kernel;
using Windose.System.Kernel.FileSystem;
using Windose.System.Kernel.Subsystem;
using Windose.System.System_Calls;

public class DeviceManager : Window
{
    private DockPanel root;
    private DockPanel explorerBody;
    private MenuBar menuBar;
    private Toolbar toolbar;
    private Panel objectCountPanel;
    private Panel selectedPanel;
    private ScrollView treeScroll;
    private TreeView tree;
    private readonly MenuPopup fileContextMenu;
    private readonly MenuPopup viewportContextMenu;
    private readonly MenuItem openContextItem;
    private readonly MenuItem editContextItem;
    private FileListViewItem? contextItem;

    public DeviceManager(int x, int y, int width, int height) : base(x, y, width, height, "Device Manager", true)
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

        explorerBody = new DockPanel(0, 0, Width, Height)
        {
            clampSize = false,
            useBackground = true,
            backgroundColor = Palette.ControlWhite,
            Padding = new Thickness(0),
        };

        treeScroll = new ScrollView(0, 0, 180, Height)
        {
            showHorizontalScrollbar = false,
            clampSize = false,
            Margin = new Thickness(0),
        };

        tree = new TreeView(0, 0, 180, Height)
        {
            useBackground = true,
            backgroundColor = Palette.ControlWhite,
        };

        //TODO Tree item right click (proper version)
        tree.itemRightClick += ctx => viewportContextMenu.ShowAt(MouseManager.X, MouseManager.Y);

        Splitter splitter = new Splitter(0, 0, 4, Height)
        {
            orientation = LayoutOrientation.Vertical,
            clampSize = false,
            Margin = new Thickness(0),
        };


        fileContextMenu = new MenuPopup(160, 24 * 3);
        viewportContextMenu = new MenuPopup(160, 24 * 3);

        MenuItem sortContext = viewportContextMenu.AddItem("Sort");
        sortContext.AddSubmenuItem("Name");
        sortContext.AddSubmenuItem("Date");
        sortContext.AddSubmenuItem("Type");
        sortContext.AddSubmenuItem("Size");
        sortContext.AddSubmenuSeparator();
        sortContext.AddSubmenuItem("Ascending");
        sortContext.AddSubmenuItem("Descending");

        viewportContextMenu.AddSeparator();

        viewportContextMenu.AddItem("Paste");
        viewportContextMenu.AddSeparator();

        viewportContextMenu.AddItem("Refresh", Refresh);
        viewportContextMenu.AddSeparator();

        MenuItem newFileContext = viewportContextMenu.AddItem("File");
        newFileContext.AddSubmenuItem("Exit");


        viewportContextMenu.AddSeparator();
        viewportContextMenu.AddItem("Properties", ShowContextProperties);

        root.AddDockChild(menuBar, Dock.Top);
        root.AddDockChild(toolbar, Dock.Top);
        root.AddDockChild(explorerBody, Dock.Fill);

        treeScroll.SetContent(tree, 180, tree.GetContentHeight());

        explorerBody.AddDockChild(treeScroll, Dock.Left);
        explorerBody.AddDockChild(splitter, Dock.Left);


        MenuPage editMenu = menuBar.AddMenuPage("Edit");
        editMenu.AddItem("Cut").enabled = false;
        editMenu.AddItem("Copy").enabled = false;
        editMenu.AddItem("Paste").enabled = false;
        editMenu.AddSeparator();
        editMenu.AddItem("Delete").enabled = false;

        MenuPage viewMenu = menuBar.AddMenuPage("View");
        viewMenu.AddItem("Refresh", Refresh);

        MenuPage helpMenu = menuBar.AddMenuPage("Help");
        helpMenu.AddItem("Windose File Explorer").enabled = false;



        BuildTree();

        //tree.selectedChanged = OpenLocation;

        //tree.itemDoubleClick = OpenLocation;




        AddChild(root);
    }


    private void BuildTree()
    {
        tree.ClearItems();

       


        TreeViewItem bootTree = tree.AddRoot("/boot", "/boot");
    }


    private void ShowContextProperties()
    {
        if (!IsItemValid(out FileListViewItem item)) return;
        LaunchTracker.Start(() => new FileProperties(X + 40, Y + 40, item.fileEntry));
    }
    private bool IsItemValid(out FileListViewItem item)
    {
        item = contextItem;
        contextItem = null;

        return item != null && item.hasFileEntry;
    }



    private void Refresh()
    {
        BuildTree();
    }


    private void RefreshExplorerVisuals()
    {
        // Explorer contains several cached layout buffers. Redraw the explorer after interaction so those updated buffers reach the screen.
        ForceDirty();
    }


    public override void Dispose()
    {
        fileContextMenu.Hide();
        fileContextMenu.Dispose();
        base.Dispose();
    }
}
