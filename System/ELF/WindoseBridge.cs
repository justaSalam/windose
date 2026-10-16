// WindoseBridge.cs - connects the ELF loader to Windose's Window / Component / Button / Label / Panel,
// the WindowManager, the process list and System.IO.
// Written against the signatures you pasted; NOT compiled against your project (I can't see Label, Palette,
// ProcessManger, SingleThreadedProcess). Expect to fix a name or two. Needs the two edits in README_WINDOW_PATCH.txt.
using System;
using System.Drawing;
using Cosmos.Kernel.System.FileSystem;
using Windose.System.Kernel;
using Windose.System.System_Calls;

namespace Windose.System.Elf
{
    public sealed class WindoseBridge : IElfHost
    {
        private static int _windowCount;

        private sealed class ObjRef
        {
            public ElfProgram Owner;
            public int Handle;
        }

        private static readonly global::System.Collections.Generic.Dictionary<object, ObjRef> _handles =
            new global::System.Collections.Generic.Dictionary<object, ObjRef>();

        internal static bool TryGetHandle(object obj, ElfProgram owner, out int handle)
        {
            handle = 0;
            if (obj == null) return false;
            lock (_handles)
            {
                ObjRef r;
                if (!_handles.TryGetValue(obj, out r) || r.Owner != owner) return false;
                handle = r.Handle;
                return true;
            }
        }

        private static void TrackHandle(object obj, ElfProgram owner, int handle)
        {
            lock (_handles) _handles[obj] = new ObjRef { Owner = owner, Handle = handle };
        }

        private static void ForgetHandle(object obj)
        {
            lock (_handles) _handles.Remove(obj);
        }

        private static void ForgetOwner(ElfProgram owner)
        {
            lock (_handles)
            {
                global::System.Collections.Generic.List<object> dead =
                    new global::System.Collections.Generic.List<object>();
                foreach (global::System.Collections.Generic.KeyValuePair<object, ObjRef> kv in _handles)
                    if (kv.Value.Owner == owner) dead.Add(kv.Key);
                for (int i = 0; i < dead.Count; i++) _handles.Remove(dead[i]);
            }
        }

        /// <summary>Call once at boot.</summary>
        public static void Install()
        {
            ElfRunner.Host = new WindoseBridge();

            ElfRunner.Output = delegate (ElfProgram p, string s)
            {
                try { SystemLogger.WriteLine("elf:" + p.Name, s, ConsoleMessageType.Log); } catch { }
            };

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
            ForgetOwner(p);
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

            ElfWindow win = new ElfWindow(80 + n * 30, 80 + n * 30,
                                          w + border * 2, h + titleH + border,
                                          title);
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
                // Labels center themselves by default; ELF coordinates are absolute.
                horizontalAlignment = HorizontalAlignment.Left,
                verticalAlignment = VerticalAlignment.Top,
            };
            lock (Component.components) { p.AddChild(l); }
            return l;
        }

