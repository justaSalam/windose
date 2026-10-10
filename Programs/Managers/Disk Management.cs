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
    private readonly Label diskInfoLabel;
    private readonly ComboBox diskCombo;
    private readonly ScrollView partitionDetailsScroll;
    private readonly DockPanel partitionDetailsContent;
    private readonly Panel partitionNameDetail;
    private readonly Panel fileSystemDetail;
    private readonly Panel mountPointDetail;
    private readonly Panel volumeLabelDetail;
    private readonly Panel sizeDetail;
    private readonly Panel usedDetail;
    private readonly Panel freeDetail;
    private readonly Panel detailsHint;

    private readonly Png partitionIcon = new Png("/mnt/System/Icons/hard_disk_drive_pie.png");

    private readonly List<IBlockDevice> devices = new List<IBlockDevice>();
    private readonly List<PartitionDetails> partitionDetails = new List<PartitionDetails>();
    private IBlockDevice? selectedDevice;

    private sealed class PartitionDetails
    {
        public Partition Partition;
        public string FileSystem = "Unknown";
        public string MountPoint = "Unmounted";
        public string VolumeLabel = "Not available";
        public string Used = "Not available";
        public string Free = "Not available";
        public ulong Size;
        public bool HasSize;

        public PartitionDetails(Partition partition)
        {
            Partition = partition;
        }
    }

    public DiskManagement(int x, int y, int width, int height)
        : base(x, y, width, height, "Disk Management", true)
    {
        root = new DockPanel(0, TitleBar, Width, Height - TitleBar)
        {
            useBackground = true,
            backgroundColor = Palette.ControlFace,
            Margin = new Thickness(0),
            Padding = new Thickness(4),
        };
        AddChild(root);                                     // attach first

        // ---- Toolbar ----
        var toolbar = new DockPanel(0, 0, Width, 30)
        {
            Margin = new Thickness(0),
            Padding = new Thickness(0),
        };
        AddToolbarButton(toolbar, "Refresh", 86, RescanDisks);

        // ---- Disk row ----
        var diskRow = new DockPanel(0, 0, Width, 26)
        {
            Margin = new Thickness(0),
            Padding = new Thickness(0),
        };
        diskRow.AddDockChild(new Label(0, 0, 40, 20)
        {
            text = "Disk:",
            useBackground = false,
            Margin = new Thickness(0),
        }, Dock.Left);
        diskCombo = new ComboBox(0, 0, 200) { Margin = new Thickness(0) };
        diskRow.AddDockChild(diskCombo, Dock.Fill);

        diskInfoLabel = new Label(0, 0, Width, 22)
        {
            text = "No disk selected",
            useBackground = false,
            clampSize = false,
            Margin = new Thickness(4, 2, 4, 0),
        };

        // ---- Partition bar ----
        partitionBar = new PartitionBar(0, 0, Width, 50)
        {
            Margin = new Thickness(0),
            segmentClicked = seg => SelectPartition(seg?.Tag as Partition, fromBar: true)
        };

        // ---- Status ----
        statusLabel = new Label(0, 0, Width, 20)
        {
            text = "",
            useBackground = false,
            Margin = new Thickness(0),
        };

        // ---- List ----
        partitionListView = new ListView(0, 0, 300, 200)
        {
            Margin = new Thickness(0, 4, 4, 0),
            columns = new List<ListViewColumn>
            {
                new() { Header = "Partition",   Width = 104 },
                new() { Header = "FS",          Width = 64 },
                new() { Header = "Mount point", Width = 96 },
                new() { Header = "Capacity",    Width = 84 },
            },
            selectedChanged = item => SelectPartition(item?.Tag as Partition, fromBar: false)
        };

        partitionDetailsScroll = new ScrollView(0, 0, 210, Height)
        {
            useBackground = true,
            backgroundColor = Palette.ControlWhite,
            showHorizontalScrollbar = false,
            showVerticalScrollbar = true,
            clampSize = false,
            Margin = new Thickness(0),
        };
        partitionDetailsContent = new DockPanel(0, 0, 190, 320)
        {
            useBackground = true,
            backgroundColor = Palette.ControlWhite,
            Padding = new Thickness(6),
            Margin = new Thickness(0),
        };
        partitionDetailsContent.AddDockChild(CreateDetailRow("Partition details", Palette.ControlFace, 30), Dock.Top);
        partitionNameDetail = CreateDetailRow("Partition: —", Palette.ControlWhite, 42);
        fileSystemDetail = CreateDetailRow("File system: —", Palette.ControlWhite, 26);
        mountPointDetail = CreateDetailRow("Mount point: —", Palette.ControlWhite, 42);
        volumeLabelDetail = CreateDetailRow("Volume label: —", Palette.ControlWhite, 38);
        sizeDetail = CreateDetailRow("Capacity: —", Palette.ControlWhite, 26);
        usedDetail = CreateDetailRow("Used: —", Palette.ControlWhite, 26);
        freeDetail = CreateDetailRow("Free: —", Palette.ControlWhite, 26);
        detailsHint = CreateDetailRow("Select a partition to view its details. Disk operations are read-only.", Palette.ControlWhite, 52);
        partitionDetailsContent.AddDockChild(partitionNameDetail, Dock.Top);
        partitionDetailsContent.AddDockChild(fileSystemDetail, Dock.Top);
        partitionDetailsContent.AddDockChild(mountPointDetail, Dock.Top);
        partitionDetailsContent.AddDockChild(volumeLabelDetail, Dock.Top);
        partitionDetailsContent.AddDockChild(sizeDetail, Dock.Top);
        partitionDetailsContent.AddDockChild(usedDetail, Dock.Top);
        partitionDetailsContent.AddDockChild(freeDetail, Dock.Top);
        partitionDetailsContent.AddDockChild(detailsHint, Dock.Fill);
        partitionDetailsScroll.SetContent(partitionDetailsContent, 190, 320);


        root.AddDockChild(toolbar, Dock.Top);
        root.AddDockChild(diskRow, Dock.Top);
        root.AddDockChild(diskInfoLabel, Dock.Top);
        root.AddDockChild(partitionBar, Dock.Top);
        root.AddDockChild(statusLabel, Dock.Bottom);
        root.AddDockChild(partitionDetailsScroll, Dock.Right);
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


    private static Panel CreateDetailRow(string text, System.Drawing.Color color, int height)
    {
        return new Panel(color, 0, 0, 100, height)
        {
            useBackground = color != Palette.ControlWhite,
            text = text,
            fontSize = 14,
            textColor = Palette.ControlBlack,
            textOffsetX = 4,
            wrapText = true,
            clampSize = false,
            Margin = new Thickness(0),
        };
    }

    private static void AddToolbarButton(DockPanel bar, string text, int width, Action click)
    {
        var button = new Button(text, 0, 0, width, 26) { Margin = new Thickness(0, 2, 4, 2) };
        button.leftClickAction = click;
        bar.AddDockChild(button, Dock.Left);
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
            diskCombo.AddItem($"{devicee.Name} ({FormatCapacity(devicee.BlockSize, devicee.BlockCount)}, {format})");
        }

        if (devices.Count > 0)
            diskCombo.SelectedIndex = 0;
        else
            OnDiskChanged(-1);
    }

    private void OnDiskChanged(int index)
    {
        selectedDevice = index >= 0 && index < devices.Count ? devices[index] : null;
        RefreshSelectedDrive();
    }

    private void RefreshSelectedDrive()
    {
        partitionListView.ClearItems();
        partitionDetails.Clear();

        List<PartitionBar.Segment> segs = new();

        if (selectedDevice == null)
        {
            partitionBar.SetSegments(segs);
            diskInfoLabel.text = "No disk selected";
            diskInfoLabel.MarkDirty();
            SelectPartition(null, false);
            return;
        }

        foreach (Partition partition in StorageManager.GetPartitions(selectedDevice))
        {
            bool hasSize = TryGetByteSize(partition.BlockSize, partition.BlockCount, out ulong size);

            string partitionName = partition.Name ?? "";
            FsKind fsKind = Probe(partition, out string labelFromProbe);
            string mountPoint = "Unmounted";
            string label = string.IsNullOrWhiteSpace(labelFromProbe) ? "Not available" : labelFromProbe;
            string used = "Not available";
            string free = "Not available";

            // Find the VFS mount belonging to this partition.
            VfsMount? mount = GetMount(partition);

            if (mount != null)
            {
                mountPoint = mount.MountPoint;

                if (TryGetSpace(mount.MountPoint, out _, out ulong usedBytes, out ulong freeBytes))
                {
                    used = ByteSize.Format(usedBytes);
                    free = ByteSize.Format(freeBytes);
                }
            }

            PartitionDetails details = new PartitionDetails(partition)
            {
                FileSystem = fsKind.ToString(),
                MountPoint = mountPoint,
                VolumeLabel = label,
                Used = used,
                Free = free,
                Size = hasSize ? size : 0,
                HasSize = hasSize,
            };
            partitionDetails.Add(details);

            partitionListView.AddItem([partitionName, details.FileSystem, mountPoint, hasSize ? ByteSize.Format(size) : "Unavailable"], partitionIcon, tag: partition);

            segs.Add(new PartitionBar.Segment
            {
                Name = partitionName,
                Size = size,
                Tag = partition
            });
        }

        partitionBar.SetSegments(segs);
        string format = Gpt.IsGpt(selectedDevice) ? "GPT" : Mbr.IsMbr(selectedDevice) ? "MBR" : "Unknown partition table";
        string diskSize = FormatCapacity(selectedDevice.BlockSize, selectedDevice.BlockCount);
        diskInfoLabel.text = $"{selectedDevice.Name} | {format} | {diskSize} | {selectedDevice.BlockSize}-byte sectors | {partitionDetails.Count} partitions";
        diskInfoLabel.MarkDirty();
        statusLabel.text = $"{partitionDetails.Count} partitions";
        statusLabel.MarkDirty();
        SelectPartition(null, false);
    }

    private void UpdatePartitionDetails(Partition? partition)
    {
        PartitionDetails? details = null;
        if (partition != null)
        {
            foreach (PartitionDetails candidate in partitionDetails)
            {
                if (ReferenceEquals(candidate.Partition, partition))
                {
                    details = candidate;
                    break;
                }
            }
        }

        if (details == null)
        {
            partitionNameDetail.text = "Partition: —";
            fileSystemDetail.text = "File system: —";
            mountPointDetail.text = "Mount point: —";
            volumeLabelDetail.text = "Volume label: —";
            sizeDetail.text = "Capacity: —";
            usedDetail.text = "Used: —";
            freeDetail.text = "Free: —";
            detailsHint.text = partitionDetails.Count == 0
                ? "No partitions are available on this disk. Disk operations are read-only."
                : "Select a partition to view its details. Disk operations are read-only.";
        }
        else
        {
            partitionNameDetail.text = $"Partition: {(string.IsNullOrWhiteSpace(details.Partition.Name) ? "Unnamed" : details.Partition.Name)}";
            fileSystemDetail.text = $"File system: {details.FileSystem}";
            mountPointDetail.text = $"Mount point: {details.MountPoint}";
            volumeLabelDetail.text = $"Volume label: {details.VolumeLabel}";
            sizeDetail.text = details.HasSize
                ? $"Capacity: {ByteSize.Format(details.Size)}"
                : "Capacity: Unavailable";
            usedDetail.text = $"Used: {details.Used}";
            freeDetail.text = $"Free: {details.Free}";
            detailsHint.text = "Disk operations are read-only; no partition changes will be made.";
        }

        partitionNameDetail.MarkDirty();
        fileSystemDetail.MarkDirty();
        mountPointDetail.MarkDirty();
        volumeLabelDetail.MarkDirty();
        sizeDetail.MarkDirty();
        usedDetail.MarkDirty();
        freeDetail.MarkDirty();
        detailsHint.MarkDirty();
    }
    private enum FsKind { Unknown, Fat12, Fat16, Fat32, Ext2 }
    private static FsKind Probe(Partition p, out string label)
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

    private static string Ascii(byte[] b, int off, int len)
        => System.Text.Encoding.ASCII.GetString(b, off, len).TrimEnd(' ', '\0');

    // Handles any block size, so byte offsets stay correct
    private static byte[]? ReadBytes(Partition p, int byteOffset, int count)
    {
        if (byteOffset < 0 || count < 0 || p.BlockSize == 0 || p.BlockSize > int.MaxValue)
            return null;

        if (count == 0)
            return Array.Empty<byte>();

        ulong blockSize = p.BlockSize;
        ulong firstLba = (ulong)byteOffset / blockSize;
        ulong endByte = (ulong)byteOffset + (ulong)count;
        ulong endLba = (endByte + blockSize - 1) / blockSize;
        if (endLba > p.BlockCount)
            return null;

        ulong blockCount = endLba - firstLba;
        ulong rawSize = blockCount * blockSize;
        if (rawSize > int.MaxValue)
            return null;

        byte[] raw = new byte[(int)rawSize];
        int offset = (int)((ulong)byteOffset % blockSize);

        try
        {
            p.ReadBlock(firstLba, blockCount, raw);
        }
        catch (Exception)
        {
            return null;
        }

        byte[] result = new byte[count];
        Array.Copy(raw, offset, result, 0, count);
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

            Partition mountedPartition = mount.Partition;
            if (ReferenceEquals(mountedPartition.Host, partition.Host) &&
                mountedPartition.StartSector == partition.StartSector &&
                mountedPartition.BlockCount == partition.BlockCount)
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

        if (stats.BlockSize == 0 || stats.AvailableBlocks > stats.Blocks ||
            stats.Blocks > ulong.MaxValue / stats.BlockSize)
            return false;

        total = stats.Blocks * stats.BlockSize;
        free = stats.AvailableBlocks * stats.BlockSize;
        used = total - free;

        return true;
    }

    private static bool TryGetByteSize(ulong blockSize, ulong blockCount, out ulong size)
    {
        size = 0;
        if (blockSize == 0 || blockCount > ulong.MaxValue / blockSize)
            return false;

        size = blockSize * blockCount;
        return true;
    }

    private static string FormatCapacity(ulong blockSize, ulong blockCount)
    {
        return TryGetByteSize(blockSize, blockCount, out ulong size)
            ? ByteSize.Format(size)
            : "Unavailable";
    }

    private void SelectPartition(Partition? p, bool fromBar)
    {
        if (fromBar)
        {
            foreach (var item in partitionListView.items)
                if (ReferenceEquals(item.Tag, p)) { partitionListView.SelectItem(item); return; }  // re-enters with fromBar=false
        }
        else
        {
            partitionBar.SelectByTag(p);
        }

        UpdatePartitionDetails(p);
        statusLabel.text = p == null
            ? $"{partitionDetails.Count} partitions"
            : $"Selected: {p.Name} | {FormatCapacity(p.BlockSize, p.BlockCount)}";
        statusLabel.MarkDirty();
    }
}
