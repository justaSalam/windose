public static class IconRegistry
{
    private static readonly Dictionary<string, string> icons = new();

    public static void Register(string ext, string path)
    {
        icons[ext] = path;
    }

    public static string? Get(string ext)
    {
        return icons.TryGetValue(ext, out string? path)
            ? path
            : null;
    }

    public static void Remove(string ext)
    {
        icons.Remove(ext);
    }
}