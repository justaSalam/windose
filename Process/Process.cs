
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
            Console.Write($"Starting {Name}: ");

            Running = true;
            thread = new Thread(Main)
            {

                Priority = ThreadPriority.Highest
            };
            thread.Start();

            Console.Write("done");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Thread : {Name} | failed to start");
            Console.WriteLine(ex.Message);
        }
    }
    private void Main()
    {
        while (Running)
        {
            try
            {
                Update();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Thread : {Name} | An exception occurred");
                Console.WriteLine(ex.Message);
            }
        }
    }

    public abstract void Update();

    public virtual void Stop()
    {
        Running = false;
    }

}

