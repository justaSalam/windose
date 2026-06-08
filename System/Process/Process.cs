
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;

public abstract class Process
{
    public string name = "undefined process";
    public ProcessType processType;
    public int id;
    public string startTime = DateTime.Now.ToString();
    public bool Running;
    private bool Initialized;

    public Process(string name, ProcessType processType)
    {
        this.name = name;
        this.processType = processType;

        Running = false;
        Initialized = false;
    }


    public virtual void Start()
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
    public void Main() //Ran only by manager
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

    public virtual void Stop()
    {
        Running = false;
    }
}

public enum ProcessType
{
    Kernel, Driver, Program, Utility, Undefined
}

