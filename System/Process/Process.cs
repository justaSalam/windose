public abstract class Process : IDisposable
{
    public string name = "undefined process";
    public ProcessType processType;
    public int id;
    public string startTime = DateTime.Now.ToString();
    public bool Running;
    public bool Initialized;

    public abstract void Start();
    public abstract void Main();
    public abstract void Dispose();
}