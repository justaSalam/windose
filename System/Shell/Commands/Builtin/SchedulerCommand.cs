

using Cosmos.Kernel.System.Diagnostics;

public sealed class SchedulerInfoCommand : IShellCommand
{
    public string Name => "scheduler";
    public string Description => "Displays information about the scheduler";
    public string Usage => "scheduler";


    public SchedulerInfoCommand()
    {
    }

    public void Execute(CommandContext context, string[] args)
    {
        context.WriteLine($"Scheduler: {SchedulerDiagnostics.SchedulerName}");
        context.WriteLine($"Scheduler Status:");
        context.WriteLine($"          Supported:    {SchedulerDiagnostics.IsSupported}");
        context.WriteLine($"          Initialized:  {SchedulerDiagnostics.IsInitialized}");
        context.WriteLine($"          Running:      {SchedulerDiagnostics.IsRunning}");
        context.WriteLine();
        context.WriteLine($"Managed CPU Count: {SchedulerDiagnostics.CpuCount}");
        context.WriteLine($"Live Thread Count: {SchedulerDiagnostics.ThreadCount}");
        context.WriteLine($"Number of slots in the thread registry: {SchedulerDiagnostics.ThreadSlotCount}");

        for (uint i = 0; i < SchedulerDiagnostics.CpuCount; i++)
        {
            for (int x = 0; x < SchedulerDiagnostics.GetRunQueueCount(i); x++)
            {
                if(SchedulerDiagnostics.TryGetRunQueueThread(i, x, out var thread))
                {
                    context.WriteLine($"Thread {thread.Id} on CPU {i}:");
                    context.WriteLine($" Id:          {thread.Id}");
                    context.WriteLine($" CpuId:       {thread.CpuId}");
                    context.WriteLine($" State:       {thread.State}");
                    context.WriteLine($" Priority:    {thread.Priority}");
                    context.WriteLine($" Stack Size:  {thread.StackSizeBytes}");
                    context.WriteLine($" Stack Used:  {thread.IsManaged}");
                    context.WriteLine();
                }
            }
        }


    }

}