using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.System.FileSystem;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Storage;


public sealed class DiskManagement : Window
{
    private const int TitleBar = 28;
 
    private readonly DockPanel root;
    private readonly ListView partitionListView;
    private readonly PartitionBar partitionBar;
    private readonly Label statusLabel;
    private readonly ComboBox diskCombo;

    // Action pane
    private readonly DockPanel actionPane;
    private readonly Label formatTitle;
    private readonly ComboBox fsCombo;
    private readonly TextField labelBox;
    private readonly Button formatButton;
    private readonly Button cancelButton;

    // Toolbar buttons that need a selection
    private readonly List<Button> needsSelection = new List<Button>();

    private readonly Png partitionIcon = new Png("/mnt/System/Icons/hard_disk_drive_pie.png");

    private readonly List<IBlockDevice> devices = new List<IBlockDevice>();
    private IBlockDevice? selectedDevice;
    private Partition? selectedPartition;

    public DiskManagement(int x, int y, int width, int height)
        : base(x, y, width, height, "Disk Management", true)
    {
        root = new DockPanel(0, TitleBar, Width, Height - TitleBar) { useBackground = true };
        AddChild(root);                                     // attach first

        // ---- Toolbar ----
        var toolbar = new DockPanel(0, 0, Width, 30);
        AddToolbarButton(toolbar, "Refresh", 80, false, RescanDisks);
        AddToolbarButton(toolbar, "Format", 80, true, ShowFormatPane);
        // Add New / Delete / Resize / Label here the same way once the StorageManager calls exist.

        // ---- Disk row ----
        var diskRow = new DockPanel(0, 0, Width, 26);
        diskRow.AddDockChild(new Label(0, 0, 40, 20) { text = "Disk:", useBackground = false }, Dock.Left);
        diskCombo = new ComboBox(0, 0, 200);                // ASSUMPTION: (x, y, width)
        diskRow.AddDockChild(diskCombo, Dock.Fill);

        // ---- Partition bar ----
        partitionBar = new PartitionBar(0, 0, Width, 50)
        {
            segmentClicked = seg => SelectPartition(seg?.Tag as Partition, fromBar: true)
        };

        // ---- Status ----
        statusLabel = new Label(0, 0, Width, 20) { text = "", useBackground = false };

        // ---- List ----
        partitionListView = new ListView(0, 0, 300, 200)
        {
            Margin = new Thickness(0, 4, 4, 0),
            columns = new List<ListViewColumn>
            {
                new() { Header = "Partition",   Width = 100 },
                new() { Header = "File system", Width = 100 },
                new() { Header = "Mount",       Width = 70 },
                new() { Header = "Label",       Width = 100 },
                new() { Header = "Size",        Width = 80 },
                new() { Header = "Used",        Width = 80 },
                new() { Header = "Unused",      Width = 80 },
            },
            selectedChanged = item => SelectPartition(item?.Tag as Partition, fromBar: false)
        };

        // ---- Action pane (fixed positions inside, dock = None) ----
        actionPane = new DockPanel(0, 0, 250, 100) { useBackground = true };
        formatTitle = new Label(10, 10, 230, 20) { text = "Format", useBackground = false };
        var fsLabel = new Label(10, 40, 80, 20) { text = "File system:", useBackground = false };
        fsCombo = new ComboBox(95, 38, 140);
        var lblLabel = new Label(10, 70, 80, 20) { text = "Label:", useBackground = false };
        labelBox = new TextField(95, 68, 140, 22);            // ASSUMPTION: (x, y, w, h)
        var warn1 = new Label(10, 100, 230, 20) { text = "Everything on the partition", useBackground = false };
        var warn2 = new Label(10, 118, 230, 20) { text = "will be lost.", useBackground = false };
        formatButton = new Button("Format", 10, 150, 100, 26);
        cancelButton = new Button("Cancel", 120, 150, 100, 26);
        formatButton.leftClickAction = DoFormat;            // ASSUMPTION: Button fires leftClickAction
        cancelButton.leftClickAction = () => actionPane.Visible = false;

        foreach (Component c in new Component[] { formatTitle, fsLabel, fsCombo, lblLabel, labelBox, warn1, warn2, formatButton, cancelButton })
        {
            actionPane.AddChild(c);
        }

        fsCombo.AddItem("FAT32");
        fsCombo.AddItem("ext2");
        actionPane.Visible = false;


        root.AddDockChild(toolbar, Dock.Top);
        root.AddDockChild(diskRow, Dock.Top);
        root.AddDockChild(partitionBar, Dock.Top);
        root.AddDockChild(statusLabel, Dock.Bottom);
        root.AddDockChild(actionPane, Dock.Right);
        root.AddDockChild(partitionListView, Dock.Fill);

        diskCombo.SelectedIndexChanged += OnDiskChanged;

        root.ResolveDockLayout();
        RescanDisks();
    }

