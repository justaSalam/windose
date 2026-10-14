// ElfRunner.cs - loads x86-64 ELF programs (static or PIE), runs each on its own thread with its own
// handle table / event queue / memory quota, and serves the kernel API. Ring 0, no isolation.
// Needs AllowUnsafeBlocks. Everything OS-specific goes through IElfHost (see WindoseBridge.cs).
//
// KernelApi below MUST match KernelApi in programs/include/kernel.h: same fields, same order.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Windose.System.Elf
{
    public unsafe struct KernelApi
    {
        public int Version;
        public int Size;
        public int Pid;
        public int Reserved;
        // every call takes the caller's pid first
        public delegate* unmanaged<int, byte*, int, void> Write;                          // (pid, text, len)
        public delegate* unmanaged<int, int, int, byte*, int> CreateWindow;               // (pid, w, h, title)
        public delegate* unmanaged<int, int, int> Close;                                  // (pid, handle)
        public delegate* unmanaged<int, int, int> Sleep;                                  // (pid, ms)
        public delegate* unmanaged<int, int, int, int, int, int, byte*, int> CreateButton;// (pid, parent, x, y, w, h, text)
        public delegate* unmanaged<int, int, int, int, byte*, int> CreateLabel;           // (pid, parent, x, y, text)
        public delegate* unmanaged<int, int, byte*, int> SetText;                         // (pid, obj, text)
        public delegate* unmanaged<int, int, int, int, int, int, uint, int> FillRect;     // (pid, win, x, y, w, h, argb)
        public delegate* unmanaged<int, KEvent*, int> PollEvent;                          // (pid, Event*)
        public delegate* unmanaged<int, byte*, byte*, int, int> ReadFile;                 // (pid, path, buf, max)
        public delegate* unmanaged<int, byte*, byte*, int, int> WriteFile;                // (pid, path, buf, len)
        public delegate* unmanaged<int, int, byte*> Alloc;                                // (pid, size)
        public delegate* unmanaged<int, byte*, int, int> GetArgs;                         // (pid, buf, max)
        public delegate* unmanaged<int, int> Ticks;                                       // (pid)
    }

    public static unsafe class ElfRunner
    {
        public const int ApiVersion = 2;
        private const int KKilled = -100;
        private const int MaxStr = 260;
        private const int MaxIo = 1024 * 1024;
        private const int MaxObjectsPerProgram = 256;

        // ---------------------------------------------------------------- configuration (set at boot)

        /// <summary>Receives everything a program prints.</summary>
        public static Action<ElfProgram, string> Output;
        /// <summary>Windows/files/process list. Without it window and file calls fail.</summary>
        public static IElfHost Host;
        /// <summary>Optional: (address, length) of a freshly loaded image; mark it executable here if your heap isn't.</summary>
        public static Action<ulong, ulong> MakeExecutable;
        /// <summary>Called on the program's thread after it exited and everything was released.</summary>
        public static Action<ElfProgram> Exited;

        public static long MemoryQuota = 64L * 1024 * 1024;    // image + malloc memory per program
        public static int MaxSingleAlloc = 16 * 1024 * 1024;
        public static ulong MaxImageSize = 16UL * 1024 * 1024;
        public static int MaxPrograms = 32;
        public static int ThreadStackBytes = 512 * 1024;

        // ---------------------------------------------------------------- program table

        private static readonly Dictionary<int, ElfProgram> Programs = new Dictionary<int, ElfProgram>();
        private static int _nextPid = 1;

        public static int Count { get { lock (Programs) return Programs.Count; } }

        public static ElfProgram Get(int pid)
        {
            lock (Programs) { ElfProgram p; return Programs.TryGetValue(pid, out p) ? p : null; }
        }

        public static ElfProgram[] List()
        {
            lock (Programs)
            {
                ElfProgram[] r = new ElfProgram[Programs.Count];
                Programs.Values.CopyTo(r, 0);
                return r;
            }
        }

        internal static void Unregister(ElfProgram p) { lock (Programs) Programs.Remove(p.Pid); }

        /// <summary>Cooperative kill. Windows close at once; the program stops at its next API call
        /// (sleep and poll_event return within ~10 ms). A program stuck in a pure compute loop cannot be stopped.</summary>
        public static bool Kill(int pid)
        {
            ElfProgram p = Get(pid);
            if (p == null) return false;
            p.RequestKill();
            return true;
        }

        // ---------------------------------------------------------------- start / run

        /// <summary>Loads and starts a program on its own thread. Returns null on failure (reason in 'error').
        /// 'args' are the arguments after the program name.</summary>
        public static ElfProgram Spawn(byte[] file, string name, string[] args, out string error)
        {
            ElfFile elf = ElfFile.Parse(file, out error);
            if (elf == null) return null;

            if (!elf.Is64 || !elf.LittleEndian || elf.Machine != ElfConst.EM_X86_64)
            { error = "need x86-64 little-endian ELF64"; return null; }
            if (elf.Type != ElfConst.ET_EXEC && elf.Type != ElfConst.ET_DYN)
            { error = "need an executable (ET_EXEC or PIE)"; return null; }
            if (elf.GetInterpreter() != null)
            { error = "dynamically linked executables are not supported"; return null; }

            ulong low, size;
            if (!elf.GetImageExtent(MaxImageSize, out low, out size)) { error = elf.Error; return null; }
            if (elf.Entry < low || elf.Entry - low >= size) { error = "entry point outside image"; return null; }
            if (elf.Type == ElfConst.ET_EXEC && low != 0)
            { error = "ET_EXEC linked at a fixed address; link as PIE (see programs/build.sh)"; return null; }
            if ((long)size > MemoryQuota) { error = "image larger than the memory quota"; return null; }

            string[] full = new string[(args == null ? 0 : args.Length) + 1];
            full[0] = name;
            for (int i = 1; i < full.Length; i++) full[i] = args[i - 1];

            ElfProgram p;
            lock (Programs)
            {
                if (Programs.Count >= MaxPrograms) { error = "too many running programs"; return null; }
                p = new ElfProgram(_nextPid++, name, full);
                Programs[p.Pid] = p;
            }

            try
            {
                // 4 KB slack so the image can start on a page boundary (PIE data may assume it)
                byte[] backing = new byte[(int)size + 4096];
                p.Image = backing;
                p.ImageHandle = GCHandle.Alloc(backing, GCHandleType.Pinned);
                ulong addr = (ulong)(byte*)p.ImageHandle.AddrOfPinnedObject();
                ulong aligned = (addr + 4095) & ~4095UL;
                int offset = (int)(aligned - addr);

                elf.CopyImage(backing, offset, low);
                ulong bias = unchecked(aligned - low);
                if (!elf.ApplyRelocations(backing, offset, low, size, bias, out error))
                { Fail(p); return null; }

                p.ImageAddress = aligned;
                p.ImageSize = size;
                p.EntryAddress = unchecked(bias + elf.Entry);

                if (MakeExecutable != null) MakeExecutable(aligned, size);
                if (Host != null) Host.ProgramStarted(p);

                Thread t = new Thread(p.ThreadMain, ThreadStackBytes);
                t.Start();
                error = null;
                return p;
            }
            catch (Exception ex)
            {
                error = "failed to start: " + ex.Message;
                Fail(p);
                return null;
            }
        }

        private static void Fail(ElfProgram p)
        {
            p.AbortStart();
            Unregister(p);
        }

        /// <summary>Starts a program and waits for it. Returns false only if it could not be started.</summary>
        public static bool Run(byte[] file, string name, string[] args, out int exitCode, out string error)
        {
            exitCode = -1;
            ElfProgram p = Spawn(file, name, args, out error);
            if (p == null) return false;
            p.Wait();
            exitCode = p.ExitCode;
            return true;
        }

        internal static void FillApi(ref KernelApi api, int pid)
        {
            api.Version = ApiVersion;
            api.Size = sizeof(KernelApi);
            api.Pid = pid;
            api.Reserved = 0;
            api.Write = &ApiWrite;
            api.CreateWindow = &ApiCreateWindow;
            api.Close = &ApiClose;
            api.Sleep = &ApiSleep;
            api.CreateButton = &ApiCreateButton;
            api.CreateLabel = &ApiCreateLabel;
            api.SetText = &ApiSetText;
            api.FillRect = &ApiFillRect;
            api.PollEvent = &ApiPollEvent;
            api.ReadFile = &ApiReadFile;
            api.WriteFile = &ApiWriteFile;
            api.Alloc = &ApiAlloc;
            api.GetArgs = &ApiGetArgs;
            api.Ticks = &ApiTicks;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>The live program for this pid, or null if it is gone or was killed.</summary>
        private static ElfProgram Live(int pid)
        {
            ElfProgram p = Get(pid);
            if (p == null || p.KillRequested) return null;
            return p;
        }

        private static string CStr(byte* s)
        {
            if (s == null) return "";
            int n = 0;
            while (n < MaxStr && s[n] != 0) n++;
            char[] c = new char[n];
            for (int i = 0; i < n; i++) c[i] = (char)s[i];
            return new string(c);
        }

        // ---------------------------------------------------------------- API handlers
        // Rules: never let an exception escape, never trust a pointer/length, return codes only.
        // 0 = ok, negative = error, KKilled = program was killed; create_* return a handle (0 = failed).

        [UnmanagedCallersOnly]
        private static void ApiWrite(int pid, byte* text, int len)
        {
            try
            {
                ElfProgram p = Live(pid);
                Action<ElfProgram, string> output = Output;
                if (p == null || output == null || text == null || len <= 0 || len > 4096) return;
                char[] c = new char[len];
                for (int i = 0; i < len; i++) c[i] = (char)text[i];
                output(p, new string(c));
            }
            catch { }
        }

        [UnmanagedCallersOnly]
        private static int ApiCreateWindow(int pid, int w, int h, byte* title)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                IElfHost host = Host;
                if (host == null || w < 32 || h < 32 || w > 4096 || h > 4096 || p.Objs.Count >= MaxObjectsPerProgram) return 0;
                object win = host.CreateWindow(w, h, CStr(title));
                if (win == null) return 0;
                int handle = p.Objs.Add(win);
                if (handle == 0) { host.Close(win); return 0; }
                host.Bind(win, handle, p);
                return handle;
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiClose(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                object o = p.Objs.Remove(handle);
                if (o == null || Host == null) return -1;
                Host.Close(o);
                return 0;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiSleep(int pid, int ms)
        {
            try
            {
                ElfProgram p = Get(pid);
                if (p == null) return KKilled;
                if (ms < 0) ms = 0;
                if (ms > 60000) ms = 60000;
                while (ms > 0)
                {
                    if (p.KillRequested) return KKilled;
                    int slice = ms < 10 ? ms : 10;       // sliced so a kill takes effect within ~10 ms
                    Thread.Sleep(slice);
                    ms -= slice;
                }
                return p.KillRequested ? KKilled : 0;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiCreateButton(int pid, int parent, int x, int y, int w, int h, byte* text)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                IElfHost host = Host;
                if (host == null || p.Objs.Count >= MaxObjectsPerProgram) return 0;
                object par = p.Objs.Get(parent);
                if (par == null) return 0;
                object b = host.CreateButton(par, x, y, w, h, CStr(text));
                if (b == null) return 0;
                int handle = p.Objs.Add(b);
                if (handle == 0) { host.Close(b); return 0; }
                host.Bind(b, handle, p);
                return handle;
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiCreateLabel(int pid, int parent, int x, int y, byte* text)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                IElfHost host = Host;
                if (host == null || p.Objs.Count >= MaxObjectsPerProgram) return 0;
                object par = p.Objs.Get(parent);
                if (par == null) return 0;
                object l = host.CreateLabel(par, x, y, CStr(text));
                if (l == null) return 0;
                int handle = p.Objs.Add(l);
                if (handle == 0) { host.Close(l); return 0; }
                host.Bind(l, handle, p);
                return handle;
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiSetText(int pid, int handle, byte* text)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                object o = p.Objs.Get(handle);
                if (o == null || Host == null) return -1;
                return Host.SetText(o, CStr(text)) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiFillRect(int pid, int handle, int x, int y, int w, int h, uint argb)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                object o = p.Objs.Get(handle);
                if (o == null || Host == null) return -1;
                return Host.FillRect(o, x, y, w, h, argb) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiPollEvent(int pid, KEvent* outEv)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (outEv == null) return 0;
                KEvent e;
                if (!p.Events.Pop(out e)) return 0;
                *outEv = e;
                return 1;
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiReadFile(int pid, byte* path, byte* buf, int max)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null || buf == null || max <= 0 || max > MaxIo) return -1;
                byte[] data = Host.ReadFile(CStr(path));
                if (data == null) return -1;
                int n = data.Length < max ? data.Length : max;
                for (int i = 0; i < n; i++) buf[i] = data[i];
                return n;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiWriteFile(int pid, byte* path, byte* buf, int len)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null || buf == null || len < 0 || len > MaxIo) return -1;
                byte[] data = new byte[len];
                for (int i = 0; i < len; i++) data[i] = buf[i];
                return Host.WriteFile(CStr(path), data) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static byte* ApiAlloc(int pid, int size)
        {
            try
            {
                ElfProgram p = Live(pid);
                return p == null ? null : p.Alloc(size);
            }
            catch { return null; }
        }

        [UnmanagedCallersOnly]
        private static int ApiGetArgs(int pid, byte* buf, int max)
        {
            try
            {
                ElfProgram p = Get(pid);
                if (p == null || buf == null || max <= 0) return -1;
                int total = 0;
                for (int i = 0; i < p.Args.Length; i++) total += p.Args[i].Length + 1;
                if (total > max) return -1;
                int o = 0;
                for (int i = 0; i < p.Args.Length; i++)
                {
                    string a = p.Args[i];
                    for (int j = 0; j < a.Length; j++) buf[o++] = (byte)a[j];
                    buf[o++] = 0;
                }
                return p.Args.Length;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTicks(int pid)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                return (int)(Environment.TickCount64 & 0x7FFFFFFF);
            }
            catch { return -2; }
        }
    }
}
