// WindoseBridge.cs - connects the ELF loader to Windose's Window / Component / Button / Label / Panel,
// the WindowManager, the process list and System.IO.
// Written against the signatures you pasted; NOT compiled against your project (I can't see Label, Palette,
// ProcessManger, SingleThreadedProcess). Expect to fix a name or two. Needs the two edits in README_WINDOW_PATCH.txt.
using System;
using System.Drawing;
using Windose.System.System_Calls;

namespace Windose.System.Elf
{
    public sealed class WindoseBridge : IElfHost
    {
        private static int _windowCount;

        /// <summary>Call once at boot.</summary>
        public static void Install()
        {
            ElfRunner.Host = new WindoseBridge();

            // TODO: where program output goes (your terminal's write method).
            ElfRunner.Output = delegate (ElfProgram p, string s) { /* Terminal.Write(s); */ };

            // Shell command, e.g. "run <path> [args]":
            //   byte[] data = File.ReadAllBytes(path);
            //   string err; ElfProgram p = ElfRunner.Spawn(data, name, args, out err);
            //   if (p == null) Terminal.WriteLine("exec failed: " + err); else Terminal.WriteLine("pid " + p.Pid);
            // "ps":   foreach (ElfProgram p in ElfRunner.List()) ... p.Pid, p.Name, p.State, p.MemoryBytes
            // "kill": ElfRunner.Kill(pid);
            // Exit notice: ElfRunner.Exited = delegate (ElfProgram p) { Terminal.WriteLine("pid " + p.Pid + " exited: " + p.ExitCode); };

            // Only if you get a page fault on the first instruction (non-executable heap):
            // ElfRunner.MakeExecutable = delegate (ulong address, ulong length) { /* mark pages executable */ };
        }

        // ---------------------------------------------------------------- process list

        // The ELF itself runs on its own thread (it blocks in its main loop). This entry only makes the program
        // visible in the process list and lets "kill" from there work.
        public void ProgramStarted(ElfProgram p)
        {
            SingleThreadedProcess proc = new SingleThreadedProcess("elf: " + p.Name, ProcessType.Program);
            proc.onUpdate += delegate () { };
            proc.onDispose += delegate () { ElfRunner.Kill(p.Pid); };
            p.HostData = proc;
            ProcessManger.QueueStart(proc);
        }

        public void ProgramExited(ElfProgram p)
        {
            SingleThreadedProcess proc = p.HostData as SingleThreadedProcess;
            if (proc != null) ProcessManger.QueueStop(proc);
        }

        // ---------------------------------------------------------------- UI

        // Programs see a client area of w x h; we add the frame and title bar around it.
        public object CreateWindow(int w, int h, string title)
        {
            int n = _windowCount++ % 10;
            int border = Palette.BorderSize;
            int titleH = Palette.TitleBarHeight;

            Window win = new Window(80 + n * 30, 80 + n * 30,
                                    w + border * 2, h + titleH + border,
                                    title, true);
            WindowManager.Register(win);   // posts to the DWM: starts the process, z-order, focus, taskbar button
            return win;
        }

        public object CreateButton(object parent, int x, int y, int w, int h, string text)
        {
            Component p = parent as Component;
            if (p == null) return null;
            Button b = new Button(text, ClientX(p, x), ClientY(p, y), w, h);
            lock (Component.components) { p.AddChild(b); }   // the DWM composes under this same lock
            return b;
        }

        public object CreateLabel(object parent, int x, int y, string text)
        {
            Component p = parent as Component;
            if (p == null) return null;
            int lx = ClientX(p, x);
            int width = Math.Max(32, p.Width - lx - Palette.BorderSize);
            Label l = new Label(lx, ClientY(p, y), width, 20)
            {
                text = text,
                useBackground = false,
                useForeground = false,
            };
            lock (Component.components) { p.AddChild(l); }
            return l;
        }

        public void Bind(object obj, int handle, ElfProgram owner)
        {
            int h = handle;
            Window win = obj as Window;
            if (win != null)
            {
                win.onClosed = delegate () { owner.PostEvent(ElfEvent.Close, h, 0, 0); };
                return;
            }
            Button b = obj as Button;
            if (b != null)
                b.leftClickAction = delegate () { owner.PostEvent(ElfEvent.Click, h, 0, 0); };
        }

        public bool SetText(object obj, string text)
        {
            Button b = obj as Button;
            if (b != null) { b.text = text; b.MarkDirty(); return true; }
            Label l = obj as Label;
            if (l != null) { l.text = text; l.MarkDirty(); return true; }
            Window w = obj as Window;
            if (w != null) { w.SetTitle(text); return true; }
            return false;
        }

        // Windose redraws windows every frame (DrawLocal), so pixels drawn "from outside" would be wiped.
        // A filled rect is therefore a solid Panel child. NOTE: every call adds one Panel - fine for a few
        // static rects, not for per-frame animation. (A proper canvas component is the next step.)
        public bool FillRect(object window, int x, int y, int w, int h, uint argb)
        {
            Component p = window as Component;
            if (p == null || w <= 0 || h <= 0) return false;
            Panel rect = new Panel(Color.FromArgb(unchecked((int)argb)), ClientX(p, x), ClientY(p, y), w, h);
            rect.useBackground = true;
            lock (Component.components) { p.AddChild(rect); }
            return true;
        }

        public void Close(object obj)
        {
            Window win = obj as Window;
            if (win != null) { WindowManager.Close(win); return; }   // async; safe to call twice (CloseNow checks windows.Contains)

            Component c = obj as Component;
            if (c != null && c.Parent != null)
                lock (Component.components) { c.Parent.RemoveChild(c); }
        }

        // ---------------------------------------------------------------- files

        public byte[] ReadFile(string path)
        {
            try { return global::System.IO.File.Exists(path) ? global::System.IO.File.ReadAllBytes(path) : null; }
            catch { return null; }
        }

        public bool WriteFile(string path, byte[] data)
        {
            try { global::System.IO.File.WriteAllBytes(path, data); return true; }
            catch { return false; }
        }

        // Program coordinates are relative to the window's client area (inside frame + title bar).
        private static int ClientX(Component parent, int x) { return parent is Window ? x + Palette.BorderSize : x; }
        private static int ClientY(Component parent, int y) { return parent is Window ? y + Palette.TitleBarHeight : y; }
    }
}
