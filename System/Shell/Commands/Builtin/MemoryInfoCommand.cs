

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
        context.WriteLine($"Total Memory: {ByteFormat.FormatBytes(MemoryInfo.RamSizeBytes)}");
        context.WriteLine($"Used Memory:  {ByteFormat.FormatBytes(MemoryInfo.RamSizeBytes - (MemoryInfo.FreePages * MemoryInfo.PageSizeBytes))}");
        context.WriteLine($"Free Memory:  {ByteFormat.FormatBytes(MemoryInfo.FreePages * MemoryInfo.PageSizeBytes)}");
        context.WriteLine();
        context.WriteLine($"GC Information:");
        context.WriteLine($"Total Collections: {MemoryInfo.TotalCollections}");
        context.WriteLine($"Total Objects Freed: {MemoryInfo.TotalObjectsFreed}");
        context.WriteLine($"GC Time Percent: {MemoryInfo.GcTimePercent}%");


    }

}