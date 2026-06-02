
public abstract class Process
{
    public string Name;
    public string Description;
    public int id;
    public string startTime;
    public bool Running;


    public virtual void Start(int processId)
    {
        try
        {
            Console.Write($"Starting {Name}: ");
            id = processId;
            Running = true;

            Console.Write("done");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Thread : {Name} | failed to start");
            Console.WriteLine(ex.Message);
        }
    }
    public void Main()
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

    public abstract void Update();

    public virtual void Stop()
    {
        Running = false;
    }

}

