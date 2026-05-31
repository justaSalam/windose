
public abstract class Process
{
    public string Name;
    public string Description;
    public int id;
    public string startTime;
    public bool Running;
    public Thread thread { get; private set; }




    public virtual void Start()
    {
        try
        {
            Console.WriteLine($"Starting Thread({Name}):");
            Running = true;

            Console.WriteLine($"Creating a thread");
            thread = new Thread(Main);

            Console.WriteLine($"Running thread.Start()");
            thread.Start();
            Console.WriteLine("Done");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Thread({Name}) failed to start");
            Console.WriteLine(ex.Message);
        }
    }
    private void Main()
    {
        try
        {
            while (thread.ThreadState == ThreadState.Running || thread.ThreadState == ThreadState.Background)
            {
                //Update();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Thread({Name}) Main failed");
            Console.WriteLine(ex.Message);
        }
    }

    public abstract void Update();

    public virtual void Stop()
    {
        Running = false;
    }

}

