using Cosmos.Kernel.Core.IO;
using Windose.System.System_Calls;


public class SingleThreadedProcess : Process
{
    public Action onDispose;
    public Action onStart;
    public Action onUpdate;

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

            onStart?.Invoke();
        }
        catch (Exception ex)
        {
            Running = false;
            Initialized = false;
            SystemLogger.WriteLine(name, ex.Message, ConsoleMessageType.Error);
        }
    }
    public override void Main()
    {
        try
        {
            if (Running)
            {
                Update();
                onUpdate?.Invoke();
            }
        }
        catch (Exception ex)
        {
            SystemLogger.WriteLine(name, ex.Message, ConsoleMessageType.Error);
            Running = false;
        }
    }

    public virtual void Update() { }

    public override void Dispose()
    {
        onDispose?.Invoke();
        Running = false;
        Initialized = false;
    }
}



