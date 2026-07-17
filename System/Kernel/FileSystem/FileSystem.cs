using System.Text;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Vfs;
using Cosmos.Kernel.System.Vfs;

public sealed class FileSystem : IWindoseFileSystem
{
    private const string DriveRoot = "C:\\";
    private const string NativeRoot = "/mnt/disk0";

    private readonly object sync = new object();

    public event Action<FileSystemChange> Changed;

    public VfsManager.VfsMount? mount;

    public FileSystem()
    {
    }

    // ---- path translation ----

    private static string ToNative(string windosePath)
    {
        if (!windosePath.StartsWith(DriveRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Path must be rooted at C:\\", nameof(windosePath));

        string rel = windosePath.Substring(DriveRoot.Length).Replace('\\', '/');
        return rel.Length == 0 ? NativeRoot : NativeRoot + "/" + rel;
    }

    private static string ToWindose(string nativePath)
    {
        string rel = nativePath.Substring(NativeRoot.Length).TrimStart('/');
        return rel.Length == 0 ? DriveRoot : DriveRoot + rel.Replace('/', '\\');
    }

    // ---- basic queries ----

    public bool FileExists(string path)
    {
        lock (sync) return File.Exists(ToNative(FileSystemManager.NormalizePath(path)));
    }

    public bool DirectoryExists(string path)
    {
        lock (sync) return VfsManager.TryOpenDirectory(path, out IVfsDirectoryHandle? directory);

    }

    // ---- create / delete ----

    public bool CreateDirectory(string path)
    {

        lock (sync)
        {
            if (!VfsManager.TryCreateDirectory(path, ModeEnum.Directory)) return false;
        }
        RaiseChanged(FileSystemChangeType.Created, path);
        return true;
    }

    public bool DeleteFile(string path)
    {
        string value = FileSystemManager.NormalizePath(path);
        string native = ToNative(value);
        lock (sync)
        {
            if (!File.Exists(native)) return false;
            try
            {
                File.Delete(native);
            }
            catch
            {
                return false;
            }
        }
        RaiseChanged(FileSystemChangeType.Deleted, value);
        return true;
    }

    public bool DeleteDirectory(string path, bool recursive = false)
    {
        string value = FileSystemManager.NormalizePath(path);
        if (IsRoot(value)) return false;
        string native = ToNative(value);

        lock (sync)
        {
            if (!Directory.Exists(native)) return false;
            bool hasChildren = Directory.EnumerateFileSystemEntries(native).Any();
            if (hasChildren && !recursive) return false;
            try
            {
                Directory.Delete(native, recursive);
            }
            catch
            {
                return false;
            }
        }
        RaiseChanged(FileSystemChangeType.Deleted, value);
        return true;
    }

    // ---- copy ----

    public bool CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
    {
        string source = FileSystemManager.NormalizePath(sourcePath);
        string destination = FileSystemManager.NormalizePath(destinationPath);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) return false;

        string nativeSrc = ToNative(source);
        string nativeDst = ToNative(destination);

        lock (sync)
        {
            if (!File.Exists(nativeSrc)) return false;
            if (Directory.Exists(nativeDst)) return false;
            if (File.Exists(nativeDst) && !overwrite) return false;

            string parent = Path.GetDirectoryName(nativeDst);
            if (string.IsNullOrEmpty(parent)) return false;
            if (File.Exists(parent)) return false;
            Directory.CreateDirectory(parent);

            try
            {
                File.Copy(nativeSrc, nativeDst, overwrite);
            }
            catch
            {
                return false;
            }
        }
        RaiseChanged(FileSystemChangeType.Created, destination);
        return true;
    }

    public bool CopyDirectory(string sourcePath, string destinationPath, bool overwrite = false)
    {
        string source = FileSystemManager.NormalizePath(sourcePath);
        string destination = FileSystemManager.NormalizePath(destinationPath);
        if (IsRoot(source) ||
            string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) ||
            IsWithin(destination, source) || IsWithin(source, destination)) return false;

        string nativeSrc = ToNative(source);
        string nativeDst = ToNative(destination);

        lock (sync)
        {
            if (!Directory.Exists(nativeSrc) || File.Exists(nativeDst)) return false;
            if (Directory.Exists(nativeDst))
            {
                if (!overwrite || IsRoot(destination)) return false;
                Directory.Delete(nativeDst, true);
            }

            string parent = Path.GetDirectoryName(nativeDst);
            if (string.IsNullOrEmpty(parent) || File.Exists(parent)) return false;
            Directory.CreateDirectory(parent);

            try
            {
                CopyDirectoryRecursive(nativeSrc, nativeDst);
            }
            catch
            {
                return false;
            }
        }
        RaiseChanged(FileSystemChangeType.Created, destination);
        return true;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string dir in Directory.GetDirectories(sourceDir))
            CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        foreach (string file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
    }

