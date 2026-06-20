public sealed class BreezeProcessHandle
{
    internal readonly BreezeRuntime Runtime;
    internal string name;

    internal BreezeProcessHandle(BreezeRuntime runtime)
    {
        Runtime = runtime;
        name = "Breeze Application";
    }

    public string Name => name;
    public bool Running => !Runtime.IsTerminated;
}

public sealed class BreezeTimerHandle
{
    internal readonly BreezeRuntime Runtime;
    internal List<BreezeStatement> TickBody;
    internal long NextTick;
    internal int interval;
    internal bool active;

    internal BreezeTimerHandle(BreezeRuntime runtime, int interval)
    {
        Runtime = runtime;
        this.interval = interval;
        active = true;
        Reset();
    }

    internal void Reset() => NextTick = DateTime.UtcNow.Ticks + interval * TimeSpan.TicksPerMillisecond;
    public int Interval => interval;
    public bool Active => active;
}

public sealed class BreezeProcessMessage
{
    internal BreezeProcessMessage(string name, object data, string sender)
    {
        Name = name;
        Data = data;
        Sender = sender;
    }

    public string Name { get; }
    public object Data { get; }
    public string Sender { get; }
}

public sealed class BreezeApplicationProcess : SingleThreadedProcess
{
    private readonly string source;
    public BreezeRuntime Runtime { get; }

    public BreezeApplicationProcess(string source)
        : base("Breeze Application", ProcessType.Program)
    {
        this.source = source ?? "";
        canTerminate = true;
        Runtime = new BreezeRuntime(StopFromRuntime, SetApplicationName);
    }

    public override void Start()
    {
        base.Start();
        if (!Running) return;
        Runtime.Execute(source);
        if (Runtime.LastError != null) Running = false;
    }

    public override void Update()
    {
        Runtime.UpdateProcess();
    }

    private void StopFromRuntime()
    {
        Running = false;
    }

    private void SetApplicationName(string value)
    {
        if (!string.IsNullOrEmpty(value)) name = value;
    }

    public override void Dispose()
    {
        Running = false;
        Runtime.TerminateApplication();
        base.Dispose();
    }
}
