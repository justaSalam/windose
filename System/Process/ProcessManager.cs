
public static class ProcessManger
{
    private sealed class PendingRestart
    {
        public Process Original;
        public Func<Process> Factory;
    }

    private sealed class PendingRestartRequest
    {
        public Process Process;
        public bool Force;
    }

    public static List<SingleThreadedProcess> processes = new();
    public static List<ScheduledProcess> scheduledProcesses = new();
    private static readonly List<PendingRestart> pendingRestarts = new();
    private static readonly List<Process> pendingStarts = new();
    private static readonly List<Process> pendingStops = new();
    private static readonly List<PendingRestartRequest> pendingRestartRequests = new();
    private static readonly object pendingLock = new object();
    private static int processId;
    private static bool isUpdating;

    public static SingleThreadedProcess Start(SingleThreadedProcess process)
    {
        if (process == null) return null;
        if (isUpdating)
        {
            QueueStart(process);
            return process;
        }

        return StartNow(process);
    }

    private static SingleThreadedProcess StartNow(SingleThreadedProcess process)
    {
        process.id = processId++;

        processes.Add(process);
        process.Start();
        return process;

    }

    public static ScheduledProcess Start(ScheduledProcess process)
    {
        if (process == null) return null;
        if (isUpdating)
        {
            QueueStart(process);
            return process;
        }

        return StartNow(process);
    }

    private static ScheduledProcess StartNow(ScheduledProcess process)
    {
        process.id = processId++;
        scheduledProcesses.Add(process);
        process.Start();
        return process;
    }

    public static void Update()
    {
        isUpdating = true;
        try
        {
            ApplyPendingRequests();
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

            StartPendingRestarts();
            ApplyPendingRequests();
        }
        finally
        {
            isUpdating = false;
        }
    }

    public static void QueueStart(Process process)
    {
        if (process == null) return;
        lock (pendingLock) pendingStarts.Add(process);
    }

    public static void QueueStop(Process process)
    {
        if (process == null) return;
        lock (pendingLock) pendingStops.Add(process);
    }

    public static void QueueRestart(Process process, bool force = false)
    {
        if (process == null) return;
        lock (pendingLock) pendingRestartRequests.Add(new PendingRestartRequest { Process = process, Force = force });
    }

    private static void ApplyPendingRequests()
    {
        List<Process> starts;
        List<Process> stops;
        List<PendingRestartRequest> restarts;
        lock (pendingLock)
        {
            starts = new List<Process>(pendingStarts);
            stops = new List<Process>(pendingStops);
            restarts = new List<PendingRestartRequest>(pendingRestartRequests);
            pendingStarts.Clear();
            pendingStops.Clear();
            pendingRestartRequests.Clear();
        }

        for (int i = 0; i < stops.Count; i++) Stop(stops[i]);
        for (int i = 0; i < restarts.Count; i++) RestartInternal(restarts[i].Process, restarts[i].Force);
        for (int i = 0; i < starts.Count; i++)
        {
            if (starts[i] is ScheduledProcess scheduled) StartNow(scheduled);
            else if (starts[i] is SingleThreadedProcess singleThreaded) StartNow(singleThreaded);
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

    public static bool Restart(Process process)
    {
        return RestartInternal(process, false);
    }

    private static bool RestartInternal(Process process, bool force)
    {
        if (process == null || (!force && !process.canTerminate) || !process.CanRestart || !Contains(process))
            return false;

        for (int i = 0; i < pendingRestarts.Count; i++)
            if (pendingRestarts[i].Original == process)
                return false;

        pendingRestarts.Add(new PendingRestart
        {
            Original = process,
            Factory = process.startInfo.RestartFactory,
        });
        Stop(process);
        return true;
    }

    private static void StartPendingRestarts()
    {
        for (int i = pendingRestarts.Count - 1; i >= 0; i--)
        {
            PendingRestart pending = pendingRestarts[i];
            if (Contains(pending.Original)) continue;
            pendingRestarts.RemoveAt(i);

            Process replacement = null;
            try { replacement = pending.Factory?.Invoke(); }
            catch (Exception exception)
            {
                Cosmos.Kernel.Core.IO.Serial.WriteString(
                    "Could not restart " + pending.Original.name + ": " + exception.Message + "\n");
            }

            if (replacement is ScheduledProcess scheduled) StartNow(scheduled);
            else if (replacement is SingleThreadedProcess singleThreaded) StartNow(singleThreaded);
        }
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
