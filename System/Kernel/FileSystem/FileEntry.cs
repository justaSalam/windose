public struct FileEntry
{
    public string FileName;

    public FileType FileType;
    public string AbsoluteLocation;
    public long SizeBytes;

    /// <summary>
    /// Number of directories and files the current directory contains
    /// </summary>
    public string? Contains;

    public int ?directoryCount;
    public int ?fileCount;


    public string CreatedAt; //ToString("D");

    /// <summary>
    /// A directory entry struct
    /// </summary>
    /// <param name="name"></param>
    /// <param name="type"></param>
    /// <param name="location"></param>
    /// <param name="size"></param>
    /// <param name="contains"></param>
    public FileEntry(string name, FileType type, string location, long size, string contains)
    {
        FileName = name;
        FileType = type;
        AbsoluteLocation = location;
        SizeBytes = size;
        Contains = contains;
        CreatedAt = DateTime.Now.ToString("D");
    }


    /// <summary>
    /// A file entry struct
    /// </summary>
    /// <param name="name"></param>
    /// <param name="type"></param>
    /// <param name="location"></param>
    /// <param name="size"></param>
    public FileEntry(string name, FileType type, string location, long size)
    {
        FileName = name;
        FileType = type;
        AbsoluteLocation = location;
        SizeBytes = size;
        CreatedAt = DateTime.Now.ToString("D");
    }

}

public enum FileType
{
    File, Directory, Executable, Link, Unknown
}