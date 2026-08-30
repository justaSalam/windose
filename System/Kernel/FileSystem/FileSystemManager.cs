
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.HAL.Vfs;
using Cosmos.Kernel.System;
using Cosmos.Kernel.System.Filesystems.Fat;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.Vfs;
using Windose.System.System_Calls;


/// <summary>
/// TODO: Replace every File and Directory call with one made from this class for proper event handling
/// </summary>
public static class FileSystemManager
{
    public static VfsManager.VfsMount? mount;


    public static void Setup()
    {
        FatFilesystemType fat = new FatFilesystemType();


        VfsManager.RegisterFilesystem("fat", fat);

        IBlockDevice? storageDevice = StorageManager.PrimaryDevice;
        if (storageDevice == null)
        {
            SystemLogger.WriteLine("FileSystemManager", "Storage device not found", ConsoleMessageType.Error);
            return;
        }

        //Mbr partition didn't work on vmware
        if (!Gpt.IsGpt(storageDevice))
        {
            Gpt.Create(storageDevice);

            DriveUtils.CreateGptPartition(storageDevice);

            StorageManager.RescanPartitions(storageDevice);

            VfsManager.TryFormat("fat", "0", new FatFormatOptions()
            {
                VolumeLabel = "SYSTEM",
                Type = FatType.Fat32
            });

            Power.Reboot();
        }



        if (VfsManager.TryMount("fat", "0", MountFlags.None, "/mnt", out VfsManager.VfsMount? mount))
        {
            SystemLogger.WriteLine("FileSystemManager", $"Mounted -> {mount.Name} partition {mount.Source} - at -> {mount.MountPoint}");
            Serial.WriteString($"Mounted -> {mount.Name} partition {mount.Source} - at -> {mount.MountPoint}\n");
        }
        else
        {
            SystemLogger.WriteLine("FileSystemManager", "Failed to mount filesystem", ConsoleMessageType.Error);
            Serial.WriteString($"Failed to mount filesystem\n");

            return;
        }

        CreateSystemDirectories();
        Registry.Initialize();

        File.WriteAllText("mnt/System/Breeze/startup.breeze", StartupService);

        File.WriteAllText("mnt/System/Breeze/main.breeze", StarterProgram);
        File.WriteAllText("mnt/System/Breeze/About Windose.breeze", AboutControlPanelApplet);

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
        Directory.CreateDirectory("/mnt/System");
        Directory.CreateDirectory("/mnt/System/Services");
        Directory.CreateDirectory("/mnt/System/Breeze/");
        Directory.CreateDirectory("/mnt/user");
        Directory.CreateDirectory("/mnt/user/Desktop");
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
