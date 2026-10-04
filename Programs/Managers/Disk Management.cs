using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.Vfs;
using System.Drawing;

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
                new() { Header = "File system", Width = 90 },
                new() { Header = "Mount",       Width = 70 },
                new() { Header = "Label",       Width = 100 },
                new() { Header = "Size",        Width = 80 },
                new() { Header = "Used",        Width = 70 },
                new() { Header = "Unused",      Width = 70 },
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
        RefreshPartitions();
    }

    private void OnDiskChanged(int index)
    {
        selectedDevice = index >= 0 && index < devices.Count ? devices[index] : null;
        RefreshPartitions();
    }

    private void RefreshPartitions()
    {
        partitionListView.ClearItems();
        var segs = new List<PartitionBar.Segment>();
     
        foreach (Partition p in StorageManager.Partitions)
        {
            
            if (p == null) continue;
            // TODO: when selectedDevice is set, skip partitions that belong to other devices
            //       (needs a Partition -> device property; I haven't seen one).

            ulong size = p.BlockSize * p.BlockCount;
            string name = p.Name ?? "";

            
            
            partitionListView.AddItem([name, "", "", "", ByteFormat.FormatBytes(size), "", ""],   // fs/mount/label/used: fill from Partition
                partitionIcon,
                tag: p);

            segs.Add(new PartitionBar.Segment { Name = name, Size = size, Tag = p });
        }

        partitionBar.SetSegments(segs);
        SelectPartition(null, fromBar: false);
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