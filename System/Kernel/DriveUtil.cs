using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.HAL.Vfs;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.Vfs;

public static class DriveUtils
{

    public static void CreateMBR(IBlockDevice device)
    {
        Mbr.Create(device);
    }
    public static void DeleteMBRPartition(IBlockDevice device, int index)
    {
        Mbr.DeletePartition(device, index);
    }

    public static bool CreateMbrPartition(IBlockDevice device, ulong startBlock = 0, ulong blockCount = 0, byte mbrSystemId = 0x0C)
    {
        // Default start block, aligned to 1MB
        if (startBlock == 0)
        {
            startBlock = 1048576UL / device.BlockSize;
        }

        //Fill the remaining disk space
        if (blockCount == 0)
        {
            if (device.BlockCount > startBlock)
            {
                blockCount = device.BlockCount - startBlock;
            }
            else
            {
                return false; // The device is too small to fit the 1 MiB alignment offset
            }
        }

        // MBR Boundary Safety Check
        // The absolute highest block address or block count an MBR table can hold is 4,294,967,295
        if (startBlock > uint.MaxValue)
        {
            return false;
            // Start address is physically out of bounds for an MBR table
        }

        if (blockCount > uint.MaxValue)
        {
            blockCount = uint.MaxValue;// Clamp the partition size to the maximum structural limit allowed by MBR
        }
        return PartitionManager.Create(device, startBlock, blockCount, mbrSystemId, Guid.Empty);
    }
    public static void RescanParitions(IBlockDevice device)
    {
        StorageManager.RescanPartitions(device);
    }

    public static bool FAT32FormatDrive(string name, ReadOnlySpan<char> source, IVfsFormatOptions vfsFormatOptions)
    {
        return VfsManager.TryFormat(name, source, vfsFormatOptions);
    }

    public static bool FAT32MountDrive(string name, ReadOnlySpan<char> source, MountFlags mountFlags, string mountPoint, out VfsManager.VfsMount? mount)
    {
        return VfsManager.TryMount(name, source, mountFlags, mountPoint, out mount);
    }

}