        public void Bind(object obj, int handle, ElfProgram owner)
        {
            TrackHandle(obj, owner, handle);
            int h = handle;
            Component comp = obj as Component;
            if (comp != null)
            {
                comp.FocusChanged += delegate (bool focused)
                {
                    owner.PostEvent(ElfEvent.Focus, h, focused ? 1 : 0, 0);
                };
            }
            FileDialog fd = obj as FileDialog;
            if (fd != null)
            {
                fd.onClosed = delegate ()
                {
                    if (fd.SelectedPath == null)
                        owner.PostEvent(ElfEvent.Click, h, 0, 0);
                };
                return;
            }
            ElfWindow ewin = obj as ElfWindow;
            if (ewin != null)
            {
                ewin.Owner = owner;
                ewin.ProgramHandle = h;
                ewin.DragDropped += delegate (DragData data)
                {
                    owner.LastDropText = data != null ? data.Text ?? "" : "";
                    owner.PostEvent(ElfEvent.Drop, h, data != null ? data.X : 0, data != null ? data.Y : 0);
                };
            }
            Window win = obj as Window;
            if (win != null)
            {
                win.onClosed = delegate () { owner.PostEvent(ElfEvent.Close, h, 0, 0); };
                return;
            }
            Button b = obj as Button;
            if (b != null)
            {
                b.leftClickAction = delegate () { owner.PostEvent(ElfEvent.Click, h, 0, 0); };
                return;
            }
            Checkbox cb = obj as Checkbox;
            if (cb != null)
            {
                cb.CheckedChanged += delegate (bool v) { owner.PostEvent(ElfEvent.Click, h, v ? 1 : 0, 0); };
                return;
            }
            RadioButton rb = obj as RadioButton;
            if (rb != null)
            {
                rb.CheckedChanged += delegate (bool v) { owner.PostEvent(ElfEvent.Click, h, v ? 1 : 0, 0); };
                return;
            }
            ComboBox combo = obj as ComboBox;
            if (combo != null)
            {
                combo.SelectedIndexChanged += delegate (int i) { owner.PostEvent(ElfEvent.Click, h, i, 0); };
                return;
            }
            ElfListBox lb = obj as ElfListBox;
            if (lb != null)
            {
                lb.List.selectedChanged += delegate (ListViewItem item) { owner.PostEvent(ElfEvent.Click, h, lb.List.SelectedIndex, 0); };
                lb.List.itemDoubleClick += delegate (ListViewItem item) { owner.PostEvent(ElfEvent.DoubleClick, h, lb.List.SelectedIndex, 0); };
                return;
            }
            ElfTree et = obj as ElfTree;
            if (et != null)
            {
                et.Owner = owner;
                et.Tree.selectedChanged += delegate (TreeViewItem item)
                {
                    int ih = 0;
                    if (item != null) TryGetHandle(item, owner, out ih);
                    owner.PostEvent(ElfEvent.Click, h, ih, 0);
                };
                et.Tree.itemDoubleClick += delegate (TreeViewItem item)
                {
                    int ih = 0;
                    if (item != null) TryGetHandle(item, owner, out ih);
                    owner.PostEvent(ElfEvent.DoubleClick, h, ih, 0);
                };
                return;
            }
            TabControl tc = obj as TabControl;
            if (tc != null)
            {
                tc.SelectedIndexChanged += delegate (int i) { owner.PostEvent(ElfEvent.Click, h, i, 0); };
                return;
            }
            MenuItem mi = obj as MenuItem;
            if (mi != null)
            {
                if (!mi.drawSeparator)
                    mi.click = delegate () { owner.PostEvent(ElfEvent.Click, h, 0, 0); };
                return;
            }
            ColorPicker cp = obj as ColorPicker;
            if (cp != null)
            {
                cp.ValueChanged += delegate (Color v) { owner.PostEvent(ElfEvent.Click, h, v.ToArgb(), 0); };
                return;
            }
            NumericUpDown nu = obj as NumericUpDown;
            if (nu != null)
            {
                nu.ValueChanged += delegate (float v) { owner.PostEvent(ElfEvent.Click, h, (int)v, 0); };
                return;
            }
            Calendar cal = obj as Calendar;
            if (cal != null)
            {
                cal.DateChanged += delegate (DateTime d) { owner.PostEvent(ElfEvent.Click, h, d.Year * 10000 + d.Month * 100 + d.Day, 0); };
                return;
            }
            DataGrid grid = obj as DataGrid;
            if (grid != null)
            {
                grid.SelectedChanged += delegate (int row) { owner.PostEvent(ElfEvent.Click, h, row, 0); };
                return;
            }
        }

        public bool SetText(object obj, string text)
        {
            Button b = obj as Button;
            if (b != null) { b.text = text; b.MarkDirty(); return true; }
            TextField t = obj as TextField;
            if (t != null) { t.SetText(text ?? ""); return true; }
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
            ForgetHandle(obj);
            if (obj is MenuBar)
            {
                lock (menuPages) menuPages.Remove(obj);
            }
            MenuPopup popup = obj as MenuPopup;
            if (popup != null) { popup.Hide(); return; }
            Window win = obj as Window;
            if (win != null) { WindowManager.Close(win); return; }   // async; safe to call twice (CloseNow checks windows.Contains)

            Component c = obj as Component;
            if (c != null && c.Parent != null)
                lock (Component.components) { c.Parent.RemoveChild(c); }
        }

        public object CreateCanvas(object parent, int x, int y, int w, int h)
        {
            Component p = parent as Component;
            if (p == null || w <= 0 || h <= 0) return null;
            ElfCanvasComponent c = new ElfCanvasComponent(ClientX(p, x), ClientY(p, y), w, h);
            lock (Component.components) { p.AddChild(c); }
            return c;
        }

        public bool DrawPixels(object canvas, int x, int y, int w, int h, IntPtr buf, int len)
        {
            ElfCanvasComponent c = canvas as ElfCanvasComponent;
            if (c == null) return false;
            return c.SetPixels(x, y, w, h, buf, len);
        }

        public bool ResizeWindow(object window, int w, int h)
        {
            Window win = window as Window;
            if (win == null || w <= 0 || h <= 0) return false;
            win.SetClientSize(w, h);
            return true;
        }

