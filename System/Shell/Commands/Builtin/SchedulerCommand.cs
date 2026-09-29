

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
        context.WriteLine($"Scheduler: {SchedulerInfo.SchedulerName}");
        context.WriteLine($"Scheduler Status:");
        context.WriteLine($"          Supported:    {SchedulerInfo.IsSupported}");
        context.WriteLine($"          Initialized:  {SchedulerInfo.IsInitialized}");
        context.WriteLine($"          Running:      {SchedulerInfo.IsRunning}");
        context.WriteLine();
        context.WriteLine($"Managed CPU Count: {SchedulerInfo.CpuCount}");
        context.WriteLine($"Live Thread Count: {SchedulerInfo.ThreadCount}");
        context.WriteLine($"Number of slots in the thread registry: {SchedulerInfo.ThreadSlotCount}");

        for (uint i = 0; i < SchedulerInfo.CpuCount; i++)
        {
            for (int x = 0; x < SchedulerInfo.GetRunQueueCount(i); x++)
            {
                if(SchedulerInfo.TryGetRunQueueThread(i, x, out var thread))
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