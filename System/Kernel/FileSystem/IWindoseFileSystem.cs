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
