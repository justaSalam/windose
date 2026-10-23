/// ElfProgram.cs - one running (or finished) ELF program: its image, handles, events, memory and thread.
using System.Runtime.InteropServices;

namespace Windose.System.Elf
{
    public enum ElfState { Starting, Running, Exited }

    public sealed unsafe class ElfProgram
    {
        public readonly int Pid;
        public readonly string Name;
        public readonly string[] Args;          // Args[0] is the program name (argv[0])
        public readonly int StartedAtTicks;

        public int ExitCode;
        public ElfState State;
        /// <summary>Set by Kill(). Every API call then returns K_KILLED and the C library exits with 137.</summary>
        public volatile bool KillRequested;
        /// <summary>Free slot for the host (WindoseBridge keeps its process-list entry here).</summary>
        public object HostData;
        /// <summary>Text payload of the last drag-and-drop onto this program's windows.</summary>
        public string LastDropText;

        internal readonly HandleTable Objs = new HandleTable();
        internal readonly ElfEventQueue Events = new ElfEventQueue();
        internal readonly Dictionary<string, string> Env = new Dictionary<string, string>(StringComparer.Ordinal);

        // image: byte[] pinned for the whole life of the program; ImageAddress is 4 KB aligned inside it
        internal byte[] Image;
        internal GCHandle ImageHandle;
        internal ulong ImageAddress;
        internal ulong ImageSize;
        internal ulong EntryAddress;
        internal ElfFile Elf;
        internal ulong Low;

        // initializers / finalizers / dependencies
        internal ulong InitFunc;
        internal ulong FiniFunc;
        internal ulong InitArray;
        internal ulong InitArraySize;
        internal ulong FiniArray;
        internal ulong FiniArraySize;
        internal ulong PreinitArray;
        internal ulong PreinitArraySize;
        internal string[] NeededLibraries;

        // memory handed out through the alloc API (pinned arrays), freed after the thread has ended
        private readonly List<GCHandle> _pins = new List<GCHandle>();
        private long _pinnedBytes;

        private readonly ManualResetEvent _done = new ManualResetEvent(false);
        private readonly object _stateLock = new object();

        internal ElfProgram(int pid, string name, string[] args)
        {
            Pid = pid; Name = name; Args = args; State = ElfState.Starting;
            StartedAtTicks = Environment.TickCount;
        }

        /// <summary>Image + allocations currently owned by the program.</summary>
        public long MemoryBytes { get { lock (_pins) return (long)ImageSize + _pinnedBytes; } }

        /// <summary>Queue an event for the program (safe from any thread, ignored after exit).</summary>
        public void PostEvent(int type, int handle, int a, int b)
        {
            if (State == ElfState.Exited) return;
            Events.Push(type, handle, a, b);
        }

        /// <summary>Block until the program has exited. False on timeout.</summary>
        public bool Wait(int timeoutMs) { return _done.WaitOne(timeoutMs); }
        public bool Wait() { return _done.WaitOne(); }

        // ---------------------------------------------------------------- memory

        internal byte* Alloc(int size)
        {
            if (size <= 0 || size > ElfRunner.MaxSingleAlloc) return null;
            lock (_pins)
            {
                if ((long)ImageSize + _pinnedBytes + size > ElfRunner.MemoryQuota) return null;
                byte[] arr;
                try { arr = new byte[size]; } catch { return null; }
                GCHandle h = GCHandle.Alloc(arr, GCHandleType.Pinned);
                _pins.Add(h);
                _pinnedBytes += size;
                return (byte*)h.AddrOfPinnedObject();
            }
        }

        private void FreeMemory()
        {
            lock (_pins)
            {
                for (int i = 0; i < _pins.Count; i++) if (_pins[i].IsAllocated) _pins[i].Free();
                _pins.Clear();
                _pinnedBytes = 0;
            }
            if (ImageHandle.IsAllocated) ImageHandle.Free();
            Image = null;
        }

