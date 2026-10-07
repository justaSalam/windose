using Cosmos.Kernel.Core.Scheduler;

namespace Windose.System.Scheduling;

public sealed class MlfqScheduler : IScheduler
{
    /*
     * Level 0 = highest priority
     * Level 3 = lowest priority
     */
    private const int LevelCount = 4;

    /*
     * Cosmos currently uses a 10 ms scheduler tick.
     *
     * These are expressed in milliseconds:
     *
     * L0 = 10 ms
     * L1 = 20 ms
     * L2 = 40 ms
     * L3 = 80 ms
     */
    private static readonly ulong[] QuantumNs =
    {
        10_000_000,
        20_000_000,
        40_000_000,
        80_000_000
    };

    /*
     * Every N ticks perform the classic MLFQ
     * priority boost.
     *
     * 100 ticks × 10 ms = ~1 second.
     */
    private const ulong BoostIntervalTicks = 100;


    public string Name => "Windose MLFQ";


    // ------------------------------------------------------------
    // Per-thread state
    // ------------------------------------------------------------

    private sealed class ThreadData
    {
        public int Level;

        public ulong QuantumUsed;

        /*
         * Set when the thread consumed its complete quantum.
         *
         * The actual demotion happens when OnThreadYield()
         * receives the thread.
         */
        public bool QuantumExpired;

        /*
         * Used to detect whether the thread is currently
         * present in one of our queues.
         */
        public bool Queued;

        /*
         * Number of consecutive ticks spent running.
         *
         * Useful for diagnostics / tuning.
         */
        public ulong RuntimeTicks;
    }


    // ------------------------------------------------------------
    // Per-CPU state
    // ------------------------------------------------------------

    private sealed class CpuData
    {
        public readonly List<SchedulerThread>[] Queues =
        {
            new List<SchedulerThread>(16),
            new List<SchedulerThread>(16),
            new List<SchedulerThread>(16),
            new List<SchedulerThread>(16)
        };

        public ulong TickCount;
    }


    // ------------------------------------------------------------
    // CPU initialization
    // ------------------------------------------------------------

    public void InitializeCpu(PerCpuState cpuState)
    {
        cpuState.SchedulerData = new CpuData();
    }


    public void ShutdownCpu(PerCpuState cpuState)
    {
        cpuState.SchedulerData = null;
    }


    // ------------------------------------------------------------
    // Thread creation
    // ------------------------------------------------------------

    public void OnThreadCreate(
        PerCpuState cpuState,
        SchedulerThread thread)
    {
        /*
         * Every new thread starts at the highest level.
         */
        thread.SchedulerData = new ThreadData
        {
            Level = 0,
            QuantumUsed = 0,
            QuantumExpired = false,
            Queued = false,
            RuntimeTicks = 0
        };

        /*
         * Important:
         *
         * Do NOT queue it here.
         *
         * Cosmos calls OnThreadReady() when it becomes runnable.
         */
    }


    // ------------------------------------------------------------
    // Thread becomes runnable
    // ------------------------------------------------------------

    public void OnThreadReady(
        PerCpuState cpuState,
        SchedulerThread thread)
    {
        CpuData? cpu = cpuState.SchedulerData as CpuData;
        ThreadData? data = thread.SchedulerData as ThreadData;

        if (cpu is null || data is null)
            return;

        /*
         * The documentation specifically warns that this hook
         * can be called more than once for the same thread.
         */
        if (data.Queued)
            return;

        if (data.Level < 0)
            data.Level = 0;

        if (data.Level >= LevelCount)
            data.Level = LevelCount - 1;

        cpu.Queues[data.Level].Add(thread);

        data.Queued = true;
    }


    // ------------------------------------------------------------
    // Thread blocks / sleeps
    // ------------------------------------------------------------

    public void OnThreadBlocked(
        PerCpuState cpuState,
        SchedulerThread thread)
    {
        CpuData? cpu = cpuState.SchedulerData as CpuData;
        ThreadData? data = thread.SchedulerData as ThreadData;

        if (cpu is null || data is null)
            return;

        /*
         * A running thread normally isn't in the queue.
         *
         * A READY thread can still be in a queue when it gets
         * blocked, so remove it if necessary.
         */
        if (data.Queued)
        {
            RemoveThread(cpu, thread, data.Level);
            data.Queued = false;
        }

        /*
         * Blocking before consuming the complete quantum means
         * the thread behaved interactively.
         *
         * Promote it.
         */
        if (!data.QuantumExpired)
        {
            if (data.Level > 0)
                data.Level--;
        }

        /*
         * Start a fresh quantum after blocking.
         */
        data.QuantumUsed = 0;
        data.QuantumExpired = false;
    }


