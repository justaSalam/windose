using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.HAL.Vfs;
using Cosmos.Kernel.System.Filesystems.Fat;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.Vfs;
using System;
using Windose.System.Kernel;
using Windose.System.System_Calls;

/// <summary>
/// Mounts the WindoseInstaller disk layout and prepares /mnt/System.
///
/// Expected GPT layout (same constants as WindoseInstaller):
///   1. BIOS boot  — type "Hah!IdontNeedEFI", raw, 1 MiB @ LBA 2048 (never mount)
///   2. ESP        — EFI System, FAT32 label WINBOOT  → /boot
///   3. Data       — Basic Data, FAT32 label WINDOSE → /mnt
///
/// System assets live at /mnt/System/{Fonts,Icons,Wallpapers,Cursors}.
/// </summary>
public static class FileSystemManager
{
    public static VfsManager.VfsMount? mount;
    public static VfsManager.VfsMount? bootMount;

    /// <summary>EFI System Partition type GUID.</summary>
    private static readonly Guid EspType = Guid.Parse("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");

    /// <summary>Limine BIOS boot partition (on-disk bytes = ASCII "Hah!IdontNeedEFI").</summary>
    private static readonly Guid BiosBootType = new Guid(
        new byte[]
        {
            0x48, 0x61, 0x68, 0x21, 0x49, 0x64, 0x6F, 0x6E,
            0x74, 0x4E, 0x65, 0x65, 0x64, 0x45, 0x46, 0x49
        });

    // Must match WindoseInstaller.Kernel partition geometry.
    private const ulong EspSectors = 262144;
    private const ulong BiosBootStartLba = 2048;
    private const ulong BiosBootSectors = 2048;

    private const string DataMount = "/mnt";
    private const string BootMount = "/boot";
    private const string SystemRoot = "/mnt/System";

    public static void Setup()
    {
        Console.WriteLine("[fs] register fat");
        FatFilesystemType fat = new FatFilesystemType();
        VfsManager.RegisterFilesystem("fat", fat);

        IBlockDevice? storageDevice = StorageManager.PrimaryDevice;
        if (storageDevice == null)
        {
            SystemLogger.WriteLine("FileSystemManager", "Storage device not found", ConsoleMessageType.Error);
            return;
        }

        SystemLogger.WriteLine("FileSystemManager", $"primary={storageDevice.Name} partitions={StorageManager.Partitions.Count}", ConsoleMessageType.Log);

        if (!Gpt.IsGpt(storageDevice))
        {
            SystemLogger.WriteLine(
                "FileSystemManager",
                "No GPT found. Boot from WindoseInstaller and install to disk first.",
                ConsoleMessageType.Error);
            return;
        }

        // Prefer the installer's fixed LBAs first. That path is known-good when the
        // disk was prepared by WindoseInstaller. Type-based discovery is fallback only.
        // Do NOT remount the ESP here when we were booted from it — that hangs some
        // AHCI/virtio stacks (CD boot worked because the ESP was not the boot path).
        Partition? dataPartition = FindPartition(storageDevice, InstallerDataStartLba)
                                   ?? FindDataPartitionByType(storageDevice);

        if (dataPartition == null)
        {
            // Last resort: pick the largest non-ESP, non-BIOS-boot partition already scanned.
            dataPartition = FindLargestUsablePartition(storageDevice);
        }

        if (dataPartition == null)
        {
            SystemLogger.WriteLine(
                "FileSystemManager",
                "WINDOSE data partition not found.",
                ConsoleMessageType.Error);
            return;
        }

        Console.WriteLine($"[fs] mounting data LBA {dataPartition.StartSector} → {DataMount}");
        if (!VfsManager.TryMount("fat", dataPartition, MountFlags.None, DataMount, out VfsManager.VfsMount? dataMount))
        {
            SystemLogger.WriteLine("FileSystemManager", "Failed to mount data partition at /mnt", ConsoleMessageType.Error);
            return;
        }

        mount = dataMount;
        SystemLogger.WriteLine(
            "FileSystemManager",
            $"Mounted data → {dataMount!.MountPoint} (LBA {dataPartition.StartSector})");

        // ESP (/boot) is optional and intentionally deferred — remounting the boot
        // ESP during BeforeRun locks up when Windose was started from that partition.
        // Call TryMountEsp() later if you need to update Limine/Windose.elf from the OS.

        Console.WriteLine("[fs] system dirs");
        CreateSystemDirectories();

        // Defer registry + script writes: first HDD-boot after install has been seen
        // to lock up on FAT writes during BeforeRun. Mount-only is enough to reach Run().
        try
        {
            Console.WriteLine("[fs] registry");
            Registry.Initialize();
            WriteDefaultBreezeScripts();
            TryWriteSetupMarker();
        }
        catch (Exception ex)
        {
            SystemLogger.WriteLine(
                "FileSystemManager",
                $"Post-mount init warning: {ex.Message}",
                ConsoleMessageType.Warning);
        }
        TryMountEsp();
        Console.WriteLine("[fs] setup done");
    }

