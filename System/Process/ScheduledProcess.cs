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

    protected ScheduledProcess(string name, ProcessType processType)
    {
        this.name = name;
        this.processType = processType;
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
                double elapsedMs = (DateTime.UtcNow.Ticks - started) / 10000.0;
                lastUpdateMs = elapsedMs;
                averageUpdateMs = averageUpdateMs == 0
                    ? elapsedMs
                    : averageUpdateMs * 0.9 + elapsedMs * 0.1;
                if (elapsedMs > peakUpdateMs) peakUpdateMs = elapsedMs;

                Thread.Sleep(1);
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

    internal void RequestStop()
    {
        stopRequested = true;
        Running = false;
    }

    internal bool HasExited => workerExited;

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
