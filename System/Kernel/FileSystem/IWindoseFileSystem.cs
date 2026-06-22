public interface IWindoseFileSystem
{
    event Action<FileSystemChange> Changed;

    bool FileExists(string path);
    bool DirectoryExists(string path);
    bool CreateDirectory(string path);
    bool DeleteFile(string path);
    bool DeleteDirectory(string path, bool recursive = false);
    bool CopyFile(string sourcePath, string destinationPath, bool overwrite = false);
    bool CopyDirectory(string sourcePath, string destinationPath, bool overwrite = false);
    bool MoveFile(string sourcePath, string destinationPath, bool overwrite = false);
    bool MoveDirectory(string sourcePath, string destinationPath, bool overwrite = false);
    bool Rename(string path, string newName, bool overwrite = false);
    bool TryReadAllText(string path, out string content);
    bool WriteAllText(string path, string content, bool overwrite = true);
    string[] GetFiles(string path);
    string[] GetDirectories(string path);
    long GetFileSize(string path);
    bool TryGetInfo(string path, out WindoseFileInfo info);
}

public enum FileSystemChangeType
{
    Created,
    Modified,
    Deleted,
    Moved,
}

public struct FileSystemChange
{
    public FileSystemChangeType Type;
    public string Path;
    public string PreviousPath;

    public FileSystemChange(FileSystemChangeType type, string path, string previousPath = "")
    {
        Type = type;
        Path = path ?? "";
        PreviousPath = previousPath ?? "";
    }
}

public struct WindoseFileInfo
{
    public string Name;
    public string FullPath;
    public bool IsDirectory;
    public long Size;
    public int ChildCount;
    public DateTime CreatedAt;
    public DateTime ModifiedAt;
}

public static class FileSystemManager
{
    public static IWindoseFileSystem Current { get; private set; }

    public static void Initialize(IWindoseFileSystem fileSystem)
    {
        Current = fileSystem;
    }

    public static void InitializeTemporary()
    {
        InMemoryFileSystem memory = new InMemoryFileSystem();
        Initialize(memory);
        memory.CreateDirectory(@"0:\Apps");
        memory.CreateDirectory(@"0:\Documents");
        memory.CreateDirectory(@"0:\System\Services");
        memory.WriteAllText(@"0:\Apps\hello.breeze", StarterProgram);
        memory.WriteAllText(@"0:\Apps\main.breeze", StarterProgram);
        memory.WriteAllText(@"0:\System\Services\startup.breeze", StartupService);
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
