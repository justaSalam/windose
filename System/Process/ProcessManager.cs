
public static class ProcessManger
{
    public static List<SingleThreadedProcess> processes = new();
    public static List<ScheduledProcess> scheduledProcesses = new();
    private static int processId;

    public static SingleThreadedProcess Start(SingleThreadedProcess process)
    {
        process.id = processId++;
        processes.Add(process);
        process.Start();
        return process;
    }

    public static ScheduledProcess Start(ScheduledProcess process)
    {
        process.id = processId++;
        scheduledProcesses.Add(process);
        process.Start();
        return process;
    }

    public static void Update()
    {
        for (int i = 0; i < processes.Count; i++)
        {
            SingleThreadedProcess process = processes[i];
            if (!process.Running) continue;
            long started = DateTime.UtcNow.Ticks;
            process.Main();

            double elapsedMs = (DateTime.UtcNow.Ticks - started) / 10000.0;
            process.lastUpdateMs = elapsedMs;
            process.averageUpdateMs = process.averageUpdateMs == 0
                ? elapsedMs
                : process.averageUpdateMs * 0.9 + elapsedMs * 0.1;

            if (elapsedMs > process.peakUpdateMs)
                process.peakUpdateMs = elapsedMs;
        }

        for (int i = processes.Count - 1; i >= 0; i--)
        {
            SingleThreadedProcess process = processes[i];
            if (process.Running) continue;
            processes.RemoveAt(i);
            process.Dispose();
        }

        for (int i = scheduledProcesses.Count - 1; i >= 0; i--)
        {
            ScheduledProcess process = scheduledProcesses[i];
            if (process.Running || !process.HasExited) continue;
            scheduledProcesses.RemoveAt(i);
            process.Dispose();
        }
    }

    public static void Stop(SingleThreadedProcess process)
    {
        if (process != null) process.Running = false;
    }

    public static void Stop(ScheduledProcess process)
    {
        process?.RequestStop();
    }

    public static void Stop(Process process)
    {
        if (process is ScheduledProcess scheduled) Stop(scheduled);
        else if (process is SingleThreadedProcess singleThreaded) Stop(singleThreaded);
    }

    public static int ProcessCount => processes.Count + scheduledProcesses.Count;

    public static Process GetProcessAt(int index)
    {
        if (index < 0) return null;
        if (index < processes.Count) return processes[index];
        index -= processes.Count;
        return index < scheduledProcesses.Count ? scheduledProcesses[index] : null;
    }

    public static bool Contains(Process process)
    {
        if (process is ScheduledProcess scheduled) return scheduledProcesses.Contains(scheduled);
        if (process is SingleThreadedProcess singleThreaded) return processes.Contains(singleThreaded);
        return false;
    }

    public static T? GetProcess<T>() where T : Process
    {
        foreach (SingleThreadedProcess process in processes)
        {
            if (process is T typed)
                return typed;
        }

        foreach (ScheduledProcess process in scheduledProcesses)
        {
            if (process is T typed)
                return typed;
        }

        return null;
    }

}
