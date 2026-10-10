using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Graphics;

public class DeviceManager : Window
{
    private readonly DockPanel root;
    private readonly DockPanel deviceListPane;
    private readonly DockPanel detailsPane;
    private readonly MenuBar menuBar;
    private readonly Toolbar toolbar;
    private readonly Panel statusPanel;
    private readonly Panel detailsHeader;
    private readonly Panel nameDetail;
    private readonly Panel kindDetail;
    private readonly Panel driverDetail;
    private readonly Panel nodePathDetail;
    private readonly Panel detailsHint;
    private readonly ScrollView treeScroll;
    private readonly TreeView tree;
    private int deviceCount;

    public DeviceManager(int x, int y, int width, int height) : base(x, y, width, height, "Device Manager", true, new Png("/mnt/System/Icons/mci_devices.png"))
    {
        root = new DockPanel(0, 0, Width, Height)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(28, 2, 2, 2),
            Padding = new Thickness(0),
            useBackground = true,
            backgroundColor = Palette.ControlFace,
        };

        menuBar = new MenuBar(0, 0, Width);
        toolbar = new Toolbar(0, 0, Width);
        toolbar.AddButton("Refresh", Refresh);

        deviceListPane = new DockPanel(0, 0, Width, Height)
        {
            clampSize = false,
            useBackground = true,
            backgroundColor = Palette.ControlWhite,
            Padding = new Thickness(0),
        };

        treeScroll = new ScrollView(0, 0, 220, Height)
        {
            showHorizontalScrollbar = false,
            clampSize = false,
            Margin = new Thickness(0),
        };

        tree = new TreeView(0, 0, 220, Height)
        {
            useBackground = true,
            backgroundColor = Palette.ControlWhite,
        };
        tree.selectedChanged += ShowDeviceDetails;

        Splitter splitter = new Splitter(0, 0, 4, Height)
        {
            orientation = LayoutOrientation.Vertical,
            clampSize = false,
            Margin = new Thickness(0),
        };

        detailsPane = new DockPanel(0, 0, Width, Height)
        {
            clampSize = false,
            useBackground = true,
            backgroundColor = Palette.ControlWhite,
            Padding = new Thickness(8),
        };

        detailsHeader = CreateDetailRow("Device details", Palette.ControlFace, 30, 18);
        nameDetail = CreateDetailRow("Name: —", Palette.ControlWhite, 26, 16);
        kindDetail = CreateDetailRow("Type: —", Palette.ControlWhite, 26, 16);
        driverDetail = CreateDetailRow("Driver: —", Palette.ControlWhite, 26, 16);
        nodePathDetail = CreateDetailRow("Node path: —", Palette.ControlWhite, 56, 16);
        detailsHint = CreateDetailRow("Select a device to view its reported details.", Palette.ControlWhite, 60, 16);

        detailsPane.AddDockChild(detailsHeader, Dock.Top);
        detailsPane.AddDockChild(nameDetail, Dock.Top);
        detailsPane.AddDockChild(kindDetail, Dock.Top);
        detailsPane.AddDockChild(driverDetail, Dock.Top);
        detailsPane.AddDockChild(nodePathDetail, Dock.Top);
        detailsPane.AddDockChild(detailsHint, Dock.Fill);

        statusPanel = new Panel(Palette.ControlFace, 0, 0, Width, 22)
        {
            useBackground = true,
            fontSize = 14,
            textColor = Palette.ControlBlack,
            textOffsetX = 6,
            wrapText = false,
            clampSize = false,
            Margin = new Thickness(0),
            text = "Devices: 0",
        };

        MenuPage deviceMenu = menuBar.AddMenuPage("Device");
        deviceMenu.AddItem("Refresh", Refresh);

        MenuPage viewMenu = menuBar.AddMenuPage("View");
        viewMenu.AddItem("Refresh", Refresh);

        root.AddDockChild(menuBar, Dock.Top);
        root.AddDockChild(toolbar, Dock.Top);
        root.AddDockChild(statusPanel, Dock.Bottom);
        root.AddDockChild(deviceListPane, Dock.Fill);

