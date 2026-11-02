using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.Executable.Lua;
using Windose.System.Elf;
using Windose.System.Kernel.FileSystem;
using Windose.System.Lua;
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

    public static string? ResolveElfPath(string currentDir, string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;

        // 1. Direct or rooted path
        if (File.Exists(target)) return target;

        // 2. Relative to current directory
        string c1 = Path.Combine(currentDir, target);
        if (File.Exists(c1)) return c1;

        if (!target.EndsWith(".elf", StringComparison.OrdinalIgnoreCase))
        {
            string c1e = Path.Combine(currentDir, target + ".elf");
            if (File.Exists(c1e)) return c1e;
        }

        // 3. /mnt/Programs/
        string p1 = Path.Combine("/mnt/Programs", target);
        if (File.Exists(p1)) return p1;

        if (!target.EndsWith(".elf", StringComparison.OrdinalIgnoreCase))
        {
            string p1e = Path.Combine("/mnt/Programs", target + ".elf");
            if (File.Exists(p1e)) return p1e;
        }

        // 4. /mnt/Apps/
        string a1 = Path.Combine("/mnt/Apps", target);
        if (File.Exists(a1)) return a1;

        if (!target.EndsWith(".elf", StringComparison.OrdinalIgnoreCase))
        {
            string a1e = Path.Combine("/mnt/Apps", target + ".elf");
            if (File.Exists(a1e)) return a1e;
        }

        return null;
    }

    public static bool ExecuteElf(CommandContext context, string elfPath, string[] args)
    {
        try
        {
            byte[] fileBytes = File.ReadAllBytes(elfPath);
            string progName = Path.GetFileName(elfPath);
            string[] fullArgs = new string[args.Length + 1];
            fullArgs[0] = progName;
            Array.Copy(args, 0, fullArgs, 1, args.Length);

            ElfProgram program = ElfRunner.Spawn(fileBytes, progName, fullArgs, out string error);
            if (program == null)
            {
                context.WriteLine($"Failed to start ELF program {progName}: {error}");
                return false;
            }

            program.Env["PWD"] = context.CurrentDirectory;

            var lineBuf = new System.Text.StringBuilder();
            program.OutputHandler = text =>
            {
                if (text == "\033[2J")
                {
                    context.Clear();
                    lock (lineBuf) { lineBuf.Clear(); }
                    return;
                }

                lock (lineBuf)
                {
                    for (int i = 0; i < text.Length; i++)
                    {
                        char c = text[i];
                        if (c == '\n')
                        {
                            string line = lineBuf.ToString();
                            lineBuf.Clear();
                            context.WriteLine(line);
                        }
                        else if (c != '\r')
                        {
                            lineBuf.Append(c);
                        }
                    }
                }
            };

            // If the program completes quickly (CLI commands like dir, cat, echo, mem),
            // wait for it so the prompt returns immediately after output.
            // If it creates a GUI window, unblock the terminal promptly.
            bool exited = program.Wait(200);
            if (!exited)
            {
                if (program.Objs.Count == 0)
                {
                    program.Wait();
                }
            }

            lock (lineBuf)
            {
                if (lineBuf.Length > 0)
                {
                    context.WriteLine(lineBuf.ToString());
                    lineBuf.Clear();
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            context.WriteLine($"ELF execution error: {ex.Message}");
            return false;
        }
    }

    public static void Execute(CommandContext context, string commandLine)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        string trimmed = (commandLine ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        // Check for redirection > or >>
        int redirIndex = -1;
        bool append = false;
        int appendIdx = trimmed.IndexOf(">>", StringComparison.Ordinal);
        if (appendIdx >= 0)
        {
            redirIndex = appendIdx;
            append = true;
        }
        else
        {
            int singleIdx = trimmed.IndexOf('>');
            if (singleIdx >= 0)
            {
                redirIndex = singleIdx;
                append = false;
            }
        }

        if (redirIndex >= 0)
        {
            string cmdPart = trimmed.Substring(0, redirIndex).Trim();
            string filePart = trimmed.Substring(redirIndex + (append ? 2 : 1)).Trim().Trim('"');

            if (string.IsNullOrEmpty(filePart))
            {
                context.WriteLine("Syntax error: no output file specified for redirection.");
                return;
            }

            string targetPath = context.ResolvePath(filePart);
            try
            {
                if (!append)
                {
                    File.WriteAllText(targetPath, "");
                }
            }
            catch (Exception ex)
            {
                context.WriteLine($"Redirection failed: {ex.Message}");
                return;
            }

            Action<string> redirectAction = line =>
            {
                try
                {
                    File.AppendAllText(targetPath, line + "\n");
                }
                catch (Exception ex)
                {
                    context.WriteLine($"Error writing to {filePart}: {ex.Message}");
                }
            };

            CommandContext redirectContext = new CommandContext(redirectAction, context.Clear, context.Close, context.ReadLine)
            {
                CurrentDirectory = context.CurrentDirectory
            };

            Execute(redirectContext, cmdPart);
            context.CurrentDirectory = redirectContext.CurrentDirectory;
            return;
        }

        string[] parts = Parse(commandLine);
        if (parts.Length == 0) return;

        string cmd = parts[0];
        string[] arguments = new string[parts.Length - 1];
        Array.Copy(parts, 1, arguments, 0, arguments.Length);

        // Internal shell commands that must run in-process (e.g. 'cd' to change current working directory)
        if (string.Equals(cmd, "cd", StringComparison.OrdinalIgnoreCase))
        {
            if (commands.TryGetValue(cmd, out IShellCommand cdCmd))
            {
                cdCmd.Execute(context, arguments);
                return;
            }
        }

        // 1. Try to resolve and execute an ELF binary first (e.g. dir, cat, echo, mem, calc, terminal, etc.)
        string? elfPath = ResolveElfPath(context.CurrentDirectory, cmd);
        if (elfPath != null)
        {
            ExecuteElf(context, elfPath, arguments);
            return;
        }

        // 2. Try C# built-in shell commands
        if (commands.TryGetValue(cmd, out IShellCommand command))
        {
            try
            {
                command.Execute(context, arguments);
            }
            catch (Exception exception)
            {
                context.WriteLine("Command failed: " + exception.Message);
            }
            return;
        }

        // 3. Try Lua script
        string scriptPath = ResolveLuaScript(cmd);
        if (scriptPath != "")
        {
            LuaInterpreter lua = new()
            {
                WorkingDirectory = context.CurrentDirectory,
            };
            try
            {
                new LuaWindoseLib(scriptPath, ["*"]).Install(lua.State);
                lua.DoFile(scriptPath);
            }
            catch (LuaException e)
            {
                context.WriteLine(e.Message);
                context.WriteLine(e.LuaStackTrace);
            }
            catch (LuaExitException e)
            {
                context.WriteLine($"Lua script exited with code {e.ExitCode}");
            }
        }
        else
        {
            context.WriteLine("Bad command or file name: " + cmd);
        }
    }

    public static void EnsureBuiltIns()
    {
        if (builtInsRegistered) return;
        builtInsRegistered = true;

        // File/system commands can stay as tiny inline classes or lambdas via
        // a thin adapter (see DelegateCommand below) if a full class is overkill.
        Register(new DelegateCommand("help", "Displays help information.", "help", (context, args) =>
        {
            foreach (IShellCommand command in Commands)
            {
                context.WriteLine($"{command.Name} - {command.Description}");
            }

        }));
        Register(new DelegateCommand("echo", "Echoes the input arguments.", "echo <ars>", (context, args) =>
        {
            context.WriteLine(string.Join(" ", args));
        }));
        Register(new DelegateCommand("cls", "Clears the command line.", "cls", (context, args) =>
        {
            context.Clear();
        }));

        Register(new ElfCommand());
        Register(new IpConfig());
        Register(new FtpCommand());
        Register(new UacCommand());
        Register(new ReadCommand());
        Register(new DirectoryCommand());
        Register(new ChangeDirectoryCommand());
        Register(new SchedulerInfoCommand());
        Register(new MemoryInfoCommand());
        Register(new DrawingDebugCommand());
        Register(new DiskPart());
        Register(new DelegateCommand("restart", "Restarts a process or the shell.", "restart <shell|explorer|pid|name>", (context, args) =>
        {
            if (args.Length == 0 || args[0].Equals("shell", StringComparison.OrdinalIgnoreCase) || args[0].Equals("explorer", StringComparison.OrdinalIgnoreCase))
            {
                context.WriteLine("Restarting Explorer shell...");
                Explorer.RestartShell();
                return;
            }

            string target = args[0];
            if (int.TryParse(target, out int pid))
            {
                if (Windose.System.Elf.ElfRunner.Restart(pid) != null)
                {
                    context.WriteLine($"Restarted ELF process {pid}.");
                    return;
                }
                for (int i = 0; i < ProcessManager.ProcessCount; i++)
                {
                    Process p = ProcessManager.GetProcessAt(i);
                    if (p != null && p.id == pid)
                    {
                        if (ProcessManager.Restart(p))
                            context.WriteLine($"Restarted process '{p.name}' (PID {pid}).");
                        else
                            context.WriteLine($"Process '{p.name}' cannot be restarted.");
                        return;
                    }
                }
                context.WriteLine($"Process PID {pid} not found.");
            }
            else
            {
                if (Windose.System.Elf.ElfRunner.Restart(target) != null)
                {
                    context.WriteLine($"Restarted ELF program '{target}'.");
                    return;
                }
                for (int i = 0; i < ProcessManager.ProcessCount; i++)
                {
                    Process p = ProcessManager.GetProcessAt(i);
                    if (p != null && (string.Equals(p.name, target, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(p.name, "elf: " + target, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (ProcessManager.Restart(p))
                            context.WriteLine($"Restarted process '{p.name}'.");
                        else
                            context.WriteLine($"Process '{p.name}' cannot be restarted.");
                        return;
                    }
                }
                context.WriteLine($"Process '{target}' not found.");
            }
        }));

        Register(new DelegateCommand("cat", "Displays the contents of a file.", "cat <file>", (context, args) =>
        {
            if (args.Length < 1)
            {
                context.WriteLine("Usage: cat <file>");
                return;
            }
            string path = context.ResolvePath(args[0]);
            if (!File.Exists(path))
            {
                context.WriteLine($"File not found: {args[0]}");
                return;
            }
            try
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                    context.WriteLine(lines[i]);
            }
            catch (Exception ex)
            {
                context.WriteLine($"Error reading {args[0]}: {ex.Message}");
            }
        }));
        RegisterAlias("type", "cat");

        Register(new DelegateCommand("copy", "Copies a file to a new location.", "copy <source> <destination>", (context, args) =>
        {
            if (args.Length < 2)
            {
                context.WriteLine("Usage: copy <source> <destination>");
                return;
            }
            string src = context.ResolvePath(args[0]);
            string dst = context.ResolvePath(args[1]);
            if (!File.Exists(src))
            {
                context.WriteLine($"Source file not found: {args[0]}");
                return;
            }
            try
            {
                if (Directory.Exists(dst))
                    dst = Path.Combine(dst, Path.GetFileName(src));

                File.Copy(src, dst, true);
                context.WriteLine("1 file(s) copied.");
            }
            catch (Exception ex)
            {
                context.WriteLine($"Copy failed: {ex.Message}");
            }
        }));
        RegisterAlias("cp", "copy");

        Register(new DelegateCommand("move", "Moves or renames a file or directory.", "move <source> <destination>", (context, args) =>
        {
            if (args.Length < 2)
            {
                context.WriteLine("Usage: move <source> <destination>");
                return;
            }
            string src = context.ResolvePath(args[0]);
            string dst = context.ResolvePath(args[1]);
            try
            {
                if (File.Exists(src))
                {
                    if (Directory.Exists(dst))
                        dst = Path.Combine(dst, Path.GetFileName(src));
                    File.Move(src, dst, true);
                    context.WriteLine("1 file(s) moved.");
                }
                else if (Directory.Exists(src))
                {
                    Directory.Move(src, dst);
                    context.WriteLine("1 directory moved.");
                }
                else
                {
                    context.WriteLine($"Source not found: {args[0]}");
                }
            }
            catch (Exception ex)
            {
                context.WriteLine($"Move failed: {ex.Message}");
            }
        }));
        RegisterAlias("mv", "move");

        Register(new DelegateCommand("del", "Deletes a file or directory.", "del <path>", (context, args) =>
        {
            if (args.Length < 1)
            {
                context.WriteLine("Usage: del <path>");
                return;
            }
            string target = context.ResolvePath(args[0]);
            try
            {
                if (File.Exists(target))
                {
                    File.Delete(target);
                    context.WriteLine($"Deleted {args[0]}.");
                }
                else if (Directory.Exists(target))
                {
                    Directory.Delete(target, true);
                    context.WriteLine($"Deleted directory {args[0]}.");
                }
                else
                {
                    context.WriteLine($"File or directory not found: {args[0]}");
                }
            }
            catch (Exception ex)
            {
                context.WriteLine($"Delete failed: {ex.Message}");
            }
        }));
        RegisterAlias("rm", "del");

        Register(new DelegateCommand("mkdir", "Creates a directory.", "mkdir <path>", (context, args) =>
        {
            if (args.Length < 1)
            {
                context.WriteLine("Usage: mkdir <path>");
                return;
            }
            string target = context.ResolvePath(args[0]);
            try
            {
                Directory.CreateDirectory(target);
                context.WriteLine($"Created directory {args[0]}.");
            }
            catch (Exception ex)
            {
                context.WriteLine($"Failed to create directory: {ex.Message}");
            }
        }));
        RegisterAlias("md", "mkdir");

        Register(new DelegateCommand("ps", "Lists currently running processes.", "ps", (context, args) =>
        {
            context.WriteLine("PID    TYPE        STATUS     NAME");
            context.WriteLine("--------------------------------------------------");
            for (int i = 0; i < ProcessManager.ProcessCount; i++)
            {
                Process p = ProcessManager.GetProcessAt(i);
                if (p != null)
                {
                    string status = p.Running ? "Running" : "Stopped";
                    context.WriteLine($"{p.id,-6} {p.processType,-11} {status,-10} {p.name}");
                }
            }
            try
            {
                ElfProgram[] elfs = Windose.System.Elf.ElfRunner.List();
                if (elfs != null)
                {
                    for (int i = 0; i < elfs.Length; i++)
                    {
                        ElfProgram elf = elfs[i];
                        if (elf != null)
                        {
                            context.WriteLine($"{elf.Pid,-6} {"ELF",-11} {"Running",-10} {elf.Name}");
                        }
                    }
                }
            }
            catch { }
        }));
        RegisterAlias("tasklist", "ps");

        Register(new DelegateCommand("kill", "Terminates a process by PID or name.", "kill <pid|name>", (context, args) =>
        {
            if (args.Length < 1)
            {
                context.WriteLine("Usage: kill <pid|name>");
                return;
            }
            string target = args[0];
            if (int.TryParse(target, out int pid))
            {
                if (Windose.System.Elf.ElfRunner.Kill(pid))
                {
                    context.WriteLine($"Terminated ELF process {pid}.");
                    return;
                }
                for (int i = 0; i < ProcessManager.ProcessCount; i++)
                {
                    Process p = ProcessManager.GetProcessAt(i);
                    if (p != null && p.id == pid)
                    {
                        ProcessManager.QueueStop(p);
                        context.WriteLine($"Terminated process '{p.name}' (PID {pid}).");
                        return;
                    }
                }
                context.WriteLine($"Process PID {pid} not found.");
            }
            else
            {
                bool found = false;
                for (int i = 0; i < ProcessManager.ProcessCount; i++)
                {
                    Process p = ProcessManager.GetProcessAt(i);
                    if (p != null && string.Equals(p.name, target, StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessManager.QueueStop(p);
                        context.WriteLine($"Terminated process '{p.name}' (PID {p.id}).");
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    try
                    {
                        ElfProgram[] elfs = Windose.System.Elf.ElfRunner.List();
                        if (elfs != null)
                        {
                            for (int i = 0; i < elfs.Length; i++)
                            {
                                ElfProgram elf = elfs[i];
                                if (elf != null && string.Equals(elf.Name, target, StringComparison.OrdinalIgnoreCase))
                                {
                                    Windose.System.Elf.ElfRunner.Kill(elf.Pid);
                                    context.WriteLine($"Terminated ELF process '{elf.Name}' (PID {elf.Pid}).");
                                    found = true;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }
                if (!found) context.WriteLine($"Process '{target}' not found.");
            }
        }));
        RegisterAlias("taskkill", "kill");


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

    private static string ResolveLuaScript(string commandName)
    {
        string requested = commandName ?? "";
        string requestedWithExtension = requested.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
            ? requested
            : requested + ".lua";

        string[] files = Directory.GetFiles(SystemPaths.SystemLua, "*.lua");
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            if (name.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(requestedWithExtension, StringComparison.OrdinalIgnoreCase))
                return file;
        }

        return "";
    }
}
