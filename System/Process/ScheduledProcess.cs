using Cosmos.Kernel.Core.IO;

/// <summary>
/// Runs blocking or CPU-heavy work on its own managed worker thread.
/// Implementations must communicate with the UI through thread-safe queues.
/// </summary>
public class ScheduledProcess : Process
{

    public Action onStart;
    public Action onUpdate;
    public Action onDispose;

    private Thread workerThread;
    private volatile bool stopRequested = false;
    private volatile bool workerExited = false;
    private readonly int updateIntervalMs;

    public ScheduledProcess(string name, ProcessType processType)
    {
        startInfo.Name = name;

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
            Running = true;

            Initialized = true;

            startTime = DateTime.Now.ToString("HH:mm:ss");

            workerThread = new Thread(WorkerLoop);
            workerThread.Start();
            onStart?.Invoke();
        }
        catch (Exception exception)
        {
            Running = false;
            Initialized = false;

            workerExited = true;
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
                Update();

                Thread.Sleep(0);
            }
        }
        catch (Exception exception)
        {
        }
        finally
        {
            Running = false;
            workerExited = true;
        }
    }

    public virtual void Update()
    {
        onUpdate?.Invoke();
    }

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
        onDispose?.Invoke();
    }
}

