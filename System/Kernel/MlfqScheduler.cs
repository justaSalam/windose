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
     * Per-level queue capacity at CPU init. OnThreadCreate grows the
     * queues so that every queue can hold every thread on the CPU, which
     * means the tick / pick / ready paths never allocate.
     */
    private const int InitialQueueCapacity = 64;

    /*
     * Allotment per level. A thread that accumulates this much runtime
     * while staying on the level is demoted.
     *
     * L0 = 10 ms, L1 = 20 ms, L2 = 40 ms, L3 = 80 ms
     */
    private static readonly ulong[] QuantumNs =
    {
        10_000_000,
        20_000_000,
        40_000_000,
        80_000_000
    };

    /*
     * Classic MLFQ priority boost, driven by real elapsed time
     * (not by a tick counter) so it can't drift when idle ticks occur.
     */
    private const ulong BoostIntervalNs = 1_000_000_000;


    public string Name => "Multilevel Feedback Queue";


    // ------------------------------------------------------------
    // Allocation-free ring-buffer run queue
    // ------------------------------------------------------------

    private sealed class RunQueue
    {
        private SchedulerThread[] _items;
        private int _head;
        private int _count;

        public RunQueue(int capacity)
        {
            _items = new SchedulerThread[capacity];
        }

        public int Count => _count;

        public SchedulerThread this[int index] => _items[Wrap(_head + index)];

        // Valid for i in [0, 2 * length).
        private int Wrap(int i)
        {
            int len = _items.Length;
            return i >= len ? i - len : i;
        }

        public void EnqueueTail(SchedulerThread thread)
        {
            if (_count == _items.Length)
                Grow(_items.Length * 2);

            _items[Wrap(_head + _count)] = thread;
            _count++;
        }

        public void EnqueueHead(SchedulerThread thread)
        {
            if (_count == _items.Length)
                Grow(_items.Length * 2);

            _head = _head == 0 ? _items.Length - 1 : _head - 1;
            _items[_head] = thread;
            _count++;
        }

        public SchedulerThread DequeueHead()
        {
            SchedulerThread thread = _items[_head];

            _items[_head] = null!;
            _head = Wrap(_head + 1);
            _count--;

            return thread;
        }

        public bool Remove(SchedulerThread thread)
        {
            /*
             * Reference comparison on purpose. Cosmos warns against
             * List.Remove / Contains / IndexOf on scheduler paths.
             */
            for (int i = 0; i < _count; i++)
            {
                if (!ReferenceEquals(_items[Wrap(_head + i)], thread))
                    continue;

                // Shift the tail down by one, preserving order.
                for (int j = i; j < _count - 1; j++)
                    _items[Wrap(_head + j)] = _items[Wrap(_head + j + 1)];

                _items[Wrap(_head + _count - 1)] = null!;
                _count--;

                return true;
            }

            return false;
        }

        public void Clear()
        {
            for (int i = 0; i < _count; i++)
                _items[Wrap(_head + i)] = null!;

            _head = 0;
            _count = 0;
        }

        public void EnsureCapacity(int capacity)
        {
            if (capacity > _items.Length)
                Grow(capacity);
        }

        private void Grow(int newCapacity)
        {
            SchedulerThread[] newItems = new SchedulerThread[newCapacity];

            for (int i = 0; i < _count; i++)
                newItems[i] = _items[Wrap(_head + i)];

            _items = newItems;
            _head = 0;
        }
    }


    // ------------------------------------------------------------
    // Per-thread state
    // ------------------------------------------------------------

    private sealed class ThreadData
    {
        public int Level;

        /*
         * Lowest level (numerically highest) this thread may be
         * demoted to. Default is the bottom level. SetPriority()
         * lowers it to pin a thread (e.g. the focused window).
         */
        public int Floor = LevelCount - 1;

        /*
         * Runtime accumulated on the current level. Only reset when
         * the level changes, so yielding can't dodge demotion.
         */
        public ulong QuantumUsed;

        /*
         * Set by OnTick when the allotment ran out.
         * The demotion itself is applied in OnThreadYield/OnThreadBlocked.
         */
        public bool QuantumExpired;

        /*
         * Set by OnTick when the thread is being kicked off the CPU
         * because a higher level has runnable threads. A preempted
         * thread keeps its turn: it goes back to the head of its queue.
         */
        public bool Preempted;

        /*
         * True from PickNext until the first tick afterwards.
         * That tick only covers part of an interval, see OnTick.
         */
        public bool JustPicked;

        /*
         * Present in one of our queues.
         */
        public bool Queued;

        /*
         * Ticks spent running, for diagnostics / tuning.
         */
        public ulong RuntimeTicks;
    }


    // ------------------------------------------------------------
    // Per-CPU state
    // ------------------------------------------------------------

    private sealed class CpuData
    {
        public readonly RunQueue[] Queues = new RunQueue[LevelCount];

        public ulong BoostTimerNs;

        public int ThreadCount;

        public CpuData()
        {
            for (int i = 0; i < LevelCount; i++)
                Queues[i] = new RunQueue(InitialQueueCapacity);
        }

        public void EnsureCapacity(int capacity)
        {
            for (int i = 0; i < LevelCount; i++)
                Queues[i].EnsureCapacity(capacity);
        }
    }


    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------

    private static void Demote(ThreadData data)
    {
        if (data.Level < data.Floor)
            data.Level++;

        data.QuantumUsed = 0;
        data.QuantumExpired = false;
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
            Floor = LevelCount - 1,
            QuantumUsed = 0,
            QuantumExpired = false,
            Preempted = false,
            JustPicked = false,
            Queued = false,
            RuntimeTicks = 0
        };

        /*
         * Grow every queue now, in creation context, so it can hold
         * every thread. This keeps allocation out of the interrupt
         * paths (OnTick / OnThreadReady / OnThreadYield).
         */
        CpuData? cpu = cpuState.SchedulerData as CpuData;

        if (cpu is not null)
        {
            cpu.ThreadCount++;
            cpu.EnsureCapacity(cpu.ThreadCount);
        }

        /*
         * Do NOT queue it here.
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
         * This hook can be called more than once for the same thread.
         */
        if (data.Queued)
            return;

        if (data.Level < 0)
            data.Level = 0;

        if (data.Level >= LevelCount)
            data.Level = LevelCount - 1;

        cpu.Queues[data.Level].EnqueueTail(thread);

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
         * A READY thread can still be in a queue when it gets blocked.
         */
        if (data.Queued)
        {
            cpu.Queues[data.Level].Remove(thread);
            data.Queued = false;
        }

        if (data.QuantumExpired)
        {
            /*
             * Used its whole allotment before blocking: demote.
             */
            Demote(data);
        }
        else if (data.Level > 0 &&
                 data.QuantumUsed < QuantumNs[data.Level] / 2)
        {
            /*
             * Blocked after using less than half its allotment:
             * clearly interactive, promote. Requiring "less than half"
             * stops a CPU-bound thread from climbing by sleeping 1 ms
             * every cycle.
             */
            data.Level--;
            data.QuantumUsed = 0;
        }

        /*
         * Otherwise keep the accumulated usage; the allotment is per
         * level, not per scheduling round.
         */
        data.Preempted = false;
        data.JustPicked = false;
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
         * Only a fully used allotment demotes. A voluntary yield or a
         * preemption keeps QuantumUsed, so yielding at 90% of the
         * quantum can't be used to stay on a high level forever.
         */
        if (data.QuantumExpired)
            Demote(data);

        bool preempted = data.Preempted;

        data.Preempted = false;
        data.JustPicked = false;

        if (!data.Queued)
        {
            if (preempted)
                cpu.Queues[data.Level].EnqueueHead(thread);
            else
                cpu.Queues[data.Level].EnqueueTail(thread);

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
         * Highest priority queue first, FIFO within a level.
         */
        for (int level = 0; level < LevelCount; level++)
        {
            RunQueue queue = cpu.Queues[level];

            if (queue.Count == 0)
                continue;

            SchedulerThread thread = queue.DequeueHead();

            ThreadData? data = thread.SchedulerData as ThreadData;

            if (data is not null)
            {
                data.Queued = false;
                data.JustPicked = true;
                data.Preempted = false;
            }

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

        if (cpu is null)
            return true;

        ThreadData? data = current.SchedulerData as ThreadData;

        /*
         * Boost timer runs on real elapsed time and ticks even while
         * the idle thread is current (data == null).
         */
        cpu.BoostTimerNs += elapsedNs;

        bool boost = false;

        if (cpu.BoostTimerNs >= BoostIntervalNs)
        {
            cpu.BoostTimerNs -= BoostIntervalNs;

            // Don't try to catch up on a long stall.
            if (cpu.BoostTimerNs >= BoostIntervalNs)
                cpu.BoostTimerNs = 0;

            BoostAllThreads(cpu);
            boost = true;
        }

        /*
         * Idle thread (or foreign thread): always reschedule so
         * newly runnable threads get picked up.
         */
        if (data is null)
            return true;

        if (boost)
        {
            /*
             * The running thread is boosted too.
             */
            data.Level = 0;
            data.QuantumUsed = 0;
            data.QuantumExpired = false;
        }

        /*
         * Charge runtime.
         *
         * The first tick after a pick only covers a random part of the
         * interval, so charging a full tick would overbill short-running
         * threads. Charging half is unbiased on average. Later ticks are
         * full intervals.
         */
        ulong charge = data.JustPicked ? elapsedNs / 2 : elapsedNs;

        data.JustPicked = false;
        data.QuantumUsed += charge;
        data.RuntimeTicks++;

        /*
         * Allotment used up: reschedule. The demotion is applied in
         * OnThreadYield / OnThreadBlocked.
         */
        if (data.QuantumUsed >= QuantumNs[data.Level])
        {
            data.QuantumExpired = true;
            return true;
        }

        /*
         * Preempt if any higher level has a runnable thread. Without
         * this, a thread woken into L0 would wait behind an L3 hog for
         * its whole quantum.
         */
        for (int level = 0; level < data.Level; level++)
        {
            if (cpu.Queues[level].Count > 0)
            {
                data.Preempted = true;
                return true;
            }
        }

        return false;
    }


    // ------------------------------------------------------------
    // Priority boost
    // ------------------------------------------------------------

    private static void BoostAllThreads(CpuData cpu)
    {
        RunQueue destination = cpu.Queues[0];

        /*
         * Drain front to back so relative order is preserved and
         * boosted threads queue behind the existing L0 threads.
         */
        for (int level = 1; level < LevelCount; level++)
        {
            RunQueue source = cpu.Queues[level];

            int count = source.Count;

            for (int i = 0; i < count; i++)
            {
                SchedulerThread thread = source[i];

                ThreadData? data = thread.SchedulerData as ThreadData;

                if (data is null)
                    continue;

                data.Level = 0;
                data.QuantumUsed = 0;
                data.QuantumExpired = false;

                destination.EnqueueTail(thread);
            }

            source.Clear();
        }
    }


    // ------------------------------------------------------------
    // Pick failure
    // ------------------------------------------------------------

    public void OnPickFailed(
        PerCpuState cpuState,
        SchedulerThread thread)
    {
        CpuData? cpu = cpuState.SchedulerData as CpuData;
        ThreadData? data = thread.SchedulerData as ThreadData;

        if (cpu is null || data is null)
            return;

        if (data.Queued)
            return;

        /*
         * PickNext took it from the head, so it goes back to the head.
         */
        data.JustPicked = false;

        cpu.Queues[data.Level].EnqueueHead(thread);
        data.Queued = true;
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
         * SMP isn't active in the Cosmos scheduler mechanism.
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

        from.ThreadCount--;
        to.ThreadCount++;
        to.EnsureCapacity(to.ThreadCount);

        if (data.Queued)
        {
            from.Queues[data.Level].Remove(thread);
            to.Queues[data.Level].EnqueueTail(thread);
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
         * Intentionally empty while the scheduler runs on one CPU.
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
         * Priority = MLFQ level, 0 = highest, 3 = lowest.
         *
         * This sets both the current level and the demotion floor:
         * the thread starts at that level and is never demoted below
         * it. Pinning to 0 means a CPU-bound thread there starves the
         * lower levels (the boost doesn't help, it's already at 0), so
         * pin the focused window to 1 unless you really want that.
         */
        using var mask = SchedulerManager.MaskInterrupts();

        CpuData? cpu = cpuState.SchedulerData as CpuData;
        ThreadData? data = thread.SchedulerData as ThreadData;

        if (cpu is null || data is null)
            return;

        int newLevel = (int)priority;

        if (newLevel < 0)
            newLevel = 0;

        if (newLevel >= LevelCount)
            newLevel = LevelCount - 1;

        data.Floor = newLevel;

        if (data.Level == newLevel)
            return;

        if (data.Queued)
        {
            cpu.Queues[data.Level].Remove(thread);

            data.Level = newLevel;

            cpu.Queues[newLevel].EnqueueTail(thread);
        }
        else
        {
            data.Level = newLevel;
        }

        data.QuantumUsed = 0;
        data.QuantumExpired = false;
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
            RunQueue queue = cpu.Queues[level];

            if (index < current + queue.Count)
                return queue[index - current];

            current += queue.Count;
        }

        return null;
    }


    // ------------------------------------------------------------
    // Thread exit
    // ------------------------------------------------------------

    public void OnThreadExit(
        PerCpuState cpuState,
        SchedulerThread thread)
    {
        CpuData? cpu = cpuState.SchedulerData as CpuData;
        ThreadData? data = thread.SchedulerData as ThreadData;

        if (cpu is null || data is null)
            return;

        // The thread may still be sitting in a run queue when it exits.
        if (data.Queued)
        {
            cpu.Queues[data.Level].Remove(thread);
            data.Queued = false;
        }

        cpu.ThreadCount--;

        // Release scheduler-specific state.
        thread.SchedulerData = null;
    }
}