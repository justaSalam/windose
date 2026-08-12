
public static class ProgramRegistry
{
    private static readonly Dictionary<string, Action<string?>> applications = new();

    public static void Register(string name, Action<string?> action)
    {
        applications[name] = action;
    }

    public static void Run(string name, string? argument = null)
    {
        if (applications.TryGetValue(name, out var action))
            action(argument);
    }

    public static bool Exists(string name)
    {
        return applications.ContainsKey(name);
    }

    public static void Remove(string name)
    {
        applications.Remove(name);
    }
}