        deviceListPane.AddDockChild(treeScroll, Dock.Left);
        deviceListPane.AddDockChild(splitter, Dock.Left);
        deviceListPane.AddDockChild(detailsPane, Dock.Fill);

        BuildTree();
        treeScroll.SetContent(tree, 220, tree.GetContentHeight());
        AddChild(root);
    }

    private static Panel CreateDetailRow(string text, System.Drawing.Color background, int height, int fontSize)
    {
        return new Panel(background, 0, 0, 100, height)
        {
            useBackground = background != Palette.ControlWhite,
            text = text,
            fontSize = fontSize,
            textColor = Palette.ControlBlack,
            textOffsetX = 4,
            wrapText = true,
            clampSize = false,
            Margin = new Thickness(0),
        };
    }

    private void BuildTree()
    {
        tree.ClearItems();
        deviceCount = 0;

        TreeViewItem keyboard = tree.AddRoot("Keyboard", "/mnt/System/Icons/keyboard.png");
        TreeViewItem pointer = tree.AddRoot("Pointer", "/mnt/System/Icons/mouse_ms.png");
        TreeViewItem network = tree.AddRoot("Network", "/mnt/System/Icons/network_drive.png");
        TreeViewItem block = tree.AddRoot("Block", "/mnt/System/Icons/removable_disk_drive_alt.png");
        TreeViewItem display = tree.AddRoot("Display", "/mnt/System/Icons/display_properties.png");
        TreeViewItem other = tree.AddRoot("Other devices", "/mnt/System/Icons/hardware.png");

        for (int i = 0; i < DriverDiagnostics.DeviceCount; i++)
        {
            if (!DriverDiagnostics.TryGetDevice(i, out PublishedDeviceInfo info))
                continue;

            TreeViewItem category = info.Kind switch
            {
                PublishedDeviceKind.Keyboard => keyboard,
                PublishedDeviceKind.Pointer => pointer,
                PublishedDeviceKind.Network => network,
                PublishedDeviceKind.Block => block,
                PublishedDeviceKind.Display => display,
                _ => other,
            };

            string name = string.IsNullOrWhiteSpace(info.Name) ? "Unnamed device" : info.Name;
            category.AddChild(name, info);
            deviceCount++;
        }

        statusPanel.text = $"Devices: {deviceCount}";
        statusPanel.MarkDirty();
        ShowDeviceDetails(null);
        tree.MarkDirty();
    }

    private void ShowDeviceDetails(TreeViewItem item)
    {
        if (item?.tag is PublishedDeviceInfo info)
        {
            nameDetail.text = $"Name: {DisplayValue(info.Name)}";
            kindDetail.text = $"Type: {info.Kind}";
            driverDetail.text = $"Driver: {DisplayValue(info.DriverName)}";
            nodePathDetail.text = $"Node path: {DisplayValue(info.NodePath)}";
            detailsHint.text = "Device is published by the active driver system.";
        }
        else if (item != null)
        {
            nameDetail.text = $"Category: {item.text}";
            kindDetail.text = $"Devices: {item.children.Count}";
            driverDetail.text = string.Empty;
            nodePathDetail.text = string.Empty;
            detailsHint.text = "Select a device to view its reported details.";
        }
        else
        {
            nameDetail.text = "Name: —";
            kindDetail.text = "Type: —";
            driverDetail.text = "Driver: —";
            nodePathDetail.text = "Node path: —";
            detailsHint.text = deviceCount == 0
                ? "No published devices are currently available."
                : "Select a device to view its reported details.";
        }

        nameDetail.MarkDirty();
        kindDetail.MarkDirty();
        driverDetail.MarkDirty();
        nodePathDetail.MarkDirty();
        detailsHint.MarkDirty();
    }

    private static string DisplayValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Not reported" : value;
    }

    private void Refresh()
    {
        BuildTree();
    }

    public override string GetComponentName() => "DeviceManager";
}