    // ------------------------------------------------------------
    // Thread yields / gets preempted
    // ------------------------------------------------------------

    public void OnThreadYield(
        PerCpuState cpuState,
        SchedulerThread thread)
    {
        CpuData? cpu = cpuState.SchedulerData as CpuData;
        ThreadData? data = thread.SchedulerData as ThreadData;

        if (cpu is null || data is null)
            return;

        /*
         * If the thread used its whole quantum,
         * demote it.
         */
        if (data.QuantumExpired)
        {
            if (data.Level < LevelCount - 1)
                data.Level++;
        }

        /*
         * Reset quantum accounting.
         */
        data.QuantumUsed = 0;
        data.QuantumExpired = false;

        /*
         * The thread is runnable again, so place it at the
         * tail of its current queue.
         */
        if (!data.Queued)
        {
            cpu.Queues[data.Level].Add(thread);
            data.Queued = true;
        }
    }


    // ------------------------------------------------------------
    // Pick next thread
    // ------------------------------------------------------------

    public SchedulerThread? PickNext(
        PerCpuState cpuState)
    {
        CpuData? cpu = cpuState.SchedulerData as CpuData;

        if (cpu is null)
            return null;

        /*
         * Highest priority queue first.
         */
        for (int level = 0; level < LevelCount; level++)
        {
            List<SchedulerThread> queue = cpu.Queues[level];

            if (queue.Count == 0)
                continue;

            /*
             * FIFO.
             */
            SchedulerThread thread = queue[0];

            queue.RemoveAt(0);

            ThreadData? data = thread.SchedulerData as ThreadData;

            if (data is not null)
                data.Queued = false;

            return thread;
        }

        /*
         * Cosmos will use the CPU idle thread.
         */
        return null;
    }


    // ------------------------------------------------------------
    // Timer tick
    // ------------------------------------------------------------

    public bool OnTick(
        PerCpuState cpuState,
        SchedulerThread current,
        ulong elapsedNs)
    {
        CpuData? cpu = cpuState.SchedulerData as CpuData;
        ThreadData? data = current.SchedulerData as ThreadData;

        if (cpu is null || data is null)
            return true;

        cpu.TickCount++;

        /*
         * Account this tick.
         */
        data.QuantumUsed += elapsedNs;
        data.RuntimeTicks++;

        /*
         * Classic MLFQ priority boost.
         */
        if (cpu.TickCount >= BoostIntervalTicks)
        {
            cpu.TickCount = 0;

            BoostAllThreads(cpu);

            /*
             * Also boost the currently running thread.
             */
            data.Level = 0;
        }

        /*
         * Quantum expired.
         */
        if (data.QuantumUsed >= QuantumNs[data.Level])
        {
            data.QuantumExpired = true;

            /*
             * Tell Cosmos to schedule another thread.
             */
            return true;
        }

        return false;
    }


    // ------------------------------------------------------------
    // Priority boost
    // ------------------------------------------------------------

    private static void BoostAllThreads(CpuData cpu)
    {
        /*
         * Move all queued threads to level 0.
         *
         * We iterate backwards because we're modifying
         * the queues while iterating.
         */
        for (int level = 1; level < LevelCount; level++)
        {
            List<SchedulerThread> source = cpu.Queues[level];
            List<SchedulerThread> destination = cpu.Queues[0];

            for (int i = source.Count - 1; i >= 0; i--)
            {
                SchedulerThread thread = source[i];

                source.RemoveAt(i);

                ThreadData? data =
                    thread.SchedulerData as ThreadData;

                if (data is null)
                    continue;

                data.Level = 0;
                data.QuantumUsed = 0;
                data.QuantumExpired = false;

                destination.Add(thread);
            }
        }
    }


    // ------------------------------------------------------------
    // Remove helper
    // ------------------------------------------------------------

    private static void RemoveThread(
        CpuData cpu,
        SchedulerThread thread,
        int level)
    {
        if (level < 0 || level >= LevelCount)
            return;

        List<SchedulerThread> queue = cpu.Queues[level];

        /*
         * Do NOT use:
         *
         * queue.Remove(thread)
         *
         * Cosmos explicitly warns against List.Remove(),
         * Contains(), and IndexOf() on scheduler paths.
         */
        for (int i = 0; i < queue.Count; i++)
        {
            if (ReferenceEquals(queue[i], thread))
            {
                queue.RemoveAt(i);
                return;
            }
        }
    }