    // ---- move / rename ----

    public bool MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
    {
        string source = FileSystemManager.NormalizePath(sourcePath);
        string destination = FileSystemManager.NormalizePath(destinationPath);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) return true;

        string nativeSrc = ToNative(source);
        string nativeDst = ToNative(destination);

        lock (sync)
        {
            if (!File.Exists(nativeSrc)) return false;
            if (Directory.Exists(nativeDst)) return false;
            if (File.Exists(nativeDst))
            {
                if (!overwrite) return false;
                File.Delete(nativeDst);
            }

            string parent = Path.GetDirectoryName(nativeDst);
            if (string.IsNullOrEmpty(parent) || File.Exists(parent)) return false;
            Directory.CreateDirectory(parent);

            try
            {
                File.Move(nativeSrc, nativeDst);
            }
            catch
            {
                return false;
            }
        }
        RaiseChanged(FileSystemChangeType.Moved, destination, source);
        return true;
    }

    public bool MoveDirectory(string sourcePath, string destinationPath, bool overwrite = false)
    {
        string source = FileSystemManager.NormalizePath(sourcePath);
        string destination = FileSystemManager.NormalizePath(destinationPath);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) return true;
        if (IsRoot(source) || IsWithin(destination, source) || IsWithin(source, destination)) return false;

        string nativeSrc = ToNative(source);
        string nativeDst = ToNative(destination);

        lock (sync)
        {
            if (!Directory.Exists(nativeSrc) || File.Exists(nativeDst)) return false;
            if (Directory.Exists(nativeDst))
            {
                if (!overwrite || IsRoot(destination)) return false;
                Directory.Delete(nativeDst, true);
            }

            string parent = Path.GetDirectoryName(nativeDst);
            if (string.IsNullOrEmpty(parent) || File.Exists(parent)) return false;
            Directory.CreateDirectory(parent);

            try
            {
                Directory.Move(nativeSrc, nativeDst);
            }
            catch
            {
                return false;
            }
        }
        RaiseChanged(FileSystemChangeType.Moved, destination, source);
        return true;
    }

    public bool Rename(string path, string newName, bool overwrite = false)
    {
        if (!IsValidName(newName)) return false;
        string source = FileSystemManager.NormalizePath(path);
        string destination = FileSystemManager.Combine(FileSystemManager.GetParent(source), newName);
        if (FileExists(source)) return MoveFile(source, destination, overwrite);
        if (DirectoryExists(source)) return MoveDirectory(source, destination, overwrite);
        return false;
    }

    // ---- read / write ----

    public bool TryReadAllText(string path, out string content)
    {
        string native = ToNative(FileSystemManager.NormalizePath(path));
        lock (sync)
        {
            if (!File.Exists(native))
            {
                content = "";
                return false;
            }
            try
            {
                content = File.ReadAllText(native, Encoding.UTF8);
                return true;
            }
            catch
            {
                content = "";
                return false;
            }
        }
    }

    public bool WriteAllText(string path, string content, bool overwrite = true)
    {
        string value = FileSystemManager.NormalizePath(path);
        string native = ToNative(value);
        bool existed;

        lock (sync)
        {
            if (Directory.Exists(native)) return false;
            existed = File.Exists(native);
            if (existed && !overwrite) return false;

            string parent = Path.GetDirectoryName(native);
            if (string.IsNullOrEmpty(parent) || File.Exists(parent)) return false;
            Directory.CreateDirectory(parent);

            try
            {
                File.WriteAllText(native, content ?? "", Encoding.UTF8);
            }
            catch
            {
                return false;
            }
        }
        RaiseChanged(existed ? FileSystemChangeType.Modified : FileSystemChangeType.Created, value);
        return true;
    }

    // ---- enumeration ----

    public string[] GetFiles(string path)
    {
        string native = ToNative(FileSystemManager.NormalizePath(path));
        lock (sync)
        {
            if (!Directory.Exists(native)) return Array.Empty<string>();
            string[] entries = Directory.GetFiles(native);
            string[] result = new string[entries.Length];
            for (int i = 0; i < entries.Length; i++)
                result[i] = ToWindose(entries[i].Replace('\\', '/'));
            Array.Sort(result, StringComparer.OrdinalIgnoreCase);
            return result;
        }
    }

    public string[] GetDirectories(string path)
    {
        string native = ToNative(FileSystemManager.NormalizePath(path));
        lock (sync)
        {
            if (!Directory.Exists(native)) return Array.Empty<string>();
            string[] entries = Directory.GetDirectories(native);
            string[] result = new string[entries.Length];
            for (int i = 0; i < entries.Length; i++)
                result[i] = ToWindose(entries[i].Replace('\\', '/'));
            Array.Sort(result, StringComparer.OrdinalIgnoreCase);
            return result;
        }
    }

    public long GetFileSize(string path)
    {
        string native = ToNative(FileSystemManager.NormalizePath(path));
        lock (sync)
        {
            if (!File.Exists(native)) return 0;
            try
            {
                return new FileInfo(native).Length;
            }
            catch
            {
                return 0;
            }
        }
    }

    public bool TryGetInfo(string path, out WindoseFileInfo info)
    {
        string value = FileSystemManager.NormalizePath(path);
        string native = ToNative(value);
        lock (sync)
        {
            if (File.Exists(native))
            {
                FileInfo fi = new FileInfo(native);
                info = new WindoseFileInfo
                {
                    Name = FileSystemManager.GetName(value),
                    FullPath = value,
                    IsDirectory = false,
                    Size = fi.Length,
                    ChildCount = 0,
                    CreatedAt = fi.CreationTimeUtc,
                    ModifiedAt = fi.LastWriteTimeUtc,
                };
                return true;
            }
            if (Directory.Exists(native))
            {
                DirectoryInfo di = new DirectoryInfo(native);
                info = new WindoseFileInfo
                {
                    Name = FileSystemManager.GetName(value),
                    FullPath = value,
                    IsDirectory = true,
                    Size = 0,
                    ChildCount = di.EnumerateFileSystemInfos().Count(),
                    CreatedAt = di.CreationTimeUtc,
                    ModifiedAt = di.LastWriteTimeUtc,
                };
                return true;
            }
            info = new WindoseFileInfo();
            return false;
        }
    }

    // ---- helpers ----

    private void RaiseChanged(FileSystemChangeType type, string path, string previousPath = "")
    {
        try
        {
            Changed?.Invoke(new FileSystemChange(type, path, previousPath));
        }
        catch
        {
        }
    }

    private static bool IsRoot(string path)
    {
        return path.Length == 3 && path[1] == ':' && path[2] == '\\';
    }

    private static bool IsWithin(string path, string directory)
    {
        if (path.Length <= directory.Length) return false;
        return path.StartsWith(directory + (directory.EndsWith("\\") ? "" : "\\"), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == "." || name == "..") return false;
        return name.IndexOf('\\') < 0 && name.IndexOf('/') < 0 && name.IndexOf(':') < 0;
    }
}