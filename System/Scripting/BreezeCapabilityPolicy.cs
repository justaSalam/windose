public static class BreezeCapabilityPolicy
{
    private static readonly Dictionary<string, HashSet<string>> grants =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    private static readonly object sync = new object();

    public static Func<string, string, bool> Authorize;

    public static bool IsAllowed(string executablePath, string capability)
    {
        string path = FileSystemManager.NormalizePath(executablePath);
        if (path.StartsWith(@"0:\System\Services\", StringComparison.OrdinalIgnoreCase)) return true;

        lock (sync)
        {
            if (grants.TryGetValue(path, out HashSet<string> capabilities) && capabilities.Contains(capability))
                return true;
        }

        if (Authorize != null)
        {
            try { return Authorize(path, capability); }
            catch { return false; }
        }

        return capability == "ui" || capability == "filesystem.read" || capability == "filesystem.write" || capability == "ipc" ||
            capability == "logging" || capability == "process.inspect" || capability == "registry.read" ||
            capability == "registry.custom.write";
    }

    public static void Grant(string executablePath, string capability)
    {
        string path = FileSystemManager.NormalizePath(executablePath);
        lock (sync)
        {
            if (!grants.TryGetValue(path, out HashSet<string> capabilities))
            {
                capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                grants[path] = capabilities;
            }
            capabilities.Add(capability);
        }
    }

    public static void Revoke(string executablePath, string capability)
    {
        string path = FileSystemManager.NormalizePath(executablePath);
        lock (sync)
            if (grants.TryGetValue(path, out HashSet<string> capabilities)) capabilities.Remove(capability);
    }
}
