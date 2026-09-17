using System.Diagnostics;

public static class Clock
{
    public static long Timestamp =>
        Stopwatch.GetTimestamp();

    public static long Milliseconds =>
        Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

    public static ulong Nanoseconds
    {
        get
        {
            long ticks = Stopwatch.GetTimestamp();

            return (ulong)(
                ticks * 1_000_000_000L /
                Stopwatch.Frequency
            );
        }
    }

}