        // ---------------------------------------------------------------- widgets

        private static Component AsChildParent(object parent)
        {
            return parent as Component;
        }

        private static void Attach(Component parent, Component child)
        {
            lock (Component.components) { parent.AddChild(child); }
        }

        public object CreateCheckbox(object parent, int x, int y, string text)
        {
            Component p = AsChildParent(parent);
            if (p == null) return null;
            Checkbox c = new Checkbox(ClientX(p, x), ClientY(p, y)) { text = text ?? "" };
            Attach(p, c);
            return c;
        }

        public bool SetChecked(object obj, bool value)
        {
            Checkbox c = obj as Checkbox;
            if (c != null) { c.Checked = value; return true; }
            RadioButton r = obj as RadioButton;
            if (r != null) { r.Checked = value; return true; }
            return false;
        }

        public int GetChecked(object obj)
        {
            Checkbox c = obj as Checkbox;
            if (c != null) return c.Checked ? 1 : 0;
            RadioButton r = obj as RadioButton;
            if (r != null) return r.Checked ? 1 : 0;
            return -1;
        }

        public object CreateRadio(object parent, int x, int y, string text, string group)
        {
            Component p = AsChildParent(parent);
            if (p == null) return null;
            RadioButton r = new RadioButton(ClientX(p, x), ClientY(p, y))
            {
                text = text ?? "",
                Group = group ?? "",
            };
            Attach(p, r);
            return r;
        }

        public object CreateSlider(object parent, int x, int y, int w, bool horizontal, int min, int max, int value)
        {
            Component p = AsChildParent(parent);
            if (p == null || w <= 0) return null;
            if (max < min) { int t = min; min = max; max = t; }
            Slider s = new Slider(ClientX(p, x), ClientY(p, y), w, 25,
                horizontal ? Orientation.Horizontal : Orientation.Vertical);
            s.Minimum = min;
            s.Maximum = max;
            s.Value = value;
            Attach(p, s);
            return s;
        }

        public bool SliderSet(object obj, int value)
        {
            Slider s = obj as Slider;
            if (s == null) return false;
            s.Value = value;
            return true;
        }

        public int SliderGet(object obj)
        {
            Slider s = obj as Slider;
            if (s == null) return int.MinValue;
            return (int)s.Value;
        }

        public object CreateProgress(object parent, int x, int y, int w, int h)
        {
            Component p = AsChildParent(parent);
            if (p == null || w <= 0 || h <= 0) return null;
            ProgressBar b = new ProgressBar(ClientX(p, x), ClientY(p, y), w, h);
            Attach(p, b);
            return b;
        }

        public bool ProgressSetup(object obj, int min, int max)
        {
            ProgressBar b = obj as ProgressBar;
            if (b == null) return false;
            if (max < min) { int t = min; min = max; max = t; }
            b.Minimum = min;
            b.Maximum = max;
            return true;
        }

        public bool ProgressSet(object obj, int value)
        {
            ProgressBar b = obj as ProgressBar;
            if (b == null) return false;
            b.Value = value;
            return true;
        }

        public object CreateTextField(object parent, int x, int y, int w)
        {
            Component p = AsChildParent(parent);
            if (p == null || w <= 0) return null;
            TextField t = new TextField(ClientX(p, x), ClientY(p, y), w);
            Attach(p, t);
            return t;
        }

        public bool TextFieldSetup(object obj, int maxLength, int passwordChar, int readOnly)
        {
            TextField t = obj as TextField;
            if (t == null) return false;
            t.MaxLength = Math.Max(0, maxLength);
            t.PasswordChar = passwordChar == 0 ? '\0' : (char)(passwordChar & 0xFFFF);
            t.readOnly = readOnly != 0;
            t.MarkDirty();
            return true;
        }

        public string GetText(object obj)
        {
            ComboBox combo = obj as ComboBox;
            if (combo != null) return combo.SelectedText;
            FileDialog dlg = obj as FileDialog;
            if (dlg != null) return dlg.SelectedPath ?? "";
            TabPage page = obj as TabPage;
            if (page != null) return page.Text ?? "";
            Component c = obj as Component;
            if (c != null) return c.text ?? "";
            return null;
        }

        public object CreateCombo(object parent, int x, int y, int w)
        {
            Component p = AsChildParent(parent);
            if (p == null || w <= 0) return null;
            ComboBox c = new ComboBox(ClientX(p, x), ClientY(p, y), w);
            Attach(p, c);
            return c;
        }

        public int ComboAddItem(object obj, string text)
        {
            ComboBox c = obj as ComboBox;
            if (c == null) return -1;
            c.AddItem(text ?? "");
            return c.ItemCount - 1;
        }

