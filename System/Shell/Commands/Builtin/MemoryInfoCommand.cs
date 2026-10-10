

using Cosmos.Kernel.System.Diagnostics;

public sealed class MemoryInfoCommand : IShellCommand
{
    public string Name => "memory";
    public string Description => "Displays information about memory usage";
    public string Usage => "memory";


    public MemoryInfoCommand()
    {
    }

    public void Execute(CommandContext context, string[] args)
    {
        context.WriteLine($"Memory Information:");
        context.WriteLine($"Total Memory: {ByteSize.Format(MemoryDiagnostics.RamSizeBytes)}");
        context.WriteLine($"Used Memory:  {ByteSize.Format(MemoryDiagnostics.RamSizeBytes - (MemoryDiagnostics.FreePages * MemoryDiagnostics.PageSizeBytes))}");
        context.WriteLine($"Free Memory:  {ByteSize.Format(MemoryDiagnostics.FreePages * MemoryDiagnostics.PageSizeBytes)}");
        context.WriteLine();
        context.WriteLine($"GC Information:");
        context.WriteLine($"Total Collections: {MemoryDiagnostics.TotalCollections}");
        context.WriteLine($"Total Objects Freed: {MemoryDiagnostics.TotalObjectsFreed}");
        context.WriteLine($"GC Time Percent: {MemoryDiagnostics.GcTimePercent}%");


    }

}