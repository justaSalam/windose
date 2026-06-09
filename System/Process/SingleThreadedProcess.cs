
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.System.Graphics;


/// <summary>
/// Meant for processes updated by the ProcessManager in the main thread
/// </summary>
public abstract class SingleThreadedProcess : Process
{
    public SingleThreadedProcess(string name, ProcessType processType)
    {
        this.name = name;
        this.processType = processType;

        Running = false;
        Initialized = false;
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
            startTime = DateTime.Now.ToString("HH:mm:ss");
            GCHandle.Alloc(this);
        }
        catch (Exception ex)
        {
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
        GarbageCollector.Collect();
    }
}



