using Windose.System.System_Calls;

/// <summary>
/// Recycle bin: deleted files/folders move here with their original path recorded
/// in a sidecar file, so they can be restored or emptied later.
/// </summary>
public static class TrashManager
{
    public const string TrashDir = "/mnt/.trash";
    public const string MetaSuffix = ".origpath";

    public sealed class TrashItem
    {
        public string StoredName = "";
        public string OriginalPath = "";
        public bool IsDirectory;
    }

    public static event Action Changed;

    private static void NotifyChanged()
    {
        try { Changed?.Invoke(); } catch { }
    }

    public static void Ensure()
    {
        try
        {
            if (!global::System.IO.Directory.Exists(TrashDir))
                global::System.IO.Directory.CreateDirectory(TrashDir);
        }
        catch (Exception exception)
        {
            SystemLogger.WriteLine("Trash", "Could not create trash: " + exception.Message, ConsoleMessageType.Error);
        }
    }

    public static bool IsEmpty()
    {
        TrashItem[] items = List();
        return items.Length == 0;
    }

    public static TrashItem[] List()
    {
        Ensure();
        global::System.Collections.Generic.List<TrashItem> items =
            new global::System.Collections.Generic.List<TrashItem>();
        string[] entries;
        try { entries = global::System.IO.Directory.GetFileSystemEntries(TrashDir); }
        catch { return items.ToArray(); }
        for (int i = 0; i < entries.Length; i++)
        {
            string stored = global::System.IO.Path.GetFileName(entries[i]);
            if (stored.EndsWith(MetaSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            bool isDir = false;
            try { isDir = global::System.IO.Directory.Exists(entries[i]); } catch { continue; }
            string original = "";
            try { original = global::System.IO.File.ReadAllText(entries[i] + MetaSuffix).Trim(); }
            catch { }
            items.Add(new TrashItem { StoredName = stored, OriginalPath = original, IsDirectory = isDir });
        }
        return items.ToArray();
    }

    /// <summary>Moves a file or directory to trash. False when missing or on failure.</summary>
    public static bool MoveToTrash(string path)
    {
        Ensure();
        try
        {
            bool isDir = global::System.IO.Directory.Exists(path);
            if (!isDir && !global::System.IO.File.Exists(path)) return false;
            string stored = UniqueName(global::System.IO.Path.GetFileName(path));
            string dest = TrashDir + "/" + stored;
            if (isDir) global::System.IO.Directory.Move(path, dest);
            else global::System.IO.File.Move(path, dest);
            try { global::System.IO.File.WriteAllText(dest + MetaSuffix, path); } catch { }
        }
        catch (Exception exception)
        {
            SystemLogger.WriteLine("Trash", "Could not trash " + path + ": " + exception.Message, ConsoleMessageType.Error);
            return false;
        }
        NotifyChanged();
        return true;
    }

    /// <summary>Restores a trashed item by stored name. Uniquifies when the target exists.</summary>
    public static bool Restore(string storedName)
    {
        Ensure();
        try
        {
            string src = TrashDir + "/" + storedName;
            bool isDir = global::System.IO.Directory.Exists(src);
            if (!isDir && !global::System.IO.File.Exists(src)) return false;
            string original = "";
            try { original = global::System.IO.File.ReadAllText(src + MetaSuffix).Trim(); } catch { }
            if (string.IsNullOrEmpty(original)) return false;
            if (global::System.IO.File.Exists(original) || global::System.IO.Directory.Exists(original))
                original = UniqueOutsideTrash(original);
            string parent = global::System.IO.Path.GetDirectoryName(original);
            if (!string.IsNullOrEmpty(parent))
            {
                try { global::System.IO.Directory.CreateDirectory(parent); } catch { }
            }
            if (isDir) global::System.IO.Directory.Move(src, original);
            else global::System.IO.File.Move(src, original);
            try { global::System.IO.File.Delete(src + MetaSuffix); } catch { }
        }
        catch (Exception exception)
        {
            SystemLogger.WriteLine("Trash", "Could not restore " + storedName + ": " + exception.Message, ConsoleMessageType.Error);
            return false;
        }
        NotifyChanged();
        return true;
    }

    public static void Empty()
    {
        Ensure();
        try
        {
            string[] entries = global::System.IO.Directory.GetFileSystemEntries(TrashDir);
            for (int i = 0; i < entries.Length; i++)
            {
                try
                {
                    if (global::System.IO.Directory.Exists(entries[i]))
                        global::System.IO.Directory.Delete(entries[i], true);
                    else
                        global::System.IO.File.Delete(entries[i]);
                }
                catch { }
            }
        }
        catch { }
        NotifyChanged();
    }

    private static string UniqueName(string name)
    {
        if (string.IsNullOrEmpty(name)) name = "item";
        string candidate = name;
        int n = 2;
        while (global::System.IO.File.Exists(TrashDir + "/" + candidate) ||
               global::System.IO.Directory.Exists(TrashDir + "/" + candidate))
        {
            candidate = name + " (" + n + ")";
            n++;
        }
        return candidate;
    }

    private static string UniqueOutsideTrash(string path)
    {
        if (!global::System.IO.File.Exists(path) && !global::System.IO.Directory.Exists(path))
            return path;
        int n = 2;
        while (true)
        {
            string candidate = path + " (" + n + ")";
            if (!global::System.IO.File.Exists(candidate) && !global::System.IO.Directory.Exists(candidate))
                return candidate;
            n++;
        }
    }
}