    /// <summary>
    /// Optional: mount ESP at /boot after the GUI is up. Safe to call from Run() or a tool.
    /// </summary>
    public static bool TryMountEsp()
    {
        if (bootMount != null)
        {
            return true;
        }

        IBlockDevice? device = StorageManager.PrimaryDevice;
        if (device == null || !Gpt.IsGpt(device))
        {
            return false;
        }

        Partition? esp = FindPartition(device, InstallerEspStartLba)
                         ?? FindEspPartitionByType(device);
        if (esp == null)
        {
            return false;
        }

        if (!VfsManager.TryMount("fat", esp, MountFlags.None, BootMount, out VfsManager.VfsMount? espMount))
        {
            return false;
        }

        bootMount = espMount;
        SystemLogger.WriteLine(
            "FileSystemManager",
            $"Mounted ESP → {espMount!.MountPoint} (LBA {esp.StartSector})");
        return true;
    }

    private static ulong InstallerEspStartLba => BiosBootStartLba + BiosBootSectors;
    private static ulong InstallerDataStartLba => InstallerEspStartLba + EspSectors;

    /// <summary>
    /// Prefer GPT Basic Data (largest), skip ESP + BIOS boot.
    /// </summary>
    private static Partition? FindDataPartitionByType(IBlockDevice device)
    {
        GptPartitionEntry? best = null;

        foreach (GptPartitionEntry entry in Gpt.Parse(device))
        {
            if (entry.PartitionType == BiosBootType || entry.PartitionType == EspType)
            {
                continue;
            }

            if (entry.PartitionType != Gpt.BasicDataPartitionType)
            {
                continue;
            }

            if (best == null || entry.SectorCount > best.SectorCount)
            {
                best = entry;
            }
        }

        return best != null ? FindPartition(device, best.StartSector) : null;
    }

    private static Partition? FindEspPartitionByType(IBlockDevice device)
    {
        foreach (GptPartitionEntry entry in Gpt.Parse(device))
        {
            if (entry.PartitionType == EspType)
            {
                return FindPartition(device, entry.StartSector);
            }
        }

        return null;
    }

    private static Partition? FindLargestUsablePartition(IBlockDevice device)
    {
        Partition? best = null;
        ulong espStart = BiosBootStartLba + BiosBootSectors;

        foreach (Partition partition in EnumeratePartitions(device))
        {
            // Skip BIOS boot and ESP by known installer LBAs.
            if (partition.StartSector == BiosBootStartLba || partition.StartSector == espStart)
            {
                continue;
            }

            if (best == null || partition.BlockCount > best.BlockCount)
            {
                best = partition;
            }
        }

        return best;
    }

    private static IEnumerable<Partition> EnumeratePartitions(IBlockDevice device)
    {
        foreach (Partition partition in StorageManager.GetPartitions(device))
        {
            yield return partition;
        }

        foreach (Partition partition in StorageManager.Partitions)
        {
            if (partition.Host == device)
            {
                yield return partition;
            }
        }
    }

    private static Partition? FindPartition(IBlockDevice device, ulong startSector)
    {
        foreach (Partition partition in StorageManager.GetPartitions(device))
        {
            if (partition.StartSector == startSector)
            {
                return partition;
            }
        }

        foreach (Partition partition in StorageManager.Partitions)
        {
            if (partition.Host == device && partition.StartSector == startSector)
            {
                return partition;
            }
        }

        return null;
    }

    /// <summary>
    /// When using an extension use format '.ext'
    /// </summary>
    public static string GetUniquePath(string directory, string baseName, string extension = "")
    {
        string path = Path.Combine(directory, $"{baseName}{extension}");

        if (!File.Exists(path) && !Directory.Exists(path))
            return path;

        int i = 2;
        while (true)
        {
            path = Path.Combine(directory, $"{baseName} ({i}){extension}");

            if (!File.Exists(path) && !Directory.Exists(path))
                return path;

            i++;
        }
    }

