public sealed class CommandContext
{
    private readonly Action<string> writeLine;
    private readonly Action clear;
    private readonly Action close;

    public string CurrentDirectory { get; set; } = "/";

    public CommandContext(Action<string> writeLine, Action clear, Action close)
    {
        this.writeLine = writeLine;
        this.clear = clear;
        this.close = close;
    }

    public void WriteLine(string text = "") => writeLine?.Invoke(text ?? "");
    public void ReadLine(string prompt, Action<string> callback)
    {
        WriteLine(prompt);
        string input = Console.ReadLine() ?? "";
        callback?.Invoke(input);
    }
    public void Clear() => clear?.Invoke();
    public void Close() => close?.Invoke();

    public string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return CurrentDirectory;

        if (path.Contains(":"))
            return FileSystemManager.NormalizePath(path);

        if (path == ".")
            return CurrentDirectory;

        if (path == "..")
            return Path.GetPathRoot(path);

        return Path.Combine(CurrentDirectory, path);
    }
}
