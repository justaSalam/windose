public sealed class BreezeProcessHandle
{
    internal readonly BreezeRuntime Runtime;
    internal string name;
    internal Process OwnerProcess;

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
    internal BreezeProcessMessage(string name, object data, string sender, int id = 0, int replyTo = 0)
    {
        Name = name;
        Data = data;
        Sender = sender;
        Id = id;
        ReplyTo = replyTo;
    }

    public string Name { get; }
    public object Data { get; }
    public string Sender { get; }
    public int Id { get; }
    public int ReplyTo { get; }
}

public sealed class BreezeApplicationProcess : SingleThreadedProcess
{
    private readonly string source;
    public BreezeRuntime Runtime { get; }

    public BreezeApplicationProcess(string source, string executablePath = "", string arguments = "")
        : base("Breeze Application", ProcessType.Program)
    {
        this.source = source ?? "";
        canTerminate = true;
        startInfo.ExecutablePath = executablePath ?? "";
        startInfo.Arguments = arguments ?? "";
        startInfo.WorkingDirectory = GetWorkingDirectory(startInfo.ExecutablePath);
        startInfo.RestartFactory = () => new BreezeApplicationProcess(
            this.source, startInfo.ExecutablePath, startInfo.Arguments);
        Runtime = new BreezeRuntime(StopFromRuntime, SetApplicationName);
        Runtime.AttachProcess(this);
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
        if (string.IsNullOrEmpty(value)) return;
        name = value;
        startInfo.Name = value;
    }

    public override void Dispose()
    {
        Running = false;
        Runtime.TerminateApplication();
        base.Dispose();
    }

    private static string GetWorkingDirectory(string executablePath)
    {
        if (string.IsNullOrEmpty(executablePath)) return "";
        return FileSystemManager.GetParent(executablePath);
    }
}

public sealed class BreezeScheduledApplicationProcess : SingleThreadedProcess
{
    private readonly string source;
    private bool sourceExecuted;
    private long nextUpdateAt;
    public BreezeRuntime Runtime { get; }

    public BreezeScheduledApplicationProcess(string source, string executablePath = "", string arguments = "")
        : base("Breeze Background Process", ProcessType.Program)
    {
        this.source = source ?? "";
        canTerminate = true;
        startInfo.ExecutablePath = executablePath ?? "";
        startInfo.Arguments = arguments ?? "";
        startInfo.WorkingDirectory = string.IsNullOrEmpty(startInfo.ExecutablePath)
            ? ""
            : FileSystemManager.GetParent(startInfo.ExecutablePath);
        startInfo.RestartFactory = () => new BreezeScheduledApplicationProcess(
            this.source, startInfo.ExecutablePath, startInfo.Arguments);
        Runtime = new BreezeRuntime(StopFromRuntime, SetApplicationName, true);
        Runtime.AttachProcess(this);
        Runtime.WorkAvailable = () => nextUpdateAt = 0;
    }

    public override void Update()
    {
        long now = DateTime.UtcNow.Ticks;
        if (sourceExecuted && now < nextUpdateAt) return;

        if (!sourceExecuted)
        {
            sourceExecuted = true;
            Runtime.Execute(source);
            if (Runtime.LastError != null)
            {
                BreezeHost.ShowError(Runtime.LastError);
                Running = false;
            }
            ScheduleNextUpdate(now);
            return;
        }

        Runtime.UpdateProcess();
        ScheduleNextUpdate(now);
    }

    private void ScheduleNextUpdate(long now)
    {
        nextUpdateAt = now + Runtime.GetRecommendedUpdateIntervalMs() * TimeSpan.TicksPerMillisecond;
    }

    private void StopFromRuntime() => Running = false;

    private void SetApplicationName(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        name = value;
        startInfo.Name = value;
    }

    public override void Dispose()
    {
        BreezeServiceManager.NotifyStopped(Runtime, Runtime.LastError != null);
        Runtime.TerminateApplication();
        base.Dispose();
    }
}