    // ------------------------------------------------------------
    // Pick failure
    // ------------------------------------------------------------

    public void OnPickFailed(
        PerCpuState cpuState,
        SchedulerThread thread)
    {
        OnThreadReady(cpuState, thread);
    }


    // ------------------------------------------------------------
    // CPU selection
    // ------------------------------------------------------------

    public uint SelectCpu(
        SchedulerThread thread,
        uint currentCpu,
        uint cpuCount)
    {
        /*
         * SMP isn't currently active in the Cosmos scheduler
         * mechanism, so simply keep the thread on its CPU.
         */
        return currentCpu;
    }


    // ------------------------------------------------------------
    // Thread migration
    // ------------------------------------------------------------

    public void OnThreadMigrate(
        SchedulerThread thread,
        PerCpuState fromState,
        PerCpuState toState)
    {
        CpuData? from = fromState.SchedulerData as CpuData;
        CpuData? to = toState.SchedulerData as CpuData;
        ThreadData? data = thread.SchedulerData as ThreadData;

        if (from is null || to is null || data is null)
            return;

        if (data.Queued)
        {
            RemoveThread(from, thread, data.Level);

            to.Queues[data.Level].Add(thread);
        }
    }


    // ------------------------------------------------------------
    // Load balancing
    // ------------------------------------------------------------

    public void Balance(
        PerCpuState cpuState,
        PerCpuState[] allCpuStates)
    {
        /*
         * Cosmos currently runs the scheduler on one CPU,
         * so this is intentionally empty.
         *
         * This becomes important when SMP scheduling is enabled.
         */
    }


    // ------------------------------------------------------------
    // Priority
    // ------------------------------------------------------------

    public void SetPriority(
        PerCpuState cpuState,
        SchedulerThread thread,
        long priority)
    {
        /*
         * Priority is policy-defined.
         *
         * For MLFQ we interpret it as the starting/current
         * MLFQ level:
         *
         * 0 = highest
         * 3 = lowest
         */

        using var mask = SchedulerManager.MaskInterrupts();

        ThreadData? data = thread.SchedulerData as ThreadData;

        if (data is null)
            return;

        int newLevel = (int)priority;

        if (newLevel < 0)
            newLevel = 0;

        if (newLevel >= LevelCount)
            newLevel = LevelCount - 1;

        if (data.Level == newLevel)
            return;

        CpuData? cpu = cpuState.SchedulerData as CpuData;

        if (cpu is null)
            return;

        if (data.Queued)
        {
            RemoveThread(cpu, thread, data.Level);

            data.Level = newLevel;

            cpu.Queues[newLevel].Add(thread);
        }
        else
        {
            data.Level = newLevel;
        }
    }


    public long GetPriority(
        SchedulerThread thread)
    {
        ThreadData? data = thread.SchedulerData as ThreadData;

        if (data is null)
            return 0;

        return data.Level;
    }


    // ------------------------------------------------------------
    // Diagnostics
    // ------------------------------------------------------------

    public int GetRunQueueCount(
        PerCpuState cpuState)
    {
        using var mask = SchedulerManager.MaskInterrupts();

        CpuData? cpu = cpuState.SchedulerData as CpuData;

        if (cpu is null)
            return 0;

        int count = 0;

        for (int level = 0; level < LevelCount; level++)
            count += cpu.Queues[level].Count;

        return count;
    }


    public SchedulerThread? GetRunQueueThread(
        PerCpuState cpuState,
        int index)
    {
        using var mask = SchedulerManager.MaskInterrupts();

        CpuData? cpu = cpuState.SchedulerData as CpuData;

        if (cpu is null || index < 0)
            return null;

        int current = 0;

        for (int level = 0; level < LevelCount; level++)
        {
            List<SchedulerThread> queue = cpu.Queues[level];

            if (index < current + queue.Count)
                return queue[index - current];

            current += queue.Count;
        }

        return null;
    }

    public void OnThreadExit(
    PerCpuState cpuState,
    SchedulerThread thread)
    {
        CpuData? cpu = cpuState.SchedulerData as CpuData;
        ThreadData? data = thread.SchedulerData as ThreadData;

        if (cpu is null || data is null)
            return;

        // The thread may still be sitting in a run queue
        // when it exits.
        if (data.Queued)
        {
            RemoveThread(cpu, thread, data.Level);
            data.Queued = false;
        }

        // Release scheduler-specific state.
        thread.SchedulerData = null;
    }
}