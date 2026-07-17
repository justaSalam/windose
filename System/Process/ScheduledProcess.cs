using System.Runtime.InteropServices;
using System.Threading;
using Cosmos.Kernel.Core.IO;

/// <summary>
/// Runs blocking or CPU-heavy work on its own managed worker thread.
/// Implementations must communicate with the UI through thread-safe queues.
/// </summary>
public abstract class ScheduledProcess : Process
{
    private GCHandle processHandle;
    private bool handleAllocated;
    private Thread workerThread;
    private volatile bool stopRequested;
    private volatile bool workerExited = true;
    private readonly int updateIntervalMs;

    protected ScheduledProcess(string name, ProcessType processType, int updateIntervalMs = 100)
    {
        this.name = name;
        this.processType = processType;
        startInfo.Name = name;
        this.updateIntervalMs = Math.Max(1, updateIntervalMs);
        Running = false;
        Initialized = false;
        canTerminate = true;
    }

    public override void Start()
    {
        if (Initialized) return;

        try
        {
            stopRequested = false;
            workerExited = false;
            Running = true;
            Initialized = true;
            startTime = DateTime.Now.ToString("HH:mm:ss");
            processHandle = GCHandle.Alloc(this);
            handleAllocated = true;
            workerThread = new Thread(WorkerLoop);
            workerThread.Start();
        }
        catch (Exception exception)
        {
            Running = false;
            Initialized = false;
            workerExited = true;
            ReleaseHandle();
            Serial.WriteString("Scheduled process " + name + " failed to start\n");
            Serial.WriteString(exception.Message + "\n");
        }
    }

    // Scheduled processes run from WorkerLoop, not ProcessManger.Update.
    public override void Main() { }

    private void WorkerLoop()
    {
        try
        {
            while (!stopRequested && Running)
            {
                long started = DateTime.UtcNow.Ticks;
                Update();
                long elapsedTicks = DateTime.UtcNow.Ticks - started;
                double elapsedMs = elapsedTicks / 10000.0;
                lastUpdateMs = elapsedMs;
                averageUpdateMs = averageUpdateMs == 0
                    ? elapsedMs
                    : averageUpdateMs * 0.9 + elapsedMs * 0.1;
                if (elapsedMs > peakUpdateMs) peakUpdateMs = elapsedMs;

                int elapsedWholeMs = (int)((elapsedTicks + TimeSpan.TicksPerMillisecond - 1)
                    / TimeSpan.TicksPerMillisecond);
                int targetIntervalMs = Math.Max(10, GetNextUpdateIntervalMs());
                int sleepMs = targetIntervalMs - elapsedWholeMs;
                Thread.Sleep(sleepMs > 10 ? sleepMs : 10);
            }
        }
        catch (Exception exception)
        {
            Serial.WriteString("Scheduled process " + name + " stopped after an error\n");
            Serial.WriteString(exception.Message + "\n");
        }
        finally
        {
            Running = false;
            workerExited = true;
        }
    }

    public abstract void Update();

    protected virtual int GetNextUpdateIntervalMs() => updateIntervalMs;

    internal void RequestStop()
    {
        stopRequested = true;
        Running = false;
    }

    internal bool HasExited => workerExited;
    public int UpdateIntervalMs => updateIntervalMs;

    public override void Dispose()
    {
        RequestStop();
        Initialized = false;
        workerThread = null;
        ReleaseHandle();
    }

    private void ReleaseHandle()
    {
        if (!handleAllocated) return;
        processHandle.Free();
        handleAllocated = false;
    }
}
