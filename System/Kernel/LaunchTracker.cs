using System;
using System.Collections.Generic;
using System.Threading;
using Cosmos.Kernel.Core.Scheduler;
using Windose.System.System_Calls;
using Windose.System.Elf;

namespace Windose.System.Kernel
{
    public static class LaunchTracker
    {
        private static readonly Queue<Action> launchQueue = new();
        private static readonly object queueLock = new();
        private static readonly AutoResetEvent queueSignal = new(false);
        private static Thread? launcherThread;
        private static volatile bool isRunning = true;

        public static int startingCount;
        public static bool isStarting => Volatile.Read(ref startingCount) > 0;

        static LaunchTracker()
        {
            EnsureWorker();
        }

        private static void EnsureWorker()
        {
            if (launcherThread != null && launcherThread.IsAlive) return;
            launcherThread = new Thread(LauncherLoop);
            launcherThread.Start();
        }

        private static void Enqueue(Action work)
        {
            Interlocked.Increment(ref startingCount);
            EnsureWorker();
            lock (queueLock)
            {
                launchQueue.Enqueue(work);
            }
            queueSignal.Set();
        }

        private static void LauncherLoop()
        {
            while (isRunning)
            {
                queueSignal.WaitOne();
                while (true)
                {
                    Action? work = null;
                    lock (queueLock)
                    {
                        if (launchQueue.Count > 0)
                            work = launchQueue.Dequeue();
                    }
                    if (work == null) break;

                    try
                    {
                        work();
                    }
                    catch (Exception ex)
                    {
                        SystemLogger.WriteLine("LaunchTracker", $"Error executing launch task: {ex.Message}", ConsoleMessageType.Error, true);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref startingCount);
                    }
                }
            }
        }

        public static void Start(Func<Window> create)
        {
            Enqueue(() =>
            {
                Window? win = null;
                try
                {
                    win = create();
                    if (win != null)
                    {
                        WindowManager.Register(win);
                    }
                }
                catch (Exception ex)
                {
                    SystemLogger.WriteLine("LaunchTracker", $"Error launching window: {ex.Message}", ConsoleMessageType.Error, true);
                    SystemLogger.WriteLine("LaunchTracker", $"           Stack trace: {ex.StackTrace}", ConsoleMessageType.Error, true);
                    SystemLogger.WriteLine("LaunchTracker", $"                  Type: {ex.GetType().FullName}", ConsoleMessageType.Error, true);

                    if (win != null)
                    {
                        try { win.Dispose(); } catch { }
                    }

                    WindowManager.ReportLaunchCrash(ex);
                }
            });
        }

        public static void StartElf(string path, string[]? args = null, Action<ElfProgram?>? onStarted = null)
        {
            Enqueue(() =>
            {
                try
                {
                    ElfProgram? prog = ElfRunner.RunFile(path, args);
                    onStarted?.Invoke(prog);
                }
                catch (Exception ex)
                {
                    SystemLogger.WriteLine("LaunchTracker", $"Error launching ELF {path}: {ex.Message}", ConsoleMessageType.Error, true);
                    WindowManager.ReportLaunchCrash(ex);
                }
            });
        }

        public static void StartElfBytes(byte[] data, string name, string[] args, Action<ElfProgram?>? onStarted = null)
        {
            Enqueue(() =>
            {
                try
                {
                    ElfProgram? prog = ElfRunner.Spawn(data, name, args, out _);
                    onStarted?.Invoke(prog);
                }
                catch (Exception ex)
                {
                    SystemLogger.WriteLine("LaunchTracker", $"Error launching ELF {name}: {ex.Message}", ConsoleMessageType.Error, true);
                    WindowManager.ReportLaunchCrash(ex);
                }
            });
        }
    }
}
