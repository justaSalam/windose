namespace Windose;

public static class PerformanceMetrics
{
    public static long ProcessTicks;
    public static long WindowManagerTicks;
    public static long MessageTicks;
    public static long ComposeTicks;
    public static long UploadTicks;
    public static long OverlayTicks;
    public static long DisplayTicks;

    public static double ProcessMs => ToMilliseconds(ProcessTicks);
    public static double WindowManagerMs => ToMilliseconds(WindowManagerTicks);
    public static double MessageMs => ToMilliseconds(MessageTicks);
    public static double ComposeMs => ToMilliseconds(ComposeTicks);
    public static double UploadMs => ToMilliseconds(UploadTicks);
    public static double OverlayMs => ToMilliseconds(OverlayTicks);
    public static double DisplayMs => ToMilliseconds(DisplayTicks);
    public static double PresentMs => UploadMs + OverlayMs + DisplayMs;
    public static double InputAndUpdateMs => Math.Max(0, WindowManagerMs - MessageMs - ComposeMs);

    public static long Now => DateTime.UtcNow.Ticks;

    public static void BeginFrame()
    {
        ProcessTicks = 0;
        WindowManagerTicks = 0;
        MessageTicks = 0;
        ComposeTicks = 0;
        UploadTicks = 0;
        OverlayTicks = 0;
        DisplayTicks = 0;
    }

    public static void AddWindowManager(long startedAt)
    {
        WindowManagerTicks += Now - startedAt;
    }

    public static void AddMessages(long startedAt)
    {
        MessageTicks += Now - startedAt;
    }

    public static void AddCompose(long startedAt)
    {
        ComposeTicks += Now - startedAt;
    }

    private static double ToMilliseconds(long ticks)
    {
        return ticks / 10000.0;
    }
}
