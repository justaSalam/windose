// WindoseBridge.cs - FILL THIS IN. Every TODO is one call into your own Window/Panel/Button/VFS code.
// Until you do, window functions just fail (return 0 / -1) and hello.elf still runs.
using System;
using Windose.System.Kernel;

namespace Windose.System.Elf
{
    /// <summary>
    /// C# Side of the ELF host bridge
    /// Implement this interface to connect ElfRunner to your Kernel
    /// </summary>
    public sealed class WindoseBridge : IElfHost
    {
        /// <summary>Call once at boot.</summary>
        public static void Install()
        {
            ElfRunner.Host = new WindoseBridge();
            ElfRunner.Output = delegate (string s) { /* TODO: Terminal.Write(s); */ };
        
            // Only needed if your heap is non-executable and you get a page fault on the first instruction:
            // ElfRunner.MakeExecutable = delegate (ulong address, ulong length) { /* TODO: mark pages executable */ };
        }

        public object CreateWindow(int w, int h, string title)
        {
            Window win = new Window(100, 100, w, h, title);
            LaunchTracker.Start(() => win);
            return win;
        }

        public object CreateButton(object parent, int x, int y, int w, int h, string text)
        {
            Button button = new Button(text, x, y, w, h);

            using Component component = (Component)parent;

            component.AddChild(button);

            return button;
        }

        public object CreateLabel(object parent, int x, int y, string text)
        {
            Label label = new Label(x, y, text.Length * 8, 20)
            {
                text = text,
            };

            using Component component = (Component)parent;

            component.AddChild(label);

            return label;
        }

        public void Bind(object obj, int handle)
        {
            using Window winHandle = (Window)obj;
          
            // TODO: window -> win.OnClose = () => ElfEvents.Push(ElfEvent.Close, handle, 0, 0);
            //       button -> btn.OnClick = () => ElfEvents.Push(ElfEvent.Click, handle, 0, 0);
            //       (capture 'handle' in a local, one lambda per object is fine here)
        }

        public bool SetText(object obj, string text)
        {
            using Component component = (Component)obj;
            component.text = text;
            component.MarkDirty();
            // TODO: ((Component)obj).Text = text; mark dirty; return true;
            return false;
        }

        public bool FillRect(object window, int x, int y, int w, int h, uint argb)
        {
            // TODO: draw into the window's buffer, mark dirty; return true;
            return false;
        }

        public void Close(object obj)
        {
            WindowManager.Close(obj as Window);
            // TODO: if already closed return; otherwise close/remove from DWM
        }

        public byte[] ReadFile(string path)
        {
            // TODO: return your VFS read (null if it doesn't exist)
            return null;
        }

        public bool WriteFile(string path, byte[] data)
        {
            // TODO: your VFS write
            return false;
        }
    }
}