        // ---------------------------------------------------------------- lifecycle

        internal void CloseAllObjects()
        {
            object[] left = Objs.Drain();
            IElfHost host = ElfRunner.Host;
            if (host == null) return;
            for (int i = 0; i < left.Length; i++)
            {
                try { host.Close(left[i]); } catch { }
            }
        }

        /// <summary>Cooperative kill: windows close immediately, the program stops at its next API call.</summary>
        internal void RequestKill()
        {
            KillRequested = true;
            CloseAllObjects();
        }

        /// <summary>Finds runtime address of an exported symbol in the program image, or 0 if not found.</summary>
        public ulong FindSymbolAddress(string name)
        {
            if (Elf == null) return 0;
            ElfSymbol sym = Elf.FindSymbol(name);
            if (sym == null || sym.SectionIndex == 0) return 0;
            return sym.SectionIndex == 0xFFF1 ? sym.Value : unchecked(ImageAddress + (sym.Value - Low));
        }

        /// <summary>Thread entry. Runs the program, then releases everything it owned.</summary>
        internal void ThreadMain()
        {
            try
            {
                State = ElfState.Running;
                KernelApi api = default(KernelApi);
                ElfRunner.FillApi(ref api, Pid);

                // Run pre-initializers
                RunFuncArray(PreinitArray, PreinitArraySize, false);

                // Run DT_INIT
                if (InitFunc != 0)
                {
                    try { ((delegate* unmanaged<void>)(byte*)InitFunc)(); }
                    catch { }
                }

                // Run DT_INIT_ARRAY constructors
                RunFuncArray(InitArray, InitArraySize, false);

                var entry = (delegate* unmanaged<KernelApi*, int>)(byte*)EntryAddress;
                ExitCode = entry(&api);
            }
            finally
            {
                // Run DT_FINI_ARRAY destructors in reverse order
                RunFuncArray(FiniArray, FiniArraySize, true);

                // Run DT_FINI
                if (FiniFunc != 0)
                {
                    try { ((delegate* unmanaged<void>)(byte*)FiniFunc)(); }
                    catch { }
                }

                Cleanup();
            }
        }

        private static void RunFuncArray(ulong arrayAddr, ulong sizeInBytes, bool reverse)
        {
            if (arrayAddr == 0 || sizeInBytes == 0) return;
            ulong count = sizeInBytes / 8;
            if (count > 4096) count = 4096;
            var ptrs = (ulong*)(byte*)arrayAddr;
            if (!reverse)
            {
                for (ulong i = 0; i < count; i++)
                {
                    ulong fn = ptrs[i];
                    if (fn != 0 && fn != ulong.MaxValue)
                    {
                        try { ((delegate* unmanaged<void>)(byte*)fn)(); }
                        catch { }
                    }
                }
            }
            else
            {
                for (long i = (long)count - 1; i >= 0; i--)
                {
                    ulong fn = ptrs[i];
                    if (fn != 0 && fn != ulong.MaxValue)
                    {
                        try { ((delegate* unmanaged<void>)(byte*)fn)(); }
                        catch { }
                    }
                }
            }
        }

        private void Cleanup()
        {
            CloseAllObjects();
            FreeMemory();                       // safe: the program thread is done
            lock (_stateLock) State = ElfState.Exited;
            ElfRunner.RecordExit(Pid, ExitCode);
            ElfRunner.Unregister(this);
            try { if (ElfRunner.Host != null) ElfRunner.Host.ProgramExited(this); } catch { }
            try { Action<ElfProgram> cb = ElfRunner.Exited; if (cb != null) cb(this); } catch { }
            _done.Set();
        }

        internal void AbortStart()
        {
            // Called when Spawn failed after the image was pinned but before the thread started.
            FreeMemory();
            State = ElfState.Exited;
            ElfRunner.RecordExit(Pid, -1);
            _done.Set();
        }
    }
}
