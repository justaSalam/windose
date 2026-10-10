using Cosmos.Kernel.Core.Scheduler;
using Windose.System.System_Calls;

namespace Windose.System.Kernel
{
    public static class LaunchTracker
    {

        public static int startingCount;
        public static bool isStarting => Volatile.Read(ref startingCount) > 0;

        public static void Start(Func<Window> create)
        {
            Interlocked.Increment(ref startingCount);
            new Thread(() =>
            {
                try
                {
                    WindowManager.Register(create());
                }
                catch (Exception ex)
                {
                    SystemLogger.WriteLine("LaunchTracker", $"Error launching window: {ex.Message}", ConsoleMessageType.Error, true);
                }
                finally
                {
                    Interlocked.Decrement(ref startingCount);
                }
              

            }).Start();
            
        }
    }
}
