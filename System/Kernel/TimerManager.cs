public static class KernelTime
{
    public static ulong SystemTicks = 0;
}

public static class TimerManager
{
    private static List<Timer> timers = new();

    public static void SetTimeout(Action callback, ulong delayTicks)
    {
        timers.Add(new Timer
        {
            TriggerTick = KernelTime.SystemTicks + delayTicks,
            Callback = callback
        });
    }

    public static void Update()
    {
        foreach (var timer in timers)
        {
            if (!timer.Fired &&
                KernelTime.SystemTicks >= timer.TriggerTick)
            {
                timer.Fired = true;
                timer.Callback();
            }
        }

        timers.RemoveAll(t => t.Fired);
    }
}

public class Timer
{
    public ulong TriggerTick;
    public Action Callback;
    public bool Fired;
}