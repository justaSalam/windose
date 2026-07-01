using System.Globalization;
using System.Text;

public sealed class RegistryEntry
{
    public string Key { get; internal set; }
    public object Value { get; internal set; }
    public object DefaultValue { get; internal set; }
    public string Description { get; internal set; }
    public bool RequiresRestart { get; internal set; }
    public bool IsBuiltIn { get; internal set; }
    internal bool IsPersistent { get; set; }
}

public readonly struct RegistryChange
{
    public readonly string Key;
    public readonly object OldValue;
    public readonly object NewValue;
    public readonly bool Deleted;

    public RegistryChange(string key, object oldValue, object newValue, bool deleted)
    {
        Key = key;
        OldValue = oldValue;
        NewValue = newValue;
        Deleted = deleted;
    }
}

public static class SystemRegistry
{
    public const string StoragePath = @"0:\System\registry.db";

    private static readonly Dictionary<string, RegistryEntry> entries =
        new Dictionary<string, RegistryEntry>(StringComparer.OrdinalIgnoreCase);
    private static readonly object sync = new object();
    private static bool initialized;
    private static bool restartRequired;

    public static event Action<RegistryChange> Changed;
    public static bool RestartRequired { get { lock (sync) return restartRequired; } }

    public static void Initialize()
    {
        lock (sync)
        {
            if (initialized) return;
            initialized = true;
            DefineNoLock("System/Theme/Name", "classic", "Current UI theme: classic or modern.", false, true);
            DefineNoLock("System/Desktop/BackgroundColor", "theme", "Desktop background color in #RRGGBB format, or theme to use the active theme background.", false, true);
            DefineNoLock("System/Desktop/Wallpaper", "", "Wallpaper path. Reserved until bitmap-backed storage is available.", false, true);
            DefineNoLock("System/Desktop/WallpaperMode", "center", "Wallpaper layout: center, tile, or stretch.", false, true);
            DefineNoLock("System/Display/Width", 1920L, "Requested boot framebuffer width.", true, true);
            DefineNoLock("System/Display/Height", 1080L, "Requested boot framebuffer height.", true, true);
            DefineNoLock("System/Display/BitsPerPixel", 32L, "Requested boot framebuffer color depth.", true, true);
        }

        Load();
    }

    public static void Define(string key, object defaultValue, string description = "", bool requiresRestart = false)
    {
        key = NormalizeKey(key);
        lock (sync) DefineNoLock(key, NormalizeValue(defaultValue), description, requiresRestart, false);
    }

    public static bool Set(string key, object value, bool persist = true)
    {
        return SetCore(NormalizeKey(key), NormalizeValue(value), persist, true, true, true);
    }

    public static bool SetRuntimeValue(string key, object value)
    {
        return SetCore(NormalizeKey(key), NormalizeValue(value), false, false, false, false);
    }

    public static object Get(string key, object fallback = null)
    {
        key = NormalizeKey(key);
        lock (sync) return entries.TryGetValue(key, out RegistryEntry entry) ? entry.Value : fallback;
    }

    public static string GetString(string key, string fallback = "")
    {
        object value = Get(key, fallback);
        return value?.ToString() ?? fallback;
    }

    public static long GetInteger(string key, long fallback = 0)
    {
        object value = Get(key, fallback);
        if (value is long integer) return integer;
        if (value is double number) return (long)number;
        return long.TryParse(value?.ToString(), out long parsed) ? parsed : fallback;
    }

    public static bool GetBoolean(string key, bool fallback = false)
    {
        object value = Get(key, fallback);
        if (value is bool boolean) return boolean;
        return bool.TryParse(value?.ToString(), out bool parsed) ? parsed : fallback;
    }

    public static RegistryEntry GetEntry(string key)
    {
        key = NormalizeKey(key);
        lock (sync)
        {
            if (!entries.TryGetValue(key, out RegistryEntry entry)) return null;
            return CopyEntry(entry);
        }
    }

    public static bool Exists(string key)
    {
        lock (sync) return entries.ContainsKey(NormalizeKey(key));
    }

    public static bool Delete(string key, bool persist = true)
    {
        key = NormalizeKey(key);
        object oldValue;
        lock (sync)
        {
            if (!entries.TryGetValue(key, out RegistryEntry entry)) return false;
            oldValue = entry.Value;
            entries.Remove(key);
            if (entry.RequiresRestart) restartRequired = true;
        }

        if (persist) Save();
        Changed?.Invoke(new RegistryChange(key, oldValue, null, true));
        return true;
    }

