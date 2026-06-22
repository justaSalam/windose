public delegate void CommandHandler(CommandContext context, string[] arguments);

public sealed class ShellCommand
{
    public string Name { get; }
    public string Description { get; }
    public string Usage { get; }
    public CommandHandler Handler { get; }

    public ShellCommand(string name, string description, string usage, CommandHandler handler)
    {
        Name = name ?? "";
        Description = description ?? "";
        Usage = usage ?? "";
        Handler = handler;
    }
}

public sealed class CommandContext
{
    private readonly Action<string> writeLine;
    private readonly Action clear;
    private readonly Action close;

    public string CurrentDirectory { get; set; } = @"0:\";

    public CommandContext(Action<string> writeLine, Action clear, Action close)
    {
        this.writeLine = writeLine;
        this.clear = clear;
        this.close = close;
    }

    public void WriteLine(string text = "") => writeLine?.Invoke(text ?? "");
    public void Clear() => clear?.Invoke();
    public void Close() => close?.Invoke();

    public string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return CurrentDirectory;
        if (path.Contains(":")) return FileSystemManager.NormalizePath(path);
        if (path == ".") return CurrentDirectory;
        if (path == "..") return FileSystemManager.GetParent(CurrentDirectory);
        return FileSystemManager.Combine(CurrentDirectory, path);
    }
}

public static class CommandRegistry
{
    private static readonly Dictionary<string, ShellCommand> commands =
        new Dictionary<string, ShellCommand>(StringComparer.OrdinalIgnoreCase);
    private static readonly List<ShellCommand> orderedCommands = new List<ShellCommand>();
    private static bool builtInsRegistered;

    public static IReadOnlyList<ShellCommand> Commands => orderedCommands;

    public static bool Register(string name, string description, string usage, CommandHandler handler)
    {
        string key = (name ?? "").Trim();
        if (key == "" || handler == null || commands.ContainsKey(key)) return false;

        ShellCommand command = new ShellCommand(key, description, usage, handler);
        commands[key] = command;
        orderedCommands.Add(command);
        return true;
    }

    public static bool RegisterAlias(string alias, string commandName)
    {
        if (!commands.TryGetValue(commandName ?? "", out ShellCommand command)) return false;
        string key = (alias ?? "").Trim();
        if (key == "" || commands.ContainsKey(key)) return false;
        commands[key] = command;
        return true;
    }

    public static void Execute(CommandContext context, string commandLine)
    {
        string[] parts = Parse(commandLine);
        if (parts.Length == 0) return;

        if (!commands.TryGetValue(parts[0], out ShellCommand command))
        {
            context.WriteLine("Bad command or file name: " + parts[0]);
            return;
        }

        string[] arguments = new string[parts.Length - 1];
        for (int i = 1; i < parts.Length; i++) arguments[i - 1] = parts[i];

        try
        {
            command.Handler(context, arguments);
        }
        catch (Exception exception)
        {
            context.WriteLine("Command failed: " + exception.Message);
        }
    }

    public static void EnsureBuiltIns()
    {
        if (builtInsRegistered) return;
        builtInsRegistered = true;

        Register("help", "Lists commands or explains one command.", "help [command]", Help);
        Register("echo", "Writes text to the console.", "echo [text]", (context, args) => context.WriteLine(Join(args)));
        Register("cls", "Clears the console.", "cls", (context, args) => context.Clear());
        RegisterAlias("clear", "cls");

        Register("pwd", "Prints the current directory.", "pwd", (context, args) => context.WriteLine(context.CurrentDirectory));
        Register("cd", "Changes the current directory.", "cd [path]", ChangeDirectory);
        Register("dir", "Lists immediate files and directories.", "dir [path]", ListDirectory);
        RegisterAlias("ls", "dir");
        Register("type", "Prints a text file.", "type <file>", TypeFile);
        RegisterAlias("cat", "type");

        Register("mkdir", "Creates a directory.", "mkdir <path>", MakeDirectory);
        Register("del", "Deletes a file.", "del <file>", DeleteFile);
        RegisterAlias("rm", "del");
        Register("rmdir", "Deletes a directory.", "rmdir <path> [/s]", DeleteDirectory);
        Register("copy", "Copies a file.", "copy <source> <destination> [/y]", CopyFile);
        Register("move", "Moves a file.", "move <source> <destination> [/y]", MoveFile);
        Register("ps", "Lists running processes.", "ps", ListProcesses);
        RegisterAlias("processes", "ps");

        Register("run", "Runs a Breeze application file.", "run <file.breeze>", RunBreeze);
        Register("service", "Starts a Breeze file in background mode.", "service <file.breeze>", RunService);

        Register("exit", "Closes Command Prompt.", "exit", (context, args) => context.Close());

    }

    private static void Help(CommandContext context, string[] args)
    {
        if (args.Length > 0)
        {
            if (!commands.TryGetValue(args[0], out ShellCommand command))
            {
                context.WriteLine("Unknown command: " + args[0]);
                return;
            }
            context.WriteLine(command.Name + " - " + command.Description);
            context.WriteLine("Usage: " + command.Usage);
            return;
        }

        context.WriteLine("Available commands:");
        for (int i = 0; i < orderedCommands.Count; i++)
            context.WriteLine("  " + orderedCommands[i].Name.PadRight(10) + orderedCommands[i].Description);
        context.WriteLine("Use help <command> for usage.");
    }

    private static void ChangeDirectory(CommandContext context, string[] args)
    {
        if (args.Length == 0) { context.WriteLine(context.CurrentDirectory); return; }
        string path = context.ResolvePath(args[0]);
        if (path == "") path = @"0:\";
        if (FileSystemManager.Current == null || !FileSystemManager.Current.DirectoryExists(path))
        {
            context.WriteLine("Directory not found: " + path);
            return;
        }
        context.CurrentDirectory = path;
    }

