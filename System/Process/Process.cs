public abstract class Process : IDisposable
{
    public string name = "undefined process";
    public ProcessType processType;
    public int id;
    public string startTime = DateTime.Now.ToString();
    public volatile bool Running;
    public bool Initialized;
    public bool canTerminate;
    public double lastUpdateMs;
    public double averageUpdateMs;
    public double peakUpdateMs;

    public abstract void Start();
    public abstract void Main();
    public abstract void Dispose();
}
