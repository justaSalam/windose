
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;

public abstract class Process
{
    public string Name;
    public string Description;
    public int id;
    public string startTime;
    public bool Running;
    public Thread thread { get; private set; }

    public Action<Canvas> drawCall;

    private GCHandle gCHandle;




    public virtual void Start()
    {
        try
        {
            gCHandle = GCHandle.Alloc(this);
            Console.Write($"Starting {Name}: ");
            drawCall = DrawCall;
            Running = true;
            thread = new Thread(Main)
            {
                Name = Name,

            };

            thread.Start();

            Console.Write("done");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Serial.WriteString($"Thread : {Name} | failed to start");
            Serial.WriteString(ex.Message);
        }
    }
    private void Main()
    {
        while (Running)
        {

            try
            {
                Update();
                Compositor.Instance.SetDrawCall(thread.ManagedThreadId, drawCall);
                Thread.Sleep(16);
            }
            catch (Exception ex)
            {
                Serial.WriteString($"Thread : {Name} | An exception occurred");
                Serial.WriteString(ex.Message);
            }
        }


    }

    public abstract void Update();

    public virtual void Stop()
    {
        Running = false;
    }

    protected virtual void DrawCall(Canvas canvas) { }

}

