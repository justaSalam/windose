

using Cosmos.Kernel.System.Network;
using Cosmos.Network.Ftp;
using System.Net.Http.Headers;
using Windose.System.Elf;

public sealed class ElfCommand : IShellCommand
{
    public string Name => "elf";
    public string Description => "ELF program runner";
    public string Usage => "elf <program.elf>";
    private readonly SubcommandDispatcher dispatcher;
    private FtpServer? server;

    public ElfCommand()
    {
        dispatcher = new SubcommandDispatcher(Usage)
            .Add("list", "", 0, ListElfProcess)
            .Add("run", "run <file.elf>", 1, RunElf)
            .Add("kill", "kill <pid>", 1, KillElf);

    }

    private void RunElf(CommandContext context, string[] args)
    {
        string path = Path.Combine(context.CurrentDirectory, args[0]);
        if (!File.Exists(path))
        {
            context.WriteLine($"File not found: {path}");
            return;
        }

        new Thread((() =>
        {
            ElfRunner.Run(File.ReadAllBytes(path), "ELF", args, out int code, out string error);

            context.WriteLine($"ELF program exited with code: {code}");
        })).Start();
    }

    private void ListElfProcess(CommandContext context, string[] arg2)
    {
        context.WriteLine($"ELF Program List: ");
        context.WriteLine();
        foreach (ElfProgram program in ElfRunner.List())
        {
            context.WriteLine($"Name: {program.Args[0]}");
            context.WriteLine($"State: {program.State}");
            context.WriteLine($"Pid: {program.Pid}");
            context.WriteLine($"Image Size: {ByteSize.Format(program.ImageSize)}");
            context.WriteLine($"Image Adress: {program.ImageAddress}");
            context.WriteLine($"Allocated Memory: {ByteSize.Format(program.MemoryBytes)}");;
            context.WriteLine($"- - - - - - - - - - - - - - - - - - - -");
        }
    }

    public void Execute(CommandContext context, string[] args)
    {
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

}