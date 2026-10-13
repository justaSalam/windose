// ElfRunner.cs - loads a static x86-64 ELF and calls its entry point in ring 0 (no isolation!).
// Needs AllowUnsafeBlocks. Everything OS-specific goes through IElfHost (see WindoseBridge.cs).
//
// KernelApi below MUST match KernelApi in programs/kapi.h: same fields, same order.
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Windose.System.Elf
{
    /// <summary>
    /// The API struct passed to the ELF program.
    /// The program calls these function pointers to do work
    /// </summary>
    public unsafe struct KernelApi
    {
        public delegate* unmanaged<byte*, int, void> Write;                 // write(text, len)
        public delegate* unmanaged<int, int, byte*, int> CreateWindow;      // create_window(w, h, title)
        public delegate* unmanaged<int> CloseWindow;      // close_window(handle)
        public delegate* unmanaged<int, int> Close;                         // close(handle)
        public delegate* unmanaged<int, void> Sleep;                        // sleep(ms)
        public delegate* unmanaged<int, int, int, int, int, byte*, int> CreateButton; // (parent,x,y,w,h,text)
        public delegate* unmanaged<int, int, int, byte*, int> CreateLabel;  // (parent,x,y,text)
        public delegate* unmanaged<int, byte*, int> SetText;                // (obj,text)
        public delegate* unmanaged<int, int, int, int, int, uint, int> FillRect; // (win,x,y,w,h,argb)
        public delegate* unmanaged<KEvent*, int> PollEvent;                 // (Event*) -> 1 if got one
        public delegate* unmanaged<byte*, byte*, int, int> ReadFile;        // (path,buf,max) -> bytes or -1
        public delegate* unmanaged<byte*, byte*, int, int> WriteFile;       // (path,buf,len) -> 0 or -1
    }

    /// <summary>
    /// If implementation needs OS access, add a method to `IElfHost` and implement it in `WindoseBridge.cs`.
    /// </summary>
    public static unsafe class ElfRunner
    {
        /// <summary>Set at boot. Receives everything the program prints.</summary>
        public static Action<string> Output;

        /// <summary>Set at boot (WindoseBridge.Install). Windows/files; without it those API calls fail.</summary>
        public static IElfHost Host;

        /// <summary>Optional: called with (address, length) of the loaded image before the jump.
        /// Use it to mark those pages executable if your heap is non-executable.</summary>
        public static Action<ulong, ulong> MakeExecutable;

        private static HandleTable _objs = new HandleTable();
        private static bool _running;

        private const int MaxStr = 260;
        private const int MaxIo = 1024 * 1024;

        // ------------------------------------------------------------ run

        public static bool Run(byte[] file, out string error)
        {
            if (_running) { error = "another ELF program is already running"; return false; }

            ElfFile elf = ElfFile.Parse(file, out error);
            if (elf == null) return false;

            if (!elf.Is64 || !elf.LittleEndian || elf.Machine != ElfConst.EM_X86_64)
            { error = "need x86-64 little-endian ELF64"; return false; }
            if (elf.Type != ElfConst.ET_EXEC)
            { error = "need ET_EXEC (static, non-PIE)"; return false; }
            if (elf.GetInterpreter() != null)
            { error = "dynamic executables are not supported"; return false; }

            ulong low;
            byte[] img = elf.LoadImage(16UL * 1024 * 1024, out low);
            if (img == null) { error = elf.Error; return false; }

            if (elf.Entry < low || elf.Entry - low >= (ulong)img.Length)
            { error = "entry point outside image"; return false; }
            ulong entryOff = elf.Entry - low;

            KernelApi api = default(KernelApi);
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

            _running = true;
            ElfEvents.Clear();
            try
            {
                fixed (byte* p = img)   // pinned for the whole call
                {
                    if (MakeExecutable != null) MakeExecutable((ulong)p, (ulong)img.Length);
                    var entry = (delegate* unmanaged<KernelApi*, void>)(p + entryOff);
                    entry(&api);
                }
                error = null;
                return true;
            }
            finally
            {
                // Close whatever the program leaked or left open when it exited/crashed.
                object[] left = _objs.Drain();
                for (int i = 0; i < left.Length; i++)
                {
                    try { if (Host != null) Host.Close(left[i]); } catch { }
                }
                _running = false;
            }
        }

        // ------------------------------------------------------------ helpers

        private static string CStr(byte* s)
        {
            if (s == null) return "";
            int n = 0;
            while (n < MaxStr && s[n] != 0) n++;
            char[] c = new char[n];
            for (int i = 0; i < n; i++) c[i] = (char)s[i];
            return new string(c);
        }

        // ------------------------------------------------------------ API handlers
        // Rules: never let an exception escape, never trust a pointer/length, return codes only.
        // Return value: 0 = ok, negative = error; create_* return a handle (0 = failed).

        [UnmanagedCallersOnly]
        private static void ApiWrite(byte* text, int len)
        {
            try
            {
                if (Output == null || text == null || len <= 0 || len > 4096) return;
                char[] c = new char[len];
                for (int i = 0; i < len; i++) c[i] = (char)text[i];
                Output(new string(c));
            }
            catch { }
        }

        [UnmanagedCallersOnly]
        private static int ApiCreateWindow(int w, int h, byte* title)
        {
            try
            {
                if (Host == null || w < 32 || h < 32 || w > 4096 || h > 4096)
                {
                    return 0;
                }
                //Managed code cannot create a window directly, so delegate this to the Host implementation.
                object win = Host.CreateWindow(w, h, CStr(title));
                if (win == null) 
                { 
                    return 0;
                }
               
                int handle = _objs.Add(win);
                if (handle == 0) 
                { 
                    Host.Close(win); return 0; 
                }
                Host.Bind(win, handle);

                return handle;
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiCloseWindow(int w, int h, byte* title)
        {
            try
            {
                if (Host == null || w < 32 || h < 32 || w > 4096 || h > 4096) return 0;
                object win = Host.CreateWindow(w, h, CStr(title));
                if (win == null) return 0;
                int handle = _objs.Add(win);
                if (handle == 0) { Host.Close(win); return 0; }
                Host.Bind(win, handle);
                return handle;
            }
            catch { return 0; }
        }


        [UnmanagedCallersOnly]
        private static int ApiClose(int handle)
        {
            try
            {
                object o = _objs.Remove(handle);
                if (o == null) return -1;
                Host.Close(o);
                return 0;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static void ApiSleep(int ms)
        {
            try
            {
                if (ms < 0) ms = 0;
                if (ms > 10000) ms = 10000;
                Thread.Sleep(ms);
            }
            catch { }
        }

        [UnmanagedCallersOnly]
        private static int ApiCreateButton(int parent, int x, int y, int w, int h, byte* text)
        {
            try
            {
                if (Host == null) return 0;
                object p = _objs.Get(parent);
                if (p == null) return 0;
                object b = Host.CreateButton(p, x, y, w, h, CStr(text));
                if (b == null) return 0;
                int handle = _objs.Add(b);
                if (handle == 0) { Host.Close(b); return 0; }
                Host.Bind(b, handle);
                return handle;
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiCreateLabel(int parent, int x, int y, byte* text)
        {
            try
            {
                if (Host == null) return 0;
                object p = _objs.Get(parent);
                if (p == null) return 0;
                object l = Host.CreateLabel(p, x, y, CStr(text));
                if (l == null) return 0;
                int handle = _objs.Add(l);
                if (handle == 0) { Host.Close(l); return 0; }
                Host.Bind(l, handle);
                return handle;
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiSetText(int handle, byte* text)
        {
            try
            {
                object o = _objs.Get(handle);
                if (o == null || Host == null) return -1;
                return Host.SetText(o, CStr(text)) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiFillRect(int handle, int x, int y, int w, int h, uint argb)
        {
            try
            {
                object o = _objs.Get(handle);
                if (o == null || Host == null) return -1;
                return Host.FillRect(o, x, y, w, h, argb) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiPollEvent(KEvent* outEv)
        {
            try
            {
                if (outEv == null) return 0;
                KEvent e;
                if (!ElfEvents.Pop(out e)) return 0;
                *outEv = e;
                return 1;
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiReadFile(byte* path, byte* buf, int max)
        {
            try
            {
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
        private static int ApiWriteFile(byte* path, byte* buf, int len)
        {
            try
            {
                if (Host == null || buf == null || len < 0 || len > MaxIo) return -1;
                byte[] data = new byte[len];
                for (int i = 0; i < len; i++) data[i] = buf[i];
                return Host.WriteFile(CStr(path), data) ? 0 : -3;
            }
            catch { return -2; }
        }
    }
}
