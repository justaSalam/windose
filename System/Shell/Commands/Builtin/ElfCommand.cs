using System;
using System.IO;
using System.Threading;
using Windose.System.Elf;

public sealed class ElfCommand : IShellCommand
{
    public string Name => "elf";
    public string Description => "ELF program runner";
    public string Usage => "elf <program.elf> | elf run <file.elf> | elf list | elf kill <pid> | elf wait <pid>";
    private readonly SubcommandDispatcher dispatcher;

    public ElfCommand()
    {
        dispatcher = new SubcommandDispatcher(Usage)
            .Add("list", "", 0, ListElfProcess)
            .Add("run", "run <file.elf>", 1, RunElf)
            .Add("kill", "kill <pid>", 1, KillElf)
            .Add("wait", "wait <pid>", 1, WaitElf);
    }

    private void RunElf(CommandContext context, string[] args)
    {
        string raw = args[0];
        string? path = ResolveElfPath(context.CurrentDirectory, raw);
        if (path == null)
        {
            context.WriteLine($"File not found: {raw}");
            return;
        }

        string progName = Path.GetFileName(path);
        new Thread(() =>
        {
            if (!ElfRunner.Run(File.ReadAllBytes(path), progName, args, out int code, out string error))
            {
                context.WriteLine($"Failed to start ELF program: {error}");
                return;
            }

            context.WriteLine($"ELF program exited with code: {code}");
        }).Start();
    }

    private string? ResolveElfPath(string currentDir, string target)
    {
        if (string.IsNullOrEmpty(target)) return null;

        // Direct / rooted
        if (File.Exists(target)) return target;

        // Current directory
        string c1 = Path.Combine(currentDir, target);
        if (File.Exists(c1)) return c1;

        // Current directory with .elf
        if (!target.EndsWith(".elf", StringComparison.OrdinalIgnoreCase))
        {
            string c1e = Path.Combine(currentDir, target + ".elf");
            if (File.Exists(c1e)) return c1e;
        }

        // /mnt/Programs/
        string p1 = Path.Combine("/mnt/Programs", target);
        if (File.Exists(p1)) return p1;

        // /mnt/Programs/ with .elf
        if (!target.EndsWith(".elf", StringComparison.OrdinalIgnoreCase))
        {
            string p1e = Path.Combine("/mnt/Programs", target + ".elf");
            if (File.Exists(p1e)) return p1e;
        }

        return null;
    }

    private void ListElfProcess(CommandContext context, string[] arg2)
    {
        context.WriteLine("ELF Program List: ");
        context.WriteLine();
        foreach (ElfProgram program in ElfRunner.List())
        {
            context.WriteLine($"Name: {program.Args[0]}");
            context.WriteLine($"State: {program.State}");
            context.WriteLine($"Pid: {program.Pid}");
            context.WriteLine($"Image Size: {ByteSize.Format(program.ImageSize)}");
            context.WriteLine($"Image Address: 0x{program.ImageAddress:X}");
            context.WriteLine($"Allocated Memory: {ByteSize.Format(program.MemoryBytes)}");
            context.WriteLine("- - - - - - - - - - - - - - - - - - - -");
        }
    }

    public void Execute(CommandContext context, string[] args)
    {
        if (args.Length == 0)
        {
            context.WriteLine("Usage: " + Usage);
            return;
        }

        string sub = args[0];
        if (!sub.Equals("list", StringComparison.OrdinalIgnoreCase) &&
            !sub.Equals("run", StringComparison.OrdinalIgnoreCase) &&
            !sub.Equals("kill", StringComparison.OrdinalIgnoreCase) &&
            !sub.Equals("wait", StringComparison.OrdinalIgnoreCase))
        {
            RunElf(context, args);
            return;
        }

        dispatcher.Dispatch(context, args);
    }

    private void KillElf(CommandContext context, string[] args)
    {
        if (!int.TryParse(args[0], out int pid))
        {
            context.WriteLine($"Invalid pid: {args[0]}");
            return;
        }

        context.WriteLine(ElfRunner.Kill(pid) ? $"Sent kill to pid {pid}." : $"No such program: {pid}.");
    }

    private void WaitElf(CommandContext context, string[] args)
    {
        if (!int.TryParse(args[0], out int pid))
        {
            context.WriteLine($"Invalid pid: {args[0]}");
            return;
        }

        context.WriteLine($"Waiting for process {pid}...");
        int r = ElfRunner.WaitPid(pid, out int code, -1);
        if (r == 0)
            context.WriteLine($"Process {pid} exited with code: {code}");
        else if (r == -2)
            context.WriteLine($"Wait timed out for process {pid}");
        else
            context.WriteLine($"Process {pid} not found");
    }
}
