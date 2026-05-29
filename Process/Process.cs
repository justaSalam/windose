public abstract class Process
{
    public string Name;
    public string Description;
    public int id;
    public int parentId = -1;//self
    public List<Process> childIds = new();
    public string startTime;
    public bool Running;


    public virtual void Start()
    {
        Running = true;
        childIds = ProcessManger.GetChildren(id);
    }
    public virtual void Tick()
    {
        if (!Running) return;
        foreach (Process process in childIds)
        {
            process.Tick();
        }
    }
    public abstract void Stop();

}