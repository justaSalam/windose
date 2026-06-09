
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;

/// <summary>
/// Meant for processes updated by the Scheduler, for heavy applications that would cause the system to freeze
/// </summary>
public abstract class MultiThreadedProcess : Process
{
    public MultiThreadedProcess(string name, ProcessType processType)
    {
        this.name = name;
        this.processType = processType;

        Running = false;
        Initialized = false;
    }


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
    public override void Main() //Ran only by manager
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

    public abstract void Update(); //Process main method

}


