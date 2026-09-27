using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.HAL.Vfs;
using Cosmos.Kernel.System;
using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Filesystems.Fat;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.Vfs;
using Windose.System.Kernel.Subsystem;

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
            context.WriteLine("Bad command or file name: " + parts[0]);
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
