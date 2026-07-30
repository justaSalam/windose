public enum ProcessPriority
{
    Idle = 0,
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4,
}

public abstract class Process : IDisposable
{
    public string name = "undefined process";
    public int id;
    public string startTime = DateTime.Now.ToString();
    

    public bool canOverridePriority = true;
    public ProcessPriority Priority = ProcessPriority.Normal;
    public ProcessType processType = ProcessType.Undefined;

    public ProcessStartInfo startInfo = new ProcessStartInfo();

    public volatile bool Running;
    public bool Initialized;
    public bool canTerminate;


    public double lastUpdateMs;
    public double averageUpdateMs;
    public double peakUpdateMs;
    public bool CanRestart => startInfo?.RestartFactory != null;

    // Priority system
    public int UpdateSkipCounter { get; set; }
    public int UpdateSkipThreshold { get; set; } // 0 = never skip

    // Crash containment
    public int CrashCount { get; set; }
    public int MaxCrashesBeforeTermination { get; set; } = 3;
    public string LastCrashReason { get; set; } = "";
    public bool IsCrashed => CrashCount >= MaxCrashesBeforeTermination;

    public abstract void Start();
    public abstract void Main();
    public abstract void Dispose();
}