        public bool ComboRemoveItem(object obj, int index)
        {
            ComboBox c = obj as ComboBox;
            if (c == null || index < 0 || index >= c.ItemCount) return false;
            c.RemoveItemAt(index);
            return true;
        }

        public void ComboClear(object obj)
        {
            ComboBox c = obj as ComboBox;
            if (c != null) c.ClearItems();
        }

        public int ComboGetSelected(object obj)
        {
            ComboBox c = obj as ComboBox;
            if (c == null) return -1;
            return c.SelectedIndex;
        }

        public bool ComboSetSelected(object obj, int index)
        {
            ComboBox c = obj as ComboBox;
            if (c == null || index < -1 || index >= c.ItemCount) return false;
            c.SelectedIndex = index;
            return true;
        }

        public object CreateListBox(object parent, int x, int y, int w, int h)
        {
            Component p = AsChildParent(parent);
            if (p == null || w <= 0 || h <= 0) return null;
            ElfListBox box = new ElfListBox(ClientX(p, x), ClientY(p, y), w, h);
            Attach(p, box);
            return box;
        }

        public int ListAddItem(object obj, string text)
        {
            ElfListBox box = obj as ElfListBox;
            if (box == null) return -1;
            box.List.AddItem(new string[] { text ?? "" });
            box.Refresh();
            return box.List.items.Count - 1;
        }

        public int ListAddRow(object obj, string[] cells)
        {
            ElfListBox box = obj as ElfListBox;
            if (box == null || cells == null) return -1;
            box.List.AddItem(cells);
            box.Refresh();
            return box.List.items.Count - 1;
        }

        public bool ListRemoveItem(object obj, int index)
        {
            ElfListBox box = obj as ElfListBox;
            if (box == null) return false;
            box.List.RemoveItemAt(index);
            box.Refresh();
            return true;
        }

        public void ListClear(object obj)
        {
            ElfListBox box = obj as ElfListBox;
            if (box == null) return;
            box.List.ClearItems();
            box.Refresh();
        }

        public int ListGetSelected(object obj)
        {
            ElfListBox box = obj as ElfListBox;
            if (box == null) return -1;
            return box.List.SelectedIndex;
        }

        public bool ListSelect(object obj, int index)
        {
            ElfListBox box = obj as ElfListBox;
            if (box == null) return false;
            box.List.SelectedIndex = index;
            box.Refresh();
            return true;
        }

        public object CreateTabs(object parent, int x, int y, int w, int h)
        {
            Component p = AsChildParent(parent);
            if (p == null || w <= 0 || h <= 0) return null;
            TabControl t = new TabControl(ClientX(p, x), ClientY(p, y), w, h);
            Attach(p, t);
            return t;
        }

        public object TabAddPage(object obj, string title)
        {
            TabControl t = obj as TabControl;
            if (t == null) return null;
            return t.AddPage(title ?? "");
        }

        public bool TabSelect(object obj, int index)
        {
            TabControl t = obj as TabControl;
            if (t == null || index < 0 || index >= t.PageCount) return false;
            t.SelectedIndex = index;
            return true;
        }

        public int TabGetSelected(object obj)
        {
            TabControl t = obj as TabControl;
            if (t == null) return -1;
            return t.SelectedIndex;
        }

        public object MenuBarCreate(object window)
        {
            Component p = AsChildParent(window);
            if (p == null) return null;
            MenuBar bar = new MenuBar(ClientX(p, 0), ClientY(p, 0), Math.Max(40, p.Width));
            Attach(p, bar);
            return bar;
        }

        private readonly global::System.Collections.Generic.Dictionary<object, global::System.Collections.Generic.Dictionary<string, MenuPage>> menuPages =
            new global::System.Collections.Generic.Dictionary<object, global::System.Collections.Generic.Dictionary<string, MenuPage>>();

        public object MenuAddItem(object menubar, string page, string text)
        {
            MenuBar bar = menubar as MenuBar;
            if (bar == null || string.IsNullOrEmpty(page)) return null;
            global::System.Collections.Generic.Dictionary<string, MenuPage> pages;
            lock (menuPages)
            {
                if (!menuPages.TryGetValue(bar, out pages))
                {
                    pages = new global::System.Collections.Generic.Dictionary<string, MenuPage>();
                    menuPages[bar] = pages;
                }
            }
            MenuPage mp;
            lock (menuPages)
            {
                if (!pages.TryGetValue(page, out mp))
                {
                    mp = bar.AddMenuPage(page);
                    pages[page] = mp;
                }
            }
            if (text == "-") return mp.AddSeparator();
            return mp.AddItem(text ?? "", null);
        }

        public object MsgBox(string title, string text, string[] buttons)
        {
            if (buttons == null || buttons.Length == 0) return null;
            MessageBox box = null;
            box = MessageBox.Show(text ?? "", title ?? "", delegate (int index)
            {
                if (TryGetOwnerHandle(box, out ElfProgram owner, out int handle))
                    owner.PostEvent(ElfEvent.Click, handle, index, 0);
            }, buttons);
            return box;
        }

        internal static bool TryGetOwnerHandle(object obj, out ElfProgram owner, out int handle)
        {
            owner = null;
            handle = 0;
            if (obj == null) return false;
            lock (_handles)
            {
                foreach (global::System.Collections.Generic.KeyValuePair<object, ObjRef> kv in _handles)
                {
                    if (ReferenceEquals(kv.Key, obj))
                    {
                        owner = kv.Value.Owner;
                        handle = kv.Value.Handle;
                        return true;
                    }
                }
            }
            return false;
        }

        public object StatusBarCreate(object parent, int x, int y, int w)
        {
            Component p = AsChildParent(parent);
            if (p == null || w <= 0) return null;
            StatusBar s = new StatusBar(ClientX(p, x), ClientY(p, y), w);
            Attach(p, s);
            return s;
        }

        public int StatusAddPanel(object obj, string text, int width)
        {
            StatusBar s = obj as StatusBar;
            if (s == null) return -1;
            s.AddPanel(text ?? "", Math.Max(16, width));
            return s.PanelCount - 1;
        }

        public bool StatusSetText(object obj, int index, string text)
        {
            StatusBar s = obj as StatusBar;
            if (s == null) return false;
            if (index < 0 || index >= s.PanelCount) return false;
            s.SetPanelText(index, text ?? "");
            return true;
        }

        public bool WidgetSetEnabled(object obj, bool value)
        {
            Component c = obj as Component;
            if (c == null) return false;
            c.Enabled = value;
            return true;
        }

        public int WidgetGetEnabled(object obj)
        {
            Component c = obj as Component;
            if (c == null) return -1;
            return c.Enabled ? 1 : 0;
        }

        public bool WidgetFocus(object obj)
        {
            Component c = obj as Component;
            if (c == null) return false;
            c.RequestFocus();
            return true;
        }

        public bool TextFieldSelect(object obj, int start, int len)
        {
            TextField t = obj as TextField;
            if (t == null) return false;
            t.Select(start, len);
            return true;
        }

        public int TextFieldGetCaret(object obj)
        {
            TextField t = obj as TextField;
            if (t == null) return -1;
            return t.SelectionStart + t.SelectionLength;
        }

        public bool TextFieldSetMultiline(object obj, bool multiline)
        {
            TextField t = obj as TextField;
            if (t == null) return false;
            t.Multiline = multiline;
            t.MarkDirty();
            return true;
        }

        public object ColorPickerCreate(object parent, int x, int y)
        {
            Component p = AsChildParent(parent);
            if (p == null) return null;
            ColorPicker c = new ColorPicker(ClientX(p, x), ClientY(p, y));
            Attach(p, c);
            return c;
        }

        public int ColorPickerGet(object obj)
        {
            ColorPicker c = obj as ColorPicker;
            if (c == null) return 0;
            return c.Value.ToArgb();
        }

        public bool ColorPickerSet(object obj, int argb)
        {
            ColorPicker c = obj as ColorPicker;
            if (c == null) return false;
            c.Value = Color.FromArgb(argb);
            return true;
        }

        public object NumericCreate(object parent, int x, int y, int w, int min, int max, int inc, int value)
        {
            Component p = AsChildParent(parent);
            if (p == null || w <= 0) return null;
            if (max < min) { int t = min; min = max; max = t; }
            NumericUpDown n = new NumericUpDown(ClientX(p, x), ClientY(p, y), w);
            n.Minimum = min;
            n.Maximum = max;
            n.Increment = Math.Max(1, inc);
            n.Value = value;
            Attach(p, n);
            return n;
        }

        public bool NumericSet(object obj, int value)
        {
            NumericUpDown n = obj as NumericUpDown;
            if (n == null) return false;
            n.Value = value;
            return true;
        }

        public int NumericGet(object obj)
        {
            NumericUpDown n = obj as NumericUpDown;
            if (n == null) return int.MinValue;
            return (int)n.Value;
        }

