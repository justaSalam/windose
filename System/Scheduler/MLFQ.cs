using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Cosmos.Kernel.Core.Scheduler
{
    public class MlfqThreadData
    {
        public int QueueIndex { get; set; }
        public ulong RemainingQuantumNs { get; set; }
        public bool IsBlocked { get; set; }
    }

    public class MlfqCpuData
    {
        public List<List<Cosmos.Kernel.Core.Scheduler.Thread>> Queues { get; } = new();
        public ulong NanosecondsSinceLastBoost { get; set; }

        public MlfqCpuData(int queueCount)
        {
            for (int i = 0; i < queueCount; i++)
            {
                Queues.Add(new List<Cosmos.Kernel.Core.Scheduler.Thread>());
            }
        }
    }

    public class MlfqScheduler : IScheduler
    {
        public string Name => "Multi-Level Feedback Queue (MLFQ) Scheduler";

        private readonly ulong[] _queueQuantaNs = { 5_000_000, 10_000_000, 20_000_000 };
        private const ulong PriorityBoostIntervalNs = 100_000_000;

        // Internal side-tables to track state safely without modifying the read-only SchedulerData fields
        private readonly Dictionary<PerCpuState, MlfqCpuData> _cpuRegistry = new();
        private readonly ConditionalWeakTable<Cosmos.Kernel.Core.Scheduler.Thread, MlfqThreadData> _threadRegistry = new();

        public void InitializeCpu(PerCpuState cpuState)
        {
            lock (_cpuRegistry)
            {
                _cpuRegistry[cpuState] = new MlfqCpuData(_queueQuantaNs.Length);
            }
        }

        public void ShutdownCpu(PerCpuState cpuState)
        {
            lock (_cpuRegistry)
            {
                if (_cpuRegistry.TryGetValue(cpuState, out var cpuData))
                {
                    foreach (var queue in cpuData.Queues)
                    {
                        queue.Clear();
                    }
                    _cpuRegistry.Remove(cpuState);
                }
            }
        }

        public void OnThreadCreate(PerCpuState cpuState, Cosmos.Kernel.Core.Scheduler.Thread thread)
        {
            var threadData = new MlfqThreadData
            {
                QueueIndex = 0,
                RemainingQuantumNs = _queueQuantaNs[0],
                IsBlocked = false
            };
            _threadRegistry.Add(thread, threadData);
        }

        public void OnThreadExit(PerCpuState cpuState, Cosmos.Kernel.Core.Scheduler.Thread thread)
        {
            lock (_cpuRegistry)
            {
                if (_cpuRegistry.TryGetValue(cpuState, out var cpuData))
                {
                    foreach (var queue in cpuData.Queues)
                    {
                        queue.Remove(thread);
                    }
                }
            }
            _threadRegistry.Remove(thread);
        }

        public void OnThreadReady(PerCpuState cpuState, Cosmos.Kernel.Core.Scheduler.Thread thread)
        {
            lock (_cpuRegistry)
            {
                if (_cpuRegistry.TryGetValue(cpuState, out var cpuData) && _threadRegistry.TryGetValue(thread, out var threadData))
                {
                    threadData.IsBlocked = false;
                    if (!cpuData.Queues[threadData.QueueIndex].Contains(thread))
                    {
                        cpuData.Queues[threadData.QueueIndex].Add(thread);
                    }
                }
            }
        }

        public void OnThreadBlocked(PerCpuState cpuState, Cosmos.Kernel.Core.Scheduler.Thread thread)
        {
            lock (_cpuRegistry)
            {
                if (_cpuRegistry.TryGetValue(cpuState, out var cpuData) && _threadRegistry.TryGetValue(thread, out var threadData))
                {
                    threadData.IsBlocked = true;
                    cpuData.Queues[threadData.QueueIndex].Remove(thread);
                    threadData.RemainingQuantumNs = _queueQuantaNs[threadData.QueueIndex];
                }
            }
        }

        public void OnThreadYield(PerCpuState cpuState, Cosmos.Kernel.Core.Scheduler.Thread thread)
        {
            lock (_cpuRegistry)
            {
                if (_cpuRegistry.TryGetValue(cpuState, out var cpuData) && _threadRegistry.TryGetValue(thread, out var threadData))
                {
                    cpuData.Queues[threadData.QueueIndex].Remove(thread);
                    threadData.RemainingQuantumNs = _queueQuantaNs[threadData.QueueIndex];
                    cpuData.Queues[threadData.QueueIndex].Add(thread);
                }
            }
        }

        public Cosmos.Kernel.Core.Scheduler.Thread? PickNext(PerCpuState cpuState)
        {
            lock (_cpuRegistry)
            {
                if (!_cpuRegistry.TryGetValue(cpuState, out var cpuData)) return null;

                for (int i = 0; i < cpuData.Queues.Count; i++)
                {
                    if (cpuData.Queues[i].Count > 0)
                    {
                        return cpuData.Queues[i][0];
                    }
                }
                return null;
            }
        }

        public void OnPickFailed(PerCpuState cpuState, Cosmos.Kernel.Core.Scheduler.Thread thread)
        {
            lock (_cpuRegistry)
            {
                if (_cpuRegistry.TryGetValue(cpuState, out var cpuData) && _threadRegistry.TryGetValue(thread, out var threadData))
                {
                    if (!cpuData.Queues[threadData.QueueIndex].Contains(thread))
                    {
                        cpuData.Queues[threadData.QueueIndex].Insert(0, thread);
                    }
                }
            }
        }

        public bool OnTick(PerCpuState cpuState, Cosmos.Kernel.Core.Scheduler.Thread current, ulong elapsedNs)
        {
            lock (_cpuRegistry)
            {
                if (!_cpuRegistry.TryGetValue(cpuState, out var cpuData)) return false;

                cpuData.NanosecondsSinceLastBoost += elapsedNs;
                if (cpuData.NanosecondsSinceLastBoost >= PriorityBoostIntervalNs)
                {
                    TriggerPriorityBoost(cpuData);
                    return true;
                }

                if (!_threadRegistry.TryGetValue(current, out var threadData) || threadData.IsBlocked) return false;

                if (threadData.RemainingQuantumNs > elapsedNs)
                {
                    threadData.RemainingQuantumNs -= elapsedNs;
                    return false;
                }

                cpuData.Queues[threadData.QueueIndex].Remove(current);

                if (threadData.QueueIndex < _queueQuantaNs.Length - 1)
                {
                    threadData.QueueIndex++;
                }

                threadData.RemainingQuantumNs = _queueQuantaNs[threadData.QueueIndex];
                cpuData.Queues[threadData.QueueIndex].Add(current);

                return true;
            }
        }

        private void TriggerPriorityBoost(MlfqCpuData cpuData)
        {
            cpuData.NanosecondsSinceLastBoost = 0;
            for (int i = 1; i < cpuData.Queues.Count; i++)
            {
                var queue = cpuData.Queues[i];
                for (int j = queue.Count - 1; j >= 0; j--)
                {
                    Cosmos.Kernel.Core.Scheduler.Thread t = queue[j];
                    if (_threadRegistry.TryGetValue(t, out var threadData))
                    {
                        threadData.QueueIndex = 0;
                        threadData.RemainingQuantumNs = _queueQuantaNs[0];
                        queue.RemoveAt(j);
                        cpuData.Queues[0].Add(t);
                    }
                }
            }
        }

        public void OnThreadMigrate(Cosmos.Kernel.Core.Scheduler.Thread thread, PerCpuState fromState, PerCpuState toState)
        {
            lock (_cpuRegistry)
            {
                if (_cpuRegistry.TryGetValue(fromState, out var fromCpu))
                {
                    foreach (var q in fromCpu.Queues) q.Remove(thread);
                }

                if (_cpuRegistry.TryGetValue(toState, out var toCpu) && _threadRegistry.TryGetValue(thread, out var threadData))
                {
                    threadData.RemainingQuantumNs = _queueQuantaNs[threadData.QueueIndex];
                    if (!threadData.IsBlocked)
                    {
                        toCpu.Queues[threadData.QueueIndex].Add(thread);
                    }
                }
            }
        }

        public uint SelectCpu(Cosmos.Kernel.Core.Scheduler.Thread thread, uint currentCpu, uint cpuCount)
        {
            return currentCpu;
        }

        public void Balance(PerCpuState cpuState, PerCpuState[] allCpuStates)
        {
            lock (_cpuRegistry)
            {
                if (!_cpuRegistry.ContainsKey(cpuState)) return;

                foreach (var externalCpu in allCpuStates)
                {
                    if (externalCpu.CpuId == cpuState.CpuId || !_cpuRegistry.TryGetValue(externalCpu, out var externalCpuData))
                        continue;

                    if (GetRunQueueCount(externalCpu) > GetRunQueueCount(cpuState) + 2)
                    {
                        for (int i = _queueQuantaNs.Length - 1; i >= 0; i--)
                        {
                            if (externalCpuData.Queues[i].Count > 0)
                            {
                                Cosmos.Kernel.Core.Scheduler.Thread targetThread = externalCpuData.Queues[i][0];
                                OnThreadMigrate(targetThread, externalCpu, cpuState);
                                return;
                            }
                        }
                    }
                }
            }
        }

        public long GetPriority(Cosmos.Kernel.Core.Scheduler.Thread thread)
        {
            return _threadRegistry.TryGetValue(thread, out var td) ? td.QueueIndex : -1;
        }

        public void SetPriority(PerCpuState cpuState, Cosmos.Kernel.Core.Scheduler.Thread thread, long priority) { lock (_cpuRegistry) { if (_threadRegistry.TryGetValue(thread, out var threadData) && _cpuRegistry.TryGetValue(cpuState, out var cpuData)) { int targetQueue = (int)Math.Clamp(priority, 0, _queueQuantaNs.Length - 1); cpuData.Queues[threadData.QueueIndex].Remove(thread); threadData.QueueIndex = targetQueue; threadData.RemainingQuantumNs = _queueQuantaNs[targetQueue]; if (!threadData.IsBlocked) { cpuData.Queues[targetQueue].Add(thread); } } } }
        public int GetRunQueueCount(PerCpuState cpuState) { lock (_cpuRegistry) { if (!_cpuRegistry.TryGetValue(cpuState, out var cpuData)) return 0; int count = 0; for (int i = 0; i < cpuData.Queues.Count; i++) count += cpuData.Queues[i].Count; return count; } }
        public Cosmos.Kernel.Core.Scheduler.Thread? GetRunQueueThread(PerCpuState cpuState, int index) { lock (_cpuRegistry) { if (!_cpuRegistry.TryGetValue(cpuState, out var cpuData) || index < 0) return null; int currentIndex = 0; for (int i = 0; i < cpuData.Queues.Count; i++) { if (index < currentIndex + cpuData.Queues[i].Count) { return cpuData.Queues[i][index - currentIndex]; } currentIndex += cpuData.Queues[i].Count; } return null; } }
    }
}