    private static void ListDirectory(CommandContext context, string[] args)
    {
        string path = context.ResolvePath(args.Length == 0 ? "" : args[0]);
        IWindoseFileSystem fileSystem = FileSystemManager.Current;
        if (fileSystem == null || !fileSystem.DirectoryExists(path))
        {
            context.WriteLine("Directory not found: " + path);
            return;
        }

        context.WriteLine("Directory of " + path);
        string[] directories = fileSystem.GetDirectories(path);
        string[] files = fileSystem.GetFiles(path);
        for (int i = 0; i < directories.Length; i++)
            context.WriteLine("<DIR>          " + FileSystemManager.GetName(directories[i]));
        for (int i = 0; i < files.Length; i++)
            context.WriteLine(fileSystem.GetFileSize(files[i]).ToString().PadLeft(12) + "   " + FileSystemManager.GetName(files[i]));
        context.WriteLine("        " + files.Length + " file(s), " + directories.Length + " dir(s)");
    }

    private static void TypeFile(CommandContext context, string[] args)
    {
        if (!RequireArguments(context, args, 1, "type <file>")) return;
        string path = context.ResolvePath(args[0]);
        if (FileSystemManager.Current != null && FileSystemManager.Current.TryReadAllText(path, out string content))
            context.WriteLine(content);
        else context.WriteLine("File not found: " + path);
    }

    private static void MakeDirectory(CommandContext context, string[] args)
    {
        if (!RequireArguments(context, args, 1, "mkdir <path>")) return;
        string path = context.ResolvePath(args[0]);
        context.WriteLine(FileSystemManager.Current != null && FileSystemManager.Current.CreateDirectory(path)
            ? "Directory created: " + path : "Could not create directory: " + path);
    }

    private static void DeleteFile(CommandContext context, string[] args)
    {
        if (!RequireArguments(context, args, 1, "del <file>")) return;
        string path = context.ResolvePath(args[0]);
        context.WriteLine(FileSystemManager.Current != null && FileSystemManager.Current.DeleteFile(path)
            ? "Deleted: " + path : "Could not delete: " + path);
    }

    private static void DeleteDirectory(CommandContext context, string[] args)
    {
        if (!RequireArguments(context, args, 1, "rmdir <path> [/s]")) return;
        string path = context.ResolvePath(args[0]);
        bool recursive = args.Length > 1 && string.Equals(args[1], "/s", StringComparison.OrdinalIgnoreCase);
        context.WriteLine(FileSystemManager.Current != null && FileSystemManager.Current.DeleteDirectory(path, recursive)
            ? "Directory deleted: " + path : "Could not delete directory: " + path);
    }

    private static void CopyFile(CommandContext context, string[] args)
    {
        if (!RequireArguments(context, args, 2, "copy <source> <destination> [/y]")) return;
        bool overwrite = args.Length > 2 && string.Equals(args[2], "/y", StringComparison.OrdinalIgnoreCase);
        string source = context.ResolvePath(args[0]);
        string destination = context.ResolvePath(args[1]);
        context.WriteLine(FileSystemManager.Current != null && FileSystemManager.Current.CopyFile(source, destination, overwrite)
            ? "Copied to " + destination : "Copy failed.");
    }

    private static void MoveFile(CommandContext context, string[] args)
    {
        if (!RequireArguments(context, args, 2, "move <source> <destination> [/y]")) return;
        bool overwrite = args.Length > 2 && string.Equals(args[2], "/y", StringComparison.OrdinalIgnoreCase);
        string source = context.ResolvePath(args[0]);
        string destination = context.ResolvePath(args[1]);
        context.WriteLine(FileSystemManager.Current != null && FileSystemManager.Current.MoveFile(source, destination, overwrite)
            ? "Moved to " + destination : "Move failed.");
    }

    private static void ListProcesses(CommandContext context, string[] args)
    {
        context.WriteLine(" PID  NAME");
        for (int i = 0; i < ProcessManger.ProcessCount; i++)
        {
            Process process = ProcessManger.GetProcessAt(i);
            if (process != null) context.WriteLine(process.id.ToString().PadLeft(4) + "  " + process.name);
        }
    }

    private static void RunBreeze(CommandContext context, string[] args)
    {
        if (!RequireArguments(context, args, 1, "run <file.breeze>")) return;
        string path = context.ResolvePath(args[0]);
        context.WriteLine(BreezeHost.RunFile(path) != null ? "Started " + path : "Could not start " + path);
    }

    private static void RunService(CommandContext context, string[] args)
    {
        if (!RequireArguments(context, args, 1, "service <file.breeze>")) return;
        string path = context.ResolvePath(args[0]);
        context.WriteLine(BreezeHost.RunScheduledFile(path) != null ? "Started service " + path : "Could not start " + path);
    }

    private static bool RequireArguments(CommandContext context, string[] args, int count, string usage)
    {
        if (args.Length >= count) return true;
        context.WriteLine("Usage: " + usage);
        return false;
    }

    private static string Join(string[] values)
    {
        string result = "";
        for (int i = 0; i < values.Length; i++) result += (i == 0 ? "" : " ") + values[i];
        return result;
    }

    private static string[] Parse(string commandLine)
    {
        List<string> parts = new List<string>();
        string current = "";
        bool quoted = false;
        string source = commandLine ?? "";

        for (int i = 0; i < source.Length; i++)
        {
            char value = source[i];
            if (value == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(value) && !quoted)
            {
                if (current != "") { parts.Add(current); current = ""; }
                continue;
            }
            current += value;
        }
        if (current != "") parts.Add(current);
        return parts.ToArray();
    }
}