        public object CalendarCreate(object parent, int x, int y)
        {
            Component p = AsChildParent(parent);
            if (p == null) return null;
            Calendar c = new Calendar(ClientX(p, x), ClientY(p, y));
            Attach(p, c);
            return c;
        }

        public int CalendarGet(object obj)
        {
            Calendar c = obj as Calendar;
            if (c == null) return 0;
            DateTime d = c.SelectedDate;
            return d.Year * 10000 + d.Month * 100 + d.Day;
        }

        public bool CalendarSet(object obj, int year, int month, int day)
        {
            Calendar c = obj as Calendar;
            if (c == null) return false;
            try { c.SelectedDate = new DateTime(year, month, day); return true; }
            catch { return false; }
        }

        public object FileDialogOpen(string title, string initialPath, string filter, bool save)
        {
            FileDialog dlg = null;
            FileDialogOptions opts = new FileDialogOptions
            {
                Mode = save ? FileDialogMode.Save : FileDialogMode.Open,
                Title = title ?? "",
                InitialPath = string.IsNullOrEmpty(initialPath) ? "/mnt" : initialPath,
                FilterExtension = filter ?? "",
            };
            dlg = new FileDialog(opts, delegate (string path)
            {
                if (TryGetOwnerHandle(dlg, out ElfProgram owner, out int handle))
                    owner.PostEvent(ElfEvent.Click, handle, 1, 0);
            });
            WindowManager.Register(dlg);
            return dlg;
        }

        public object GridCreate(object parent, int x, int y, int w, int h)
        {
            Component p = AsChildParent(parent);
            if (p == null || w <= 0 || h <= 0) return null;
            DataGrid g = new DataGrid(ClientX(p, x), ClientY(p, y), w, h);
            Attach(p, g);
            return g;
        }

        public int GridAddColumn(object obj, string title, int width)
        {
            DataGrid g = obj as DataGrid;
            if (g == null) return -1;
            return g.AddColumn(title ?? "", Math.Max(20, width));
        }

        public int GridAddRow(object obj, string[] cells)
        {
            DataGrid g = obj as DataGrid;
            if (g == null || cells == null) return -1;
            return g.AddRow(cells);
        }

        public void GridClear(object obj)
        {
            DataGrid g = obj as DataGrid;
            if (g != null) g.ClearRows();
        }

        public int GridGetSelected(object obj)
        {
            DataGrid g = obj as DataGrid;
            if (g == null) return -1;
            return g.SelectedRow;
        }

        public bool GridSelect(object obj, int row)
        {
            DataGrid g = obj as DataGrid;
            if (g == null || row < -1 || row >= g.RowCount) return false;
            g.SelectRow(row);
            return true;
        }

        public bool GridSetCell(object obj, int row, int col, string text)
        {
            DataGrid g = obj as DataGrid;
            if (g == null) return false;
            return g.SetCell(row, col, text ?? "");
        }

        public string GridGetCell(object obj, int row, int col)
        {
            DataGrid g = obj as DataGrid;
            if (g == null || row < 0 || row >= g.RowCount || col < 0 || col >= g.ColumnCount) return null;
            return g.GetCell(row, col);
        }

        public bool GridSort(object obj, int col, bool descending)
        {
            DataGrid g = obj as DataGrid;
            if (g == null || col < 0 || col >= g.ColumnCount) return false;
            g.SortByColumn(col, !descending);
            return true;
        }

        public bool DropAccept(object obj, bool accept)
        {
            ElfWindow win = obj as ElfWindow;
            if (win == null) return false;
            win.AllowDrop = accept;
            return true;
        }
        /// <summary>
        /// TODO Implement a better way of launching, this is some seriously ill shit way of doing it
        /// </summary>
        /// <param name="name"></param>
        /// <param name="arg"></param>
        /// <returns></returns>
        public bool LaunchApp(string name, string arg)
        {
            try
            {
                string app = (name ?? "").Trim().ToLowerInvariant();
                if (app == "explorer") LaunchTracker.Start(() => new FileExplorer(100, 100, 800, 500, "File Explorer", "/mnt"));
                else if (app == "terminal") LaunchTracker.Start(() => new CommandPrompt());
                else if (app == "editor")
                {
                    if (string.IsNullOrEmpty(arg)) LaunchTracker.Start(() => new BreezeEditor());
                    else LaunchTracker.Start(() => new BreezeEditor(100, 80, 900, 620, arg));
                }
                else if (app == "taskmgr") LaunchTracker.Start(() => new PerformanceMonitor(180, 120));
                else if (app == "registry") LaunchTracker.Start(() => new RegistryEditor());
                else if (app == "settings") LaunchTracker.Start(() => new DisplaySettings());
                else if (app == "imageviewer" && !string.IsNullOrEmpty(arg)) LaunchTracker.Start(() => new ImageViewer(arg, 120, 120, 640, 480));
                else return false;
                return true;
            }
            catch { return false; }
        }

