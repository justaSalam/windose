
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;


/// <summary>
/// Meant for processes updated by the ProcessManager in the main thread
/// </summary>
public abstract class SingleThreadedProcess : Process
{
    private GCHandle processHandle;
    private bool handleAllocated;

    public SingleThreadedProcess(string name, ProcessType processType)
    {
        this.name = name;
        this.processType = processType;

        Running = false;
        Initialized = false;
        canTerminate = true;
    }

    /// <summary>
    /// Process init called by process manager at the start of the process' life
    /// </summary>
    public override void Start()
    {
        try
        {
            if (Initialized) return;

            Running = true;
            Initialized = true;
            startTime = DateTime.Now.ToString("HH:mm:ss");
            processHandle = GCHandle.Alloc(this);
            handleAllocated = true;
        }
        catch (Exception ex)
        {
            Running = false;
            Initialized = false;
            Serial.WriteString($"Thread : {name} | failed to start");
            Serial.WriteString(ex.Message);
        }
    }
    /// <summary>
    /// Ran only by process manager, do not call manually
    /// </summary>
    public override void Main()
    {
        try
        {
            if (Running)
                Update();
        }
        catch (Exception ex)
        {
            Serial.WriteString($"Thread : {name} | An exception occurred");
            Serial.WriteString(ex.Message);
        }
    }

    /// <summary>
    /// Process main method
    /// </summary>
    public abstract void Update();

    /// <summary>
    /// Call base.Dispose() last, GC collection might cause an exception
    /// </summary>
    public override void Dispose()
    {
        Running = false;
        Initialized = false;
        if (!handleAllocated) return;
        processHandle.Free();
        handleAllocated = false;
    }
}