    private static void CreateSystemDirectories()
    {
        Directory.CreateDirectory(SystemRoot);
        Directory.CreateDirectory(SystemRoot + "/Fonts");
        Directory.CreateDirectory(SystemRoot + "/Icons");
        Directory.CreateDirectory(SystemRoot + "/Wallpapers");
        Directory.CreateDirectory(SystemRoot + "/Cursors");
        Directory.CreateDirectory(SystemRoot + "/Services");
        Directory.CreateDirectory(SystemRoot + "/Breeze");
        Directory.CreateDirectory("/mnt/user");
        Directory.CreateDirectory("/mnt/user/Desktop");
        Directory.CreateDirectory("/mnt/Programs");
        Directory.CreateDirectory("/mnt/Apps");
    }

    /// <summary>
    /// Optional: fill missing /mnt/System assets from embedded resources.
    /// Not called during BeforeRun (can be very slow / hang on some HDD boot paths).
    /// </summary>
    public static void EnsureSystemResources()
    {
        bool fontsOk =
            File.Exists(SystemRoot + "/Fonts/ARIAL.TTF") ||
            File.Exists(SystemRoot + "/Fonts/ARIAL.ttf");
        bool iconsOk = Directory.Exists(SystemRoot + "/Icons") &&
                       File.Exists(SystemRoot + "/Icons/desktop.png");
        bool cursorsOk = File.Exists(SystemRoot + "/Cursors/arrow.png");
        bool wallsOk = File.Exists(SystemRoot + "/Wallpapers/Lithium.png");

        if (fontsOk && iconsOk && cursorsOk && wallsOk)
        {
            SystemLogger.WriteLine("FileSystemManager", "System resources present under /mnt/System");
            return;
        }

        SystemLogger.WriteLine(
            "FileSystemManager",
            "Some /mnt/System assets missing — extracting embedded resources.",
            ConsoleMessageType.Warning);
        ResourceLoader.LoadAssemblyResources();
    }

    private static void WriteDefaultBreezeScripts()
    {
        WriteIfMissing(SystemRoot + "/Breeze/startup.breeze", StartupService);
        WriteIfMissing(SystemRoot + "/Breeze/main.breeze", StarterProgram);
        WriteIfMissing(SystemRoot + "/Breeze/About Windose.breeze", AboutControlPanelApplet);
    }

    private static void WriteIfMissing(string path, string contents)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, contents);
        }
    }

    private static void TryWriteSetupMarker()
    {
        try
        {
            if (!File.Exists("/mnt/.setup"))
            {
                File.WriteAllText("/mnt/.setup", "windose-installer");
            }
        }
        catch
        {
            // Non-fatal.
        }
    }

    public static string NormalizePath(string path)
    {
        path = path.Replace('\\', '/');

        while (path.Contains("//"))
            path = path.Replace("//", "/");

        return path;
    }

    public static string Combine(string directory, string name)
    {
        string parent = NormalizePath(directory);
        if (string.IsNullOrEmpty(name)) return parent;
        return NormalizePath(parent + "\\" + name.Trim('\\', '/'));
    }

    public static string GetParent(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string value = NormalizePath(path);
        if (value.Length <= 3) return "";
        int separator = value.LastIndexOf('\\');
        if (separator <= 2) return value.Substring(0, 3);
        return value.Substring(0, separator);
    }

    public static string GetName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string value = NormalizePath(path);
        if (value.Length <= 3) return value;
        int separator = value.LastIndexOf('\\');
        return separator < 0 ? value : value.Substring(separator + 1);
    }

    public static string GetExtension(string path)
    {
        string name = GetName(path);
        int dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name.Substring(dot);
    }

    private const string StarterProgram = @"// Temporary in-memory Breeze file
let main = window(""Hello Breeze"", 180, 120, 520, 260);
let root = windowRoot(main);
let message = panel(""This file was opened from the temporary disk."", 40);
dock(root, message, ""top"");
show(main);
";

    private const string AboutControlPanelApplet = @"// Control Panel applet
let main = window(""About Windose"", 180, 140, 480, 240);
let root = windowRoot(main);
let body = stackPanel(""vertical"");
dock(root, body, ""fill"");
let heading = panel(""Windose Control Panel"", 36);
let description = panel(""This applet is written in Breeze."", 36);
let closeButton = button(""OK"", 80, 28);
stack(body, heading);
stack(body, description);
stack(body, closeButton);
on closeButton.click {
    close(main);
}
show(main);
";

    private const string StartupService = @"// Windose one-shot service launcher
let serviceFiles = getFiles(""/mnt/System/Services"");
let index = 0;
while (index < listCount(serviceFiles)) {
    let path = listGet(serviceFiles, index);
    if (fileName(path) != ""startup.breeze"") {
        startService(path);
    }
    index = index + 1;
}
log(""Service startup complete"");
";
}
