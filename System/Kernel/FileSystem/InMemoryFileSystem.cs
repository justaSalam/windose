using System.Text;

public sealed class InMemoryFileSystem : IWindoseFileSystem
{
    private sealed class MemoryFile
    {
        public string Content;
        public DateTime CreatedAt;
        public DateTime ModifiedAt;

        public MemoryFile(string content, DateTime createdAt, DateTime modifiedAt)
        {
            Content = content;
            CreatedAt = createdAt;
            ModifiedAt = modifiedAt;
        }

        public MemoryFile Clone()
        {
            return new MemoryFile(Content, CreatedAt, ModifiedAt);
        }
    }

    private sealed class MemoryDirectory
    {
        public DateTime CreatedAt;
        public DateTime ModifiedAt;

        public MemoryDirectory(DateTime createdAt, DateTime modifiedAt)
        {
            CreatedAt = createdAt;
            ModifiedAt = modifiedAt;
        }

        public MemoryDirectory Clone()
        {
            return new MemoryDirectory(CreatedAt, ModifiedAt);
        }
    }

    private readonly Dictionary<string, MemoryFile> files =
        new Dictionary<string, MemoryFile>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MemoryDirectory> directories =
        new Dictionary<string, MemoryDirectory>(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new object();

    public event Action<FileSystemChange> Changed;

    public InMemoryFileSystem()
    {
        DateTime now = DateTime.UtcNow;
        directories.Add(@"0:\", new MemoryDirectory(now, now));
    }

    public bool FileExists(string path)
    {
        lock (sync) return files.ContainsKey(FileSystemManager.NormalizePath(path));
    }

    public bool DirectoryExists(string path)
    {
        lock (sync) return directories.ContainsKey(FileSystemManager.NormalizePath(path));
    }

    public bool CreateDirectory(string path)
    {
        string value = FileSystemManager.NormalizePath(path);
        bool created;
        lock (sync)
        {
            if (files.ContainsKey(value)) return false;
            created = EnsureDirectoryNoLock(value);
            if (!directories.ContainsKey(value)) return false;
        }
        if (created) RaiseChanged(FileSystemChangeType.Created, value);
        return true;
    }

    public bool DeleteFile(string path)
    {
        string value = FileSystemManager.NormalizePath(path);
        lock (sync)
        {
            if (!files.Remove(value)) return false;
            TouchDirectoryNoLock(FileSystemManager.GetParent(value));
        }
        RaiseChanged(FileSystemChangeType.Deleted, value);
        return true;
    }

    public bool DeleteDirectory(string path, bool recursive = false)
    {
        string value = FileSystemManager.NormalizePath(path);
        if (IsRoot(value)) return false;

        lock (sync)
        {
            if (!directories.ContainsKey(value)) return false;
            bool hasChildren = HasDescendantsNoLock(value);
            if (hasChildren && !recursive) return false;

            RemoveDirectoryTreeNoLock(value);
            TouchDirectoryNoLock(FileSystemManager.GetParent(value));
        }
        RaiseChanged(FileSystemChangeType.Deleted, value);
        return true;
    }

    public bool CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
    {
        string source = FileSystemManager.NormalizePath(sourcePath);
        string destination = FileSystemManager.NormalizePath(destinationPath);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) return false;

        lock (sync)
        {
            if (!files.TryGetValue(source, out MemoryFile sourceFile)) return false;
            if (directories.ContainsKey(destination)) return false;
            if (files.ContainsKey(destination) && !overwrite) return false;

            string parent = FileSystemManager.GetParent(destination);
            if (parent == "" || files.ContainsKey(parent)) return false;
            EnsureDirectoryNoLock(parent);
            if (!directories.ContainsKey(parent)) return false;
            DateTime now = DateTime.UtcNow;
            files[destination] = new MemoryFile(sourceFile.Content, now, now);
            TouchDirectoryNoLock(parent);
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

        lock (sync)
        {
            if (!directories.ContainsKey(source) || files.ContainsKey(destination)) return false;
            if (directories.ContainsKey(destination))
            {
                if (!overwrite || IsRoot(destination)) return false;
                RemoveDirectoryTreeNoLock(destination);
            }

            string parent = FileSystemManager.GetParent(destination);
            if (parent == "" || files.ContainsKey(parent)) return false;
            EnsureDirectoryNoLock(parent);
            if (!directories.ContainsKey(parent)) return false;

            List<string> sourceDirectories = GetDirectoryTreeNoLock(source);
            List<string> sourceFiles = GetFileTreeNoLock(source);
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < sourceDirectories.Count; i++)
            {
                string target = ReplaceRoot(sourceDirectories[i], source, destination);
                directories[target] = new MemoryDirectory(now, now);
            }
            for (int i = 0; i < sourceFiles.Count; i++)
            {
                string target = ReplaceRoot(sourceFiles[i], source, destination);
                MemoryFile file = files[sourceFiles[i]];
                files[target] = new MemoryFile(file.Content, now, now);
            }
            TouchDirectoryNoLock(parent);
        }
        RaiseChanged(FileSystemChangeType.Created, destination);
        return true;
    }

    public bool MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
    {
        string source = FileSystemManager.NormalizePath(sourcePath);
        string destination = FileSystemManager.NormalizePath(destinationPath);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) return true;

        lock (sync)
        {
            if (!files.TryGetValue(source, out MemoryFile sourceFile)) return false;
            if (directories.ContainsKey(destination)) return false;
            if (files.ContainsKey(destination) && !overwrite) return false;

            string parent = FileSystemManager.GetParent(destination);
            if (parent == "" || files.ContainsKey(parent)) return false;
            EnsureDirectoryNoLock(parent);
            if (!directories.ContainsKey(parent)) return false;
            files.Remove(destination);
            files.Remove(source);
            sourceFile.ModifiedAt = DateTime.UtcNow;
            files[destination] = sourceFile;
            TouchDirectoryNoLock(FileSystemManager.GetParent(source));
            TouchDirectoryNoLock(parent);
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

        lock (sync)
        {
            if (!directories.ContainsKey(source) || files.ContainsKey(destination)) return false;
            if (directories.ContainsKey(destination))
            {
                if (!overwrite || IsRoot(destination)) return false;
                RemoveDirectoryTreeNoLock(destination);
            }

            string parent = FileSystemManager.GetParent(destination);
            if (parent == "" || files.ContainsKey(parent)) return false;
            EnsureDirectoryNoLock(parent);
            if (!directories.ContainsKey(parent)) return false;

            List<string> sourceDirectories = GetDirectoryTreeNoLock(source);
            List<string> sourceFiles = GetFileTreeNoLock(source);
            Dictionary<string, MemoryDirectory> movedDirectories = new Dictionary<string, MemoryDirectory>();
            Dictionary<string, MemoryFile> movedFiles = new Dictionary<string, MemoryFile>();
            for (int i = 0; i < sourceDirectories.Count; i++)
                movedDirectories[ReplaceRoot(sourceDirectories[i], source, destination)] = directories[sourceDirectories[i]];
            for (int i = 0; i < sourceFiles.Count; i++)
                movedFiles[ReplaceRoot(sourceFiles[i], source, destination)] = files[sourceFiles[i]];

            RemoveDirectoryTreeNoLock(source);
            foreach (KeyValuePair<string, MemoryDirectory> item in movedDirectories)
                directories[item.Key] = item.Value;
            foreach (KeyValuePair<string, MemoryFile> item in movedFiles)
                files[item.Key] = item.Value;
            directories[destination].ModifiedAt = DateTime.UtcNow;
            TouchDirectoryNoLock(FileSystemManager.GetParent(source));
            TouchDirectoryNoLock(parent);
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

    public bool TryReadAllText(string path, out string content)
    {
        lock (sync)
        {
            if (files.TryGetValue(FileSystemManager.NormalizePath(path), out MemoryFile file))
            {
                content = file.Content;
                return true;
            }
            content = "";
            return false;
        }
    }

    public bool WriteAllText(string path, string content, bool overwrite = true)
    {
        string value = FileSystemManager.NormalizePath(path);
        bool existed;
        lock (sync)
        {
            if (directories.ContainsKey(value)) return false;
            existed = files.TryGetValue(value, out MemoryFile file);
            if (existed && !overwrite) return false;

            string parent = FileSystemManager.GetParent(value);
            if (parent == "" || files.ContainsKey(parent)) return false;
            EnsureDirectoryNoLock(parent);
            if (!directories.ContainsKey(parent)) return false;
            DateTime now = DateTime.UtcNow;
            if (existed)
            {
                file.Content = content ?? "";
                file.ModifiedAt = now;
            }
            else
            {
                files[value] = new MemoryFile(content ?? "", now, now);
            }
            TouchDirectoryNoLock(parent);
        }
        RaiseChanged(existed ? FileSystemChangeType.Modified : FileSystemChangeType.Created, value);
        return true;
    }

    public string[] GetFiles(string path)
    {
        string directory = FileSystemManager.NormalizePath(path);
        lock (sync)
        {
            List<string> result = new List<string>();
            foreach (string file in files.Keys)
                if (string.Equals(FileSystemManager.GetParent(file), directory, StringComparison.OrdinalIgnoreCase))
                    result.Add(file);
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result.ToArray();
        }
    }

    public string[] GetDirectories(string path)
    {
        string directory = FileSystemManager.NormalizePath(path);
        lock (sync)
        {
            List<string> result = new List<string>();
            foreach (string candidate in directories.Keys)
            {
                if (string.Equals(candidate, directory, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(FileSystemManager.GetParent(candidate), directory, StringComparison.OrdinalIgnoreCase))
                    result.Add(candidate);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result.ToArray();
        }
    }

    public long GetFileSize(string path)
    {
        lock (sync)
        {
            if (!files.TryGetValue(FileSystemManager.NormalizePath(path), out MemoryFile file)) return 0;
            return Encoding.UTF8.GetByteCount(file.Content ?? "");
        }
    }

    public bool TryGetInfo(string path, out WindoseFileInfo info)
    {
        string value = FileSystemManager.NormalizePath(path);
        lock (sync)
        {
            if (files.TryGetValue(value, out MemoryFile file))
            {
                info = new WindoseFileInfo
                {
                    Name = FileSystemManager.GetName(value),
                    FullPath = value,
                    IsDirectory = false,
                    Size = Encoding.UTF8.GetByteCount(file.Content ?? ""),
                    ChildCount = 0,
                    CreatedAt = file.CreatedAt,
                    ModifiedAt = file.ModifiedAt,
                };
                return true;
            }
            if (directories.TryGetValue(value, out MemoryDirectory directory))
            {
                info = new WindoseFileInfo
                {
                    Name = FileSystemManager.GetName(value),
                    FullPath = value,
                    IsDirectory = true,
                    Size = 0,
                    ChildCount = CountChildrenNoLock(value),
                    CreatedAt = directory.CreatedAt,
                    ModifiedAt = directory.ModifiedAt,
                };
                return true;
            }
            info = new WindoseFileInfo();
            return false;
        }
    }

    private bool EnsureDirectoryNoLock(string path)
    {
        if (path == "") return false;
        List<string> missing = new List<string>();
        string current = path;
        while (current != "" && !directories.ContainsKey(current))
        {
            if (files.ContainsKey(current)) return false;
            missing.Add(current);
            current = FileSystemManager.GetParent(current);
        }

        DateTime now = DateTime.UtcNow;
        for (int i = missing.Count - 1; i >= 0; i--)
            directories[missing[i]] = new MemoryDirectory(now, now);
        return missing.Count > 0;
    }

    private void RemoveDirectoryTreeNoLock(string root)
    {
        List<string> directoryPaths = GetDirectoryTreeNoLock(root);
        List<string> filePaths = GetFileTreeNoLock(root);
        for (int i = 0; i < filePaths.Count; i++) files.Remove(filePaths[i]);
        for (int i = directoryPaths.Count - 1; i >= 0; i--) directories.Remove(directoryPaths[i]);
    }

    private List<string> GetDirectoryTreeNoLock(string root)
    {
        List<string> result = new List<string>();
        foreach (string path in directories.Keys)
            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase) || IsWithin(path, root))
                result.Add(path);
        result.Sort((left, right) => left.Length.CompareTo(right.Length));
        return result;
    }

    private List<string> GetFileTreeNoLock(string root)
    {
        List<string> result = new List<string>();
        foreach (string path in files.Keys)
            if (IsWithin(path, root)) result.Add(path);
        return result;
    }

    private bool HasDescendantsNoLock(string root)
    {
        foreach (string path in directories.Keys)
            if (IsWithin(path, root)) return true;
        foreach (string path in files.Keys)
            if (IsWithin(path, root)) return true;
        return false;
    }

    private int CountChildrenNoLock(string path)
    {
        int count = 0;
        foreach (string candidate in directories.Keys)
            if (!string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(FileSystemManager.GetParent(candidate), path, StringComparison.OrdinalIgnoreCase)) count++;
        foreach (string candidate in files.Keys)
            if (string.Equals(FileSystemManager.GetParent(candidate), path, StringComparison.OrdinalIgnoreCase)) count++;
        return count;
    }

    private void TouchDirectoryNoLock(string path)
    {
        if (path != "" && directories.TryGetValue(path, out MemoryDirectory directory))
            directory.ModifiedAt = DateTime.UtcNow;
    }

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

    private static string ReplaceRoot(string path, string oldRoot, string newRoot)
    {
        return newRoot + path.Substring(oldRoot.Length);
    }

    private static bool IsValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == "." || name == "..") return false;
        return name.IndexOf('\\') < 0 && name.IndexOf('/') < 0 && name.IndexOf(':') < 0;
    }
}