        public bool OpenFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return false;
                if (path.EndsWith(".elf", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] data = ReadFile(path);
                    if (data == null) return false;
                    string error;
                    return ElfRunner.Spawn(data, path, new string[] { path }, out error) != null;
                }
                FileExplorer.OpenFilePath(path);
                return true;
            }
            catch { return false; }
        }

        public void NotifySend(string title, string text)
        {
            try { NotificationManager.Notify(title ?? "", text ?? ""); } catch { }
        }

        public int DesktopCount()
        {
            return WindowManager.DesktopCount;
        }

        public int DesktopCurrent()
        {
            return WindowManager.CurrentDesktop;
        }

        public bool DesktopSwitch(int index)
        {
            if (index < 0 || index >= WindowManager.DesktopCount) return false;
            WindowManager.SwitchDesktop(index);
            return true;
        }

        public bool WindowSetDesktop(object obj, int index)
        {
            Window win = obj as Window;
            if (win == null || !win.showInTaskbar) return false;
            if (index < 0 || index >= WindowManager.DesktopCount) return false;
            win.desktop = index;
            WindowManager.Invalidate(win.bounds);
            win.MarkDirty();
            return true;
        }

        public object TreeCreate(object parent, int x, int y, int w, int h)
        {
            Component p = AsChildParent(parent);
            if (p == null || w <= 0 || h <= 0) return null;
            ElfTree tree = new ElfTree(ClientX(p, x), ClientY(p, y), w, h);
            Attach(p, tree);
            return tree;
        }

        private static TreeViewItem TreeItem(object node)
        {
            return node as TreeViewItem;
        }

        private static TreeView TreeOf(object obj)
        {
            ElfTree tree = obj as ElfTree;
            return tree != null ? tree.Tree : null;
        }

        private TreeViewItem AddTreeNode(TreeView parent, TreeViewItem parentItem, string text, string icon)
        {
            TreeViewItem item;
            if (!string.IsNullOrEmpty(icon))
            {
                try { item = parentItem != null ? parentItem.AddChild(text ?? "", icon, null) : null; }
                catch { item = null; }
                if (item == null)
                    item = parentItem != null ? parentItem.AddChild(text ?? "") : new TreeViewItem(text ?? "");
            }
            else
            {
                item = parentItem != null ? parentItem.AddChild(text ?? "") : new TreeViewItem(text ?? "");
            }
            if (parentItem == null)
            {
                parent.roots.Add(item);
            }
            parent.MarkDirty();
            return item;
        }

        public object TreeAddRoot(object obj, string text, string icon)
        {
            TreeView tree = TreeOf(obj);
            if (tree == null) return null;
            return AddTreeNode(tree, null, text, icon);
        }

        public object TreeAddChild(object obj, object parentItem, string text, string icon)
        {
            TreeView tree = TreeOf(obj);
            TreeViewItem parent = TreeItem(parentItem);
            if (tree == null || parent == null) return null;
            return AddTreeNode(tree, parent, text, icon);
        }

        public void TreeClear(object obj)
        {
            TreeView tree = TreeOf(obj);
            if (tree != null) tree.ClearItems();
        }

        public int TreeSelectedItem(object obj)
        {
            ElfTree tree = obj as ElfTree;
            if (tree == null || tree.Tree.selectedItem == null || tree.Owner == null) return 0;
            int handle;
            return TryGetHandle(tree.Tree.selectedItem, tree.Owner, out handle) ? handle : 0;
        }

        public bool TreeSetExpanded(object obj, object node, bool expanded)
        {
            TreeViewItem item = TreeItem(node);
            if (item == null) return false;
            if (expanded) item.Expand();
            else item.Collapse();
            TreeView tree = TreeOf(obj);
            if (tree != null) tree.MarkDirty();
            return true;
        }

        public object ToolbarCreate(object parent, int x, int y, int w)
        {
            Component p = AsChildParent(parent);
            if (p == null || w <= 0) return null;
            Toolbar bar = new Toolbar(ClientX(p, x), ClientY(p, y), w);
            Attach(p, bar);
            return bar;
        }

        public object ToolbarAddButton(object obj, string text)
        {
            Toolbar bar = obj as Toolbar;
            if (bar == null) return null;
            return bar.AddButton(text ?? "");
        }

        public void ToolbarAddSeparator(object obj)
        {
            Toolbar bar = obj as Toolbar;
            if (bar != null) bar.AddSeparator();
        }

        public object PopupCreate()
        {
            return new MenuPopup(160, 24);
        }

        public object PopupAddItem(object obj, string text)
        {
            MenuPopup popup = obj as MenuPopup;
            if (popup == null) return null;
            if (text == "-") return popup.AddSeparator();
            return popup.AddItem(text ?? "", null);
        }

        public void PopupShowAt(object obj, int x, int y)
        {
            MenuPopup popup = obj as MenuPopup;
            if (popup == null) return;
            x = Math.Max(0, Math.Min(x, Global.screenWidth - 40));
            y = Math.Max(0, Math.Min(y, Global.screenHeight - 20));
            popup.ShowAt(x, y);
        }

        public int ListAddColumn(object obj, string title, int width)
        {
            ElfListBox box = obj as ElfListBox;
            if (box == null) return -1;
            box.List.AddColumn(title ?? "", Math.Max(20, width));
            box.Refresh();
            return box.List.columns.Count - 1;
        }

        public int WidgetScreenPos(object obj)
        {
            Component c = obj as Component;
            if (c == null) return 0;
            return ((c.AbsoluteY & 0xFFFF) << 16) | (c.AbsoluteX & 0xFFFF);
        }

        public int ListHitTest(object obj, int x, int y)
        {
            ElfListBox box = obj as ElfListBox;
            if (box == null) return -1;
            int dx = box.List.AbsoluteX - box.AbsoluteX;
            int dy = box.List.AbsoluteY - box.AbsoluteY;
            return box.List.GetItemIndexAt(x - dx, y - dy);
        }

        public string[] ListDrives()
        {
            try
            {
                global::System.Collections.Generic.List<string> mounts =
                    new global::System.Collections.Generic.List<string>();
                foreach (VfsMount mount in VfsManager.Mounts)
                {
                    if (mount == null || string.IsNullOrEmpty(mount.MountPoint)) continue;
                    mounts.Add(mount.MountPoint);
                }
                mounts.Sort(global::System.StringComparer.Ordinal);
                return mounts.ToArray();
            }
            catch { return null; }
        }

        public bool DriveStat(string mount, out ulong total, out ulong free)
        {
            total = 0;
            free = 0;
            try
            {
                if (string.IsNullOrEmpty(mount)) return false;
                if (!VfsManager.TryStatFs(mount, out VfsStatFs stats)) return false;
                if (stats.BlockSize == 0 || stats.AvailableBlocks > stats.Blocks ||
                    stats.Blocks > ulong.MaxValue / stats.BlockSize)
                    return false;
                total = stats.Blocks * stats.BlockSize;
                free = stats.AvailableBlocks * stats.BlockSize;
                return true;
            }
            catch { return false; }
        }

        // ---------------------------------------------------------------- files

        public string[] ListDir(string path)
        {
            try
            {
                if (!global::System.IO.Directory.Exists(path)) return null;
                string[] all = global::System.IO.Directory.GetFileSystemEntries(path);
                string[] names = new string[all.Length];
                for (int i = 0; i < all.Length; i++)
                    names[i] = global::System.IO.Path.GetFileName(all[i]);
                global::System.Array.Sort(names, global::System.StringComparer.Ordinal);
                return names;
            }
            catch { return null; }
        }

        public bool StatFile(string path, out bool isDir, out long size)
        {
            isDir = false;
            size = 0;
            try
            {
                if (global::System.IO.File.Exists(path))
                {
                    size = new global::System.IO.FileInfo(path).Length;
                    return true;
                }
                if (global::System.IO.Directory.Exists(path))
                {
                    isDir = true;
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        public bool MakeDir(string path)
        {
            try { global::System.IO.Directory.CreateDirectory(path); return true; }
            catch { return false; }
        }

        public bool DeletePath(string path)
        {
            try
            {
                if (global::System.IO.File.Exists(path)) { global::System.IO.File.Delete(path); return true; }
                if (global::System.IO.Directory.Exists(path)) { global::System.IO.Directory.Delete(path, true); return true; }
                return false;
            }
            catch { return false; }
        }

        public bool RenamePath(string oldPath, string newPath)
        {
            try
            {
                if (string.IsNullOrEmpty(newPath)) return false;
                if (global::System.IO.File.Exists(oldPath)) { global::System.IO.File.Move(oldPath, newPath); return true; }
                if (global::System.IO.Directory.Exists(oldPath)) { global::System.IO.Directory.Move(oldPath, newPath); return true; }
                return false;
            }
            catch { return false; }
        }

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