    public override void Resize(int w, int h)
    {
        base.Resize(w, h);
        root.X = 0;
        root.Y = TitleBar;
        root.Resize(w, h - TitleBar);
    }


    private void AddToolbarButton(DockPanel bar, string text, int width, bool needsPartition, Action click)
    {
        var button = new Button(text, 0, 0, width, 26) { Margin = new Thickness(0, 2, 4, 2) };
        button.leftClickAction = click;
        bar.AddDockChild(button, Dock.Left);

        if (needsPartition) needsSelection.Add(button);
    }

    private void RescanDisks()
    {
        devices.Clear();
        diskCombo.ClearItems();

        for (int i = 0; i < StorageManager.DeviceCount; i++)
        {
            IBlockDevice? devicee = StorageManager.GetDevice(i);
            if (devicee == null) continue;

            StorageManager.RescanPartitions(devicee);
            devices.Add(devicee);

            string format = "Unknown";
            if (Gpt.IsGpt(devicee))
            {
                format = "GPT";
            }
            else if (Mbr.IsMbr(devicee))
            {
                format = "MBR";
            }
            diskCombo.AddItem($"{devicee.Name} ({ByteFormat.FormatBytes(devicee.BlockSize * devicee.BlockCount)}, {format})");
        }

        selectedDevice = devices.Count > 0 ? devices[0] : null;
        RefreshSelectedDrive();
    }

    private void OnDiskChanged(int index)
    {
        selectedDevice = index >= 0 && index < devices.Count ? devices[index] : null;
        RefreshSelectedDrive();
    }

    private void RefreshSelectedDrive()
    {
        partitionListView.ClearItems();

        List<PartitionBar.Segment> segs = new();

        if (selectedDevice == null)
        {
            partitionBar.SetSegments(segs);
            SelectPartition(null, false);
            return;
        }

        foreach (Partition partition in StorageManager.GetPartitions(selectedDevice))
        {
            ulong size = partition.BlockSize * partition.BlockCount;

            string partitionName = partition.Name ?? "";

            string filesystem = "";
            FsKind fsKind = FsKind.Unknown;
            string mountPoint = "UNMOUNTED";
            string label = "";

            string used = "";
            string unused = "";

            // Find the VFS mount belonging to this partition.
            VfsMount? mount = GetMount(partition);

            if (mount != null)
            {
                byte[] b = ReadBytes(partition, 0, 2048)!;
                fsKind = Probe(partition, out string labelFromProbe);
                mountPoint = mount.MountPoint;
                label = labelFromProbe;

                if (TryGetSpace(mount.MountPoint, out ulong total, out ulong usedBytes, out ulong freeBytes))
                {
                    used = ByteFormat.FormatBytes(usedBytes);
                    unused = ByteFormat.FormatBytes(freeBytes);
                }
            }

            partitionListView.AddItem([partitionName, fsKind.ToString(), mountPoint, label, ByteFormat.FormatBytes(size), used, unused], partitionIcon, tag: partition);

            segs.Add(new PartitionBar.Segment
            {
                Name = partitionName,
                Size = size,
                Tag = partition
            });
        }

        partitionBar.SetSegments(segs);
        SelectPartition(null, false);
    }
    private enum FsKind { Unknown, Fat12, Fat16, Fat32, Ext2 }
    static FsKind Probe(Partition p, out string label)
    {
        label = "";

        // Read the first 2 KiB: boot sector (FAT) + ext2 superblock at byte 1024
        byte[] buf = ReadBytes(p, 0, 2048);
        if (buf == null) return FsKind.Unknown;

        // ext2/3/4: superblock at 1024, magic 0xEF53 at superblock offset 56
        if (buf[1024 + 56] == 0x53 && buf[1024 + 57] == 0xEF)
        {
            label = Ascii(buf, 1024 + 120, 16);   // s_volume_name
            return FsKind.Ext2;
        }

        // FAT: 0x55AA at 510, then check type strings
        if (buf[510] == 0x55 && buf[511] == 0xAA)
        {
            if (Ascii(buf, 82, 8).StartsWith("FAT32"))
            {
                if (buf[66] == 0x29) label = Ascii(buf, 71, 11);
                if (label == "NO NAME") label = "";
                return FsKind.Fat32;
            }
            string t = Ascii(buf, 54, 8);
            if (t.StartsWith("FAT16") || t.StartsWith("FAT12"))
            {
                if (buf[38] == 0x29) label = Ascii(buf, 43, 11);
                if (label == "NO NAME") label = "";
                return t.StartsWith("FAT16") ? FsKind.Fat16 : FsKind.Fat12;
            }
        }

        return FsKind.Unknown;
    }

