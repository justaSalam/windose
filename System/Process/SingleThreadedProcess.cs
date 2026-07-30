using Cosmos.Kernel.Core.IO;


public abstract class SingleThreadedProcess : Process
{

    public SingleThreadedProcess(string name, ProcessType processType)
    {
        this.name = name;
        this.processType = processType;
        startInfo.Name = name;

        Running = false;
        Initialized = false;
        canTerminate = true;
    }

    public override void Start()
    {
        try
        {
            if (Initialized) return;

            Running = true;
            Initialized = true;
            startTime = DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            Running = false;
            Initialized = false;
            Serial.WriteString($"Thread : {name} | failed to start");
            Serial.WriteString(ex.Message);
        }
    }
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
            Running = false;
        }
    }

    public abstract void Update();

    public override void Dispose()
    {
        Running = false;
        Initialized = false;
    }
}