    public static List<string> GetKeys(string prefix = "")
    {
        prefix = NormalizeKey(prefix);
        List<string> result = new List<string>();
        lock (sync)
        {
            foreach (string key in entries.Keys)
                if (prefix == "" || key.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) result.Add(key);
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    public static void ClearRestartRequired()
    {
        lock (sync) restartRequired = false;
    }

    public static bool Save()
    {
        IWindoseFileSystem fileSystem = FileSystemManager.Current;
        if (fileSystem == null) return false;

        StringBuilder output = new StringBuilder();
        lock (sync)
        {
            foreach (RegistryEntry entry in entries.Values)
            {
                if (!entry.IsPersistent) continue;
                if (!TrySerialize(entry.Value, out string type, out string value)) continue;
                if (!TrySerialize(entry.DefaultValue, out string defaultType, out string defaultValue)) continue;
                output.Append(type).Append('|')
                    .Append(Encode(entry.Key)).Append('|')
                    .Append(Encode(value)).Append('|')
                    .Append(defaultType).Append('|')
                    .Append(Encode(defaultValue)).Append('|')
                    .Append(Encode(entry.Description)).Append('|')
                    .Append(entry.RequiresRestart ? "true" : "false").Append('\n');
            }
        }

        string directory = FileSystemManager.GetParent(StoragePath);
        if (!fileSystem.DirectoryExists(directory)) fileSystem.CreateDirectory(directory);
        return fileSystem.WriteAllText(StoragePath, output.ToString());
    }

    public static void Load()
    {
        IWindoseFileSystem fileSystem = FileSystemManager.Current;
        if (fileSystem == null || !fileSystem.TryReadAllText(StoragePath, out string content)) return;

        string[] lines = content.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string[] parts = lines[i].Split('|');
            if (parts.Length != 3 && parts.Length != 7) continue;
            try
            {
                string key = NormalizeKey(Decode(parts[1]));
                string serialized = Decode(parts[2]);
                if (!TryDeserialize(parts[0], serialized, out object value)) continue;
                SetCore(key, value, false, false, false, true);
                if (parts.Length == 7)
                {
                    TryDeserialize(parts[3], Decode(parts[4]), out object defaultValue);
                    lock (sync)
                    {
                        RegistryEntry entry = entries[key];
                        if (!entry.IsBuiltIn)
                        {
                            entry.DefaultValue = defaultValue;
                            entry.Description = Decode(parts[5]);
                            entry.RequiresRestart = bool.TryParse(parts[6], out bool requiresRestart) && requiresRestart;
                        }
                    }
                }
            }
            catch { }
        }
        ClearRestartRequired();
    }

    public static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";
        string normalized = key.Trim().Replace('\\', '/');
        while (normalized.Contains("//")) normalized = normalized.Replace("//", "/");
        return normalized.Trim('/');
    }

    private static bool SetCore(string key, object value, bool persist, bool notify, bool markRestart, bool persistentEntry)
    {
        if (key == "") return false;
        object oldValue = null;
        bool changed;
        lock (sync)
        {
            if (entries.TryGetValue(key, out RegistryEntry entry))
            {
                oldValue = entry.Value;
                changed = !ValuesEqual(oldValue, value);
                if (!changed) return true;
                entry.Value = value;
                if (markRestart && entry.RequiresRestart) restartRequired = true;
            }
            else
            {
                entries[key] = new RegistryEntry
                {
                    Key = key,
                    Value = value,
                    DefaultValue = value,
                    Description = "Custom runtime registry value.",
                    IsBuiltIn = false,
                    IsPersistent = persistentEntry,
                };
                changed = true;
            }
        }

        if (persist) Save();
        if (notify && changed) Changed?.Invoke(new RegistryChange(key, oldValue, value, false));
        return true;
    }

    private static void DefineNoLock(string key, object value, string description, bool requiresRestart, bool builtIn)
    {
        if (key == "") return;
        if (entries.TryGetValue(key, out RegistryEntry existing))
        {
            if (!existing.IsBuiltIn)
            {
                existing.DefaultValue = value;
                existing.Description = description ?? "";
                existing.RequiresRestart = requiresRestart;
            }
            return;
        }
        entries[key] = new RegistryEntry
        {
            Key = key,
            Value = value,
            DefaultValue = value,
            Description = description ?? "",
            RequiresRestart = requiresRestart,
            IsBuiltIn = builtIn,
            IsPersistent = true,
        };
    }

    private static RegistryEntry CopyEntry(RegistryEntry entry)
    {
        return new RegistryEntry
        {
            Key = entry.Key,
            Value = entry.Value,
            DefaultValue = entry.DefaultValue,
            Description = entry.Description,
            RequiresRestart = entry.RequiresRestart,
            IsBuiltIn = entry.IsBuiltIn,
            IsPersistent = entry.IsPersistent,
        };
    }

    private static object NormalizeValue(object value)
    {
        if (value == null || value is string || value is bool || value is long || value is double) return value;
        if (value is int integer) return (long)integer;
        if (value is float single) return (double)single;
        return value.ToString();
    }

    private static bool ValuesEqual(object first, object second)
    {
        if (first == null || second == null) return first == second;
        return first.Equals(second) || first.ToString() == second.ToString();
    }

    private static bool TrySerialize(object value, out string type, out string serialized)
    {
        if (value == null) { type = "null"; serialized = ""; return true; }
        if (value is bool boolean) { type = "bool"; serialized = boolean ? "true" : "false"; return true; }
        if (value is long integer) { type = "int"; serialized = integer.ToString(CultureInfo.InvariantCulture); return true; }
        if (value is double number) { type = "number"; serialized = number.ToString(CultureInfo.InvariantCulture); return true; }
        type = "string";
        serialized = value.ToString();
        return true;
    }

    private static bool TryDeserialize(string type, string serialized, out object value)
    {
        switch (type)
        {
            case "null": value = null; return true;
            case "bool": if (bool.TryParse(serialized, out bool boolean)) { value = boolean; return true; } break;
            case "int": if (long.TryParse(serialized, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer)) { value = integer; return true; } break;
            case "number": if (double.TryParse(serialized, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) { value = number; return true; } break;
            case "string": value = serialized; return true;
        }
        value = null;
        return false;
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
    private static string Decode(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));
}
