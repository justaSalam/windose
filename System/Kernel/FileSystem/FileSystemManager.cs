
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Vfs;
using Cosmos.Kernel.System;
using Cosmos.Kernel.System.Filesystems.Fat;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.Vfs;
using Windose.System.System_Calls;

public static class FileSystemManager
{
    public static VfsManager.VfsMount? mount;


    public static void Initialize()
    {
        FatFilesystemType fat = new FatFilesystemType();


        VfsManager.RegisterFilesystem("fat", fat);

        if (VfsManager.TryMount("fat", "0", MountFlags.None, "/mnt", out VfsManager.VfsMount? mount))
        {
            ConsoleMessage.WriteLine("FileSystemManager", $"Mounted -> {mount.Name} partition {mount.Source} - at -> {mount.MountPoint}");
            Serial.WriteString($"Mounted -> {mount.Name} partition {mount.Source} - at -> {mount.MountPoint}\n");
        }
        else
        {
            ConsoleMessage.WriteLine("FileSystemManager", "Failed to mount filesystem", ConsoleMessageType.Error);
            Serial.WriteString($"Failed to mount filesystem\n");

            return;
        }



        //CreateSystemDirectories();
        //SystemRegistry.Initialize();

        //StorageManager.Initialize();
        //
        //VfsManager.RegisterFilesystem("fat", new FatFilesystemType());
        //
        //
        //// Scan partitions
        //if (StorageManager.PrimaryDevice != null)
        //{
        //    StorageManager.RescanPartitions(StorageManager.PrimaryDevice);
        //
        //    if (StorageManager.Partitions.Count == 0)
        //    {
        //        Mbr.Create(StorageManager.PrimaryDevice);
        //
        //        PartitionManager.Create(StorageManager.PrimaryDevice, 2048, 100000, 0x0C, Guid.Empty);
        //
        //
        //        StorageManager.RescanPartitions(StorageManager.PrimaryDevice);
        //    }
        //}
        //
        //VfsManager.TryFormat("fat", "0", null);
        //
        //
        //VfsManager.TryMount("fat", "0", MountFlags.None, "/mnt", out mount);


        //fs.WriteAllText(@"C:\Apps\main.breeze", StarterProgram);

        //fs.CreateDirectory(@"C:\System\ControlPanel");
        //fs.CreateDirectory(@"C:\System\Services");
        //fs.WriteAllText(@"C:\System\ControlPanel\About Windose.breeze", AboutControlPanelApplet);
        //fs.WriteAllText(@"C:\System\Services\startup.breeze", StartupService);
    }

    private static void CreateSystemDirectories()
    {
        Serial.WriteString("sysdir\n");
        Directory.CreateDirectory("/mnt/System");
        Directory.CreateDirectory("/mnt/System/Services");
        Directory.CreateDirectory("/mnt/User");
        Console.WriteLine("done");

    }

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return @"0:\";
        string value = path.Trim().Replace('/', '\\');
        if (value == "0:") return @"0:\";
        if (!value.Contains(":")) value = @"0:\" + value.TrimStart('\\');

        string prefix = value.Length >= 2 && value[1] == ':' ? value.Substring(0, 2) : "0:";
        string rest = value.Length > 2 ? value.Substring(2) : "";
        while (rest.Contains("\\\\")) rest = rest.Replace("\\\\", "\\");
        rest = rest.TrimStart('\\');
        value = prefix + "\\" + rest;
        if (value.Length > 3) value = value.TrimEnd('\\');
        return value;
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
let serviceFiles = getFiles(""0:\\System\\Services"");
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
