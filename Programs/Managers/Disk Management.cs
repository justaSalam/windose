using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.HAL.Vfs;
using Cosmos.Kernel.System.Filesystems.Fat;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.Vfs;

public sealed class DiskManagement : Window
{

    private DockPanel root;
    private MenuBar menuBar;
    private GroupBox devices;
    private GroupBox partitions;

    private ListView partitionListView;
    private ListView deviceListView;

    private StatusBar status;



    private ListViewItem contextItem;
    private readonly MenuPopup fileContextMenu;
    private readonly MenuItem openContextItem;
    private readonly MenuItem changeDriveLetterItem;

    private IBlockDevice selectedDevice;
    private Partition selectedPartition;


    public DiskManagement(int x, int y, int width, int height) : base(x, y, width, height, "Disk Management", true)
    {
        root = new DockPanel(0, 0, Width, Height)
        {
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(28, 10, 10, 10),
            Padding = new Thickness(0),
            useBackground = true
        };

        devices = new GroupBox(0, 0, Width, Height)
        {
            text = "Devices",
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            useBackground = true,
            Margin = new Thickness(10, 35, 10, 10),
        };

        partitions = new GroupBox(0, 0, Width, Height)
        {
            text = "Partitions",
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            useBackground = true,
            Margin = new Thickness(10, 35, 10, 10),
        };

        menuBar = new MenuBar(0, 0, Width, 30)
        {
            verticalAlignment = VerticalAlignment.Top,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
        };

        partitionListView = new ListView(0, 0, Width - 20, Height - 80)
        {
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(10),
            Padding = new Thickness(0),
            useBackground = true,
            viewMode = ListViewMode.Details,
            headers = ["Name", "Drive Size"],
            headerWidths = [180, 200],
            itemRightClick = ShowDriveContextMenu
        };

        deviceListView = new ListView(0, 0, Width - 20, Height - 80)
        {
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(10),
            Padding = new Thickness(0),
            useBackground = true,
            viewMode = ListViewMode.Details,
            headers = ["Name", "Drive Size"],
            headerWidths = [180, 200],
            itemRightClick = ShowDriveContextMenu
        };

        status = new StatusBar(0, 0, Width);
        status.AddPanel("Select a drive to continute", 300);


        MenuPage File = menuBar.AddMenuPage("File");
        File.AddItem("Options", Options);
        File.AddItem("Exit", () =>
        {
            WindowManager.PostClose(this);
        });

        MenuPage Action = menuBar.AddMenuPage("Action");
        Action.AddItem("Refresh", Refresh);
        Action.AddItem("Rescan Disks", RescanDisks);
        Action.AddItem("Create Volume", CreateVolume);
        Action.AddItem("Attach Volume", RescanDisks);


        fileContextMenu = new MenuPopup(180, 24 * 3);

        openContextItem = fileContextMenu.AddItem("Explore", null);
        fileContextMenu.AddSeparator();

        changeDriveLetterItem = fileContextMenu.AddItem("Change Drive Letter", ShowContextProperties);
        fileContextMenu.AddItem("Format", () =>
        {
            DriveUtils.FAT32FormatDrive(selectedDevice.Name, "1", new FatFormatOptions()
            {
                Type = FatType.Fat32,
                VolumeLabel = "ComosFat32Format"
            });

        });
        fileContextMenu.AddSeparator();

        fileContextMenu.AddItem("Mount Volume", CreateVolume);
        fileContextMenu.AddItem("Extend Volume", ShowContextProperties);
        fileContextMenu.AddItem("Shrink Volume", ShowContextProperties);
        fileContextMenu.AddItem("Delete Volume", DeleteVolume);

        fileContextMenu.AddSeparator();
        fileContextMenu.AddItem("Properties", ShowContextProperties);

        devices.AddGroupChild(deviceListView);
        partitions.AddGroupChild(partitionListView);

        root.AddDockChild(menuBar, Dock.Top);
        root.AddDockChild(devices, Dock.Fill);
        root.AddDockChild(partitions, Dock.Fill);
        root.AddDockChild(status, Dock.Bottom);

        AddChild(root);


        RescanDisks();
    }

    private void DeleteVolume()
    {
        DriveUtils.DeleteMBRPartition(selectedDevice, 0);
    }

    private void CreateVolume()
    {
        WindowManager.Register(new DiskmgrNewVolume(200, 200, selectedDevice));
        //DriveUtils.CreateMBR(selectedDevice);
        //DriveUtils.CreateMbrPartition(selectedDevice);
        //DriveUtils.FAT32MountDrive("fatmount", "1", MountFlags.None, "/mnt", out VfsManager.VfsMount? mount);

    }

    private void RescanDisks()
    {
        for (int i = 0; i < StorageManager.DeviceCount; i++)
        {
            IBlockDevice? device = StorageManager.GetDevice(i);
            StorageManager.RescanPartitions(device);

        }

        Refresh();
    }

    private void Options()
    {
        throw new NotImplementedException();
    }

    private void Refresh()
    {
        deviceListView.ClearItems();
        partitionListView.ClearItems();

        for (int i = 0; i < StorageManager.DeviceCount; i++)
        {
            IBlockDevice? device = StorageManager.GetDevice(i);
            ListViewItem deviceItem = deviceListView.AddItem($"{device?.Name} - {device?.BlockSize * device?.BlockCount / 1024} KB");
            deviceItem.tag = device;
            deviceItem.icon = new Png("/mnt/System/Icons/hard_disk_drive.png");

        }

        foreach (Partition partition in StorageManager.Partitions)
        {
            ListViewItem partitionItem = partitionListView.AddItem($"{partition?.Name} - {partition?.BlockSize * partition?.BlockCount / 1024} KB - {partition?.Host.Name}");
            partitionItem.tag = partition;
            partitionItem.icon = new Png("/mnt/System/Icons/hard_disk_drive_pie.png");

        }
    }

    private void ShowDriveContextMenu(ListViewItem item, int mouseX, int mouseY)
    {
        contextItem = item;
        openContextItem.enabled = item != null;
        changeDriveLetterItem.enabled = item != null && !item.isFolder && item.hasFileEntry;

        selectedDevice = item?.tag as IBlockDevice;
        selectedPartition = item?.tag as Partition;

        int x = Math.Min(mouseX, Math.Max(0, Global.screenWidth - fileContextMenu.Width));
        int y = Math.Min(mouseY, Math.Max(0, Global.screenHeight - fileContextMenu.Height));
        fileContextMenu.ShowAt(x, y);

        RefreshDriveVisuals();
    }


    private void RefreshDriveVisuals()
    {
        deviceListView.MarkDirty(false);
        ForceDirty();
    }

    private void ShowContextProperties()
    {

    }

}