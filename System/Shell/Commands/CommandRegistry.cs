using Cosmos.Executable.Lua;
using Windose.System.Kernel.FileSystem;
using Windose.System.Shell.Commands;

public delegate void CommandHandler(CommandContext context, string[] arguments);


public static class CommandRegistry
{
    
    private static readonly Dictionary<string, IShellCommand> commands =
        new Dictionary<string, IShellCommand>(StringComparer.OrdinalIgnoreCase);
    private static readonly List<IShellCommand> orderedCommands = new List<IShellCommand>();
    private static bool builtInsRegistered;

    public static IReadOnlyList<IShellCommand> Commands => orderedCommands;


    static CommandRegistry()
    {
    }

    public static bool Register(IShellCommand command)
    {
        if (command == null || commands.ContainsKey(command.Name)) return false;
        commands[command.Name] = command;
        orderedCommands.Add(command);
        return true;
    }

    public static bool RegisterAlias(string alias, string commandName)
    {
        if (!commands.TryGetValue(commandName ?? "", out IShellCommand command)) return false;
        string key = (alias ?? "").Trim();
        if (key == "" || commands.ContainsKey(key)) return false;
        commands[key] = command;
        return true;
    }

    public static void Execute(CommandContext context, string commandLine)
    {
        if(context == null) throw new ArgumentNullException(nameof(context));

        string[] parts = Parse(commandLine);
        if (parts.Length == 0) return;

        if (!commands.TryGetValue(parts[0], out IShellCommand command))
        {
            string[] files = Directory.GetFiles(SystemPaths.SystemLua, "*.lua");

            if (files.Any(f => Path.GetFileName(f).Equals(parts[0], StringComparison.OrdinalIgnoreCase)))
            {
                LuaInterpreter lua = new()
                {
                    WorkingDirectory = context.CurrentDirectory, // where dofile, require and io.open start relative paths from
                };
                LuaBindings.RegisterBindings(lua, context);

                try
                {
                    lua.DoFile($"{SystemPaths.SystemLua}/{parts[0]}");
                }
                catch (LuaException e)
                {
                    // A syntax error, or a runtime error no pcall caught
                    context.WriteLine(e.Message);
                    context.WriteLine(e.LuaStackTrace);
                }
                catch (LuaExitException e)
                {
                    context.WriteLine($"Lua script exited with code {e.ExitCode}");
                    // The script called os.exit(e.ExitCode)
                }
            }
            else
            {
                context.WriteLine("Bad command or file name: " + parts[0]);
            }
            return;
        }

        string[] arguments = new string[parts.Length - 1];
        Array.Copy(parts, 1, arguments, 0, arguments.Length);

        try
        {
            command.Execute(context, arguments);
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

        // File/system commands can stay as tiny inline classes or lambdas via
        // a thin adapter (see DelegateCommand below) if a full class is overkill.
        Register(new DelegateCommand("help", "Displays help information.", "help",(context, args) =>
        {
            foreach (IShellCommand command in Commands)
            {
                context.WriteLine($"{command.Name} - {command.Description}");
            }
            
        }));
        Register(new DelegateCommand("echo", "Echoes the input arguments.", "echo <ars>",(context, args) =>
        {
            context.WriteLine(string.Join(" ", args));
        }));
        Register(new DelegateCommand("cls", "Clears the command line.", "cls", (context, args) =>
        {
            context.Clear();
        }));

        Register(new UacCommand());
        Register(new DirectoryCommand());
        Register(new ChangeDirectoryCommand());
        Register(new SchedulerInfoCommand());
        Register(new MemoryInfoCommand());
        Register(new DiskPart());


        // Register(new DiskManagerCommand());
        // Register(new SystemPropertiesCommand());
        // ...
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
