using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Storage;

public sealed class DiskPart : InteractiveShellCommand
{
    public override string Name => "diskpart";
    public override string Description => "Interactive disk partitioning utility.";
    public override string Usage => "diskpart";
    protected override string Prompt => "DISKPART> ";

    private IBlockDevice? selectedDisk;
    private Partition? selectedPartition;

    protected override SubcommandDispatcher BuildDispatcher()
    {
        selectedDisk = null;
        selectedPartition = null;

        return new SubcommandDispatcher(Usage)
            .Add("list", "disk | partition", 1, ListItems)
            .Add("select", "disk <n> | partition <n>", 2, Select)
            .Add("create", "partition primary [size=<mb>]", 1, CreatePartition)
            .Add("delete", "partition", 0, DeletePartition)
            .Add("details", "disk | partition", 1, Detail);
    }

    private void ListItems(CommandContext context, string[] args)
    {
        if (args[0].Equals("disk", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < StorageManager.DeviceCount; i++)
            {
                IBlockDevice? device = StorageManager.GetDevice(i);

                if (device == null)
                {
                    context.WriteLine($"Disk {i}    <unavailable>");
                    continue;
                }

                string marker = ReferenceEquals(device, selectedDisk) ? "*" : " ";
                context.WriteLine($"{marker} Disk {i}    {device.Name}    {ByteFormat.FormatBytes(device.BlockSize * device.BlockCount)}");
            }
            return;
        }
        if (args[0].Equals("partition", StringComparison.OrdinalIgnoreCase))
        {
            if (selectedDisk == null)
            {
                context.WriteLine("No disk selected.");
                return;
            }
            int index = 0;
            foreach (Partition partition in StorageManager.Partitions)
            {
                string marker = ReferenceEquals(partition, selectedPartition) ? "*" : " ";
                context.WriteLine($"{marker} Partition {index}    {partition.Name}    {ByteFormat.FormatBytes(partition.BlockSize * partition.BlockCount)}");
                index++;
            }
            return;
        }
        context.WriteLine("Usage: list disk | partition");
    }

    private void Select(CommandContext context, string[] args)
    {
        if (!int.TryParse(args[1], out int index)) { context.WriteLine("Expected a number."); return; }

        if (args[0].Equals("disk", StringComparison.OrdinalIgnoreCase))
        {
            IBlockDevice? device = StorageManager.GetDevice(index);
            if (device == null)
            {
                context.WriteLine("No such disk.");
                return;
            }
            selectedDisk = device;
            selectedPartition = null;
            context.WriteLine("Disk " + index + " is now the selected disk.");
            return;
        }
        context.WriteLine("Usage: select disk <n> | partition <n>");
    }

    private void CreatePartition(CommandContext context, string[] args)
    {
        if (selectedDisk == null)
        {
            context.WriteLine("No disk selected.");
            return;
        }

        if (args.Length < 3 || !ulong.TryParse(args[2], out ulong sizeMb) || sizeMb == 0)
        {
            context.WriteLine("Usage: create partition primary <size in MB>");
            return;
        }

        ulong sectorCount = sizeMb * ByteFormat.mega / selectedDisk.BlockSize;

        if (!Gpt.IsGpt(selectedDisk))
        {
            context.WriteLine("Disk is not GPT. Creating GPT partition table.");
            Gpt.Create(selectedDisk);
        }



        ulong alignment = 1024 * 1024 / selectedDisk.BlockSize;
        ulong start = alignment;

        StorageManager.RescanPartitions(selectedDisk);
        foreach (Partition p in StorageManager.GetPartitions(selectedDisk))
        {
            ulong end = p.StartSector + p.BlockCount;
            if (end > start)
                start = (end + alignment - 1) / alignment * alignment;
        }

        ulong usable = selectedDisk.BlockCount - 34;   // last usable LBA, backup GPT lives after it
        if (start + sectorCount > usable)
        {
            context.WriteLine($"Not enough free space (start={start}, need={sectorCount}, disk={selectedDisk.BlockCount}).");
            return;
        }

        if (!PartitionManager.Create(selectedDisk, start, sectorCount, 0x0C, Gpt.BasicDataPartitionType))
        {
            context.WriteLine("Failed to create partition.");
            return;
        }

        StorageManager.RescanPartitions(selectedDisk);
        context.WriteLine($"Created {sizeMb} MB partition on {selectedDisk.Name}");
    }

    private void DeletePartition(CommandContext context, string[] args)
    {
        if (selectedPartition == null)
        {
            context.WriteLine("No partition selected.");
            return;
        }
        context.WriteLine("Deleted partition " + selectedPartition.Name);
        selectedPartition = null;
    }

    private void Detail(CommandContext context, string[] args)
    {
        if (args[0].Equals("disk", StringComparison.OrdinalIgnoreCase))
        {
            if (selectedDisk == null)
            {
                context.WriteLine("No disk selected.");
                return;
            }
            context.WriteLine($"Name: {selectedDisk.Name}");
            context.WriteLine($"Total size: {ByteFormat.FormatBytes(selectedDisk.BlockSize * selectedDisk.BlockCount)}");
            context.WriteLine($"Block size: {selectedDisk.BlockSize}");
            context.WriteLine($"Block count: {selectedDisk.BlockCount}");
        }
    }
}