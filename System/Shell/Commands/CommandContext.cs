public sealed class CommandContext
{
    private readonly Action<string> writeLine;
    private readonly Action clear;
    private readonly Action close;
    private readonly Func<string, string> readLine;

    public string CurrentDirectory { get; set; } = "/";

    public CommandContext(Action<string> writeLine, Action clear, Action close, Func<string, string> readLine)
    {
        this.writeLine = writeLine;
        this.clear = clear;
        this.close = close;
        this.readLine = readLine;
    }

    public void WriteLine(string text = "") => writeLine?.Invoke(text ?? "");
    public void Clear() => clear?.Invoke();
    public void Close() => close?.Invoke();

    // Blocks the CALLING thread (the command's own worker thread) until the
    // user submits a line in the owning TerminalConsole. Call this only from
    // a command's worker thread — never from the thread that delivers
    // keyboard events, or it deadlocks against itself.
    public string ReadLine(string prompt) => readLine(prompt);

    public string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return CurrentDirectory;

        if (path.Contains(":"))
            return FileSystemManager.NormalizePath(path);

        if (path == ".")
            return CurrentDirectory;

        if (path == "..")
        {
            string parent = Path.GetDirectoryName(CurrentDirectory.TrimEnd('/'));
            return string.IsNullOrEmpty(parent) ? "/" : parent;
        }

        return Path.Combine(CurrentDirectory, path);
    }
}