    static string Ascii(byte[] b, int off, int len)
        => System.Text.Encoding.ASCII.GetString(b, off, len).TrimEnd(' ', '\0');

    // Handles any block size, so byte offsets stay correct
    static byte[]? ReadBytes(Partition p, int byteOffset, int count)
    {
        int bs = (int)p.BlockSize;
        int firstLba = byteOffset / bs;
        int blocks = (byteOffset % bs + count + bs - 1) / bs;
        byte[] raw = new byte[blocks * bs];

        p.ReadBlock((ulong)firstLba, (ulong)blocks, raw);

        byte[] result = new byte[count];
        Array.Copy(raw, byteOffset % bs, result, 0, count);
        return result;
    }

    private VfsMount? GetMount(Partition partition)
    {
        foreach (VfsMount mount in VfsManager.Mounts)
        {

            if (mount.Partition == null) 
            { 
                continue; 
            }

            if (mount.Partition.Name == partition.Name)     
            { 
                return mount; 
            }
        }

        return null;
    }
    private bool TryGetSpace(string mountPoint, out ulong total, out ulong used, out ulong free)
    {
        total = 0;
        used = 0;
        free = 0;

        if (!VfsManager.TryStatFs(mountPoint, out VfsStatFs stats))
        {
            return false;
        }

        total = stats.Blocks * stats.BlockSize;
        free = stats.AvailableBlocks * stats.BlockSize;
        used = total - free;

        return true;
    }
    private void SelectPartition(Partition? p, bool fromBar)
    {
        selectedPartition = p;

        if (fromBar)
        {
            foreach (var item in partitionListView.items)
                if (ReferenceEquals(item.Tag, p)) { partitionListView.SelectItem(item); return; }  // re-enters with fromBar=false
        }
        else
        {
            partitionBar.SelectByTag(p);
        }

        foreach (var b in needsSelection) b.Visible = p != null;

        if (p == null)
        {
            statusLabel.text = "";
            actionPane.Visible = false;
        }
        else
        {
            ulong size = (ulong)p.BlockSize * (ulong)p.BlockCount;
            statusLabel.text = $"{p.Name}: {ByteFormat.FormatBytes(size)}";
            formatTitle.text = $"Format {p.Name}";
        }
        statusLabel.MarkDirty();
    }

    private void ShowFormatPane()
    {
        if (selectedPartition == null) return;
        actionPane.Visible = true;                           // DockPanel re-flows via OnChildVisibilityChanged
    }

    private void DoFormat()
    {
        if (selectedPartition == null) return;

        // TODO: call your actual format routine here, with fsCombo's selection and labelBox.text.
        // Deliberately not guessed: a wrong call here wipes a disk.
        statusLabel.text = $"Format of {selectedPartition.Name} not implemented yet";
        statusLabel.MarkDirty();
    }
}