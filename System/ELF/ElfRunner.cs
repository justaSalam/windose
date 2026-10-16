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
        public delegate* unmanaged<int, int> Yield;                                       // (pid)
        public delegate* unmanaged<int, byte*, byte*, int, int> SpawnProc;                 // (pid, path, argbuf, arglen) -> child pid
        public delegate* unmanaged<int, int, int> KillProc;                               // (pid, target) -> 0
        public delegate* unmanaged<int, int, int, int, int, int, int> CreateCanvas;        // (pid, parent, x, y, w, h)
        public delegate* unmanaged<int, int, int, int, int, int, byte*, int, int> DrawPixels; // (pid, canvas, x, y, w, h, buf, len)
        public delegate* unmanaged<int, int, int, int, int> ResizeWindow;                 // (pid, win, w, h)
        public delegate* unmanaged<int, byte*, byte*, int, int> ListDir;                   // (pid, path, buf, max) -> entry count
        public delegate* unmanaged<int, byte*, ElfFileStat*, int> StatFile;                 // (pid, path, out) -> 0
        public delegate* unmanaged<int, byte*, int> MakeDir;                                // (pid, path) -> 0
        public delegate* unmanaged<int, byte*, int> DeletePath;                             // (pid, path) -> 0
        public delegate* unmanaged<int, byte*, byte*, int> RenamePath;                     // (pid, oldpath, newpath) -> 0
        public delegate* unmanaged<int, int, int, int, byte*, int> CreateCheckbox;          // (pid, parent, x, y, text)
        public delegate* unmanaged<int, int, int, int> SetChecked;                          // (pid, handle, checked)
        public delegate* unmanaged<int, int, int> GetChecked;                               // (pid, handle) -> 0/1
        public delegate* unmanaged<int, int, int, int, byte*, byte*, int> CreateRadio;     // (pid, parent, x, y, text, group)
        public delegate* unmanaged<int, int, int, int, int, int, int, int, int, int> CreateSlider; // (pid, parent, x, y, w, horiz, min, max, value)
        public delegate* unmanaged<int, int, int, int> SliderSet;                           // (pid, handle, value)
        public delegate* unmanaged<int, int, int> SliderGet;                                // (pid, handle) -> value
        public delegate* unmanaged<int, int, int, int, int, int, int> CreateProgress;      // (pid, parent, x, y, w, h)
        public delegate* unmanaged<int, int, int, int, int> ProgressSetup;                  // (pid, handle, min, max)
        public delegate* unmanaged<int, int, int, int> ProgressSet;                         // (pid, handle, value)
        public delegate* unmanaged<int, int, int, int, int, int> CreateTextField;                // (pid, parent, x, y, w)
        public delegate* unmanaged<int, int, int, int, int, int> TextFieldSetup;            // (pid, handle, maxlen, password, readonly)
        public delegate* unmanaged<int, int, byte*, int, int> GetText;                      // (pid, handle, buf, max) -> len
        public delegate* unmanaged<int, int, int, int, int, int> CreateCombo;                   // (pid, parent, x, y, w)
        public delegate* unmanaged<int, int, byte*, int> ComboAddItem;                      // (pid, handle, text) -> index
        public delegate* unmanaged<int, int, int, int> ComboRemoveItem;                     // (pid, handle, index)
        public delegate* unmanaged<int, int, int> ComboClear;                               // (pid, handle)
        public delegate* unmanaged<int, int, int> ComboGetSelected;                         // (pid, handle) -> index
        public delegate* unmanaged<int, int, int, int> ComboSetSelected;                    // (pid, handle, index)
        public delegate* unmanaged<int, int, int, int, int, int, int> CreateListBox;        // (pid, parent, x, y, w, h)
        public delegate* unmanaged<int, int, byte*, int> ListAddItem;                       // (pid, handle, text) -> index
        public delegate* unmanaged<int, int, int, int> ListRemoveItem;                      // (pid, handle, index)
        public delegate* unmanaged<int, int, int> ListClear;                                // (pid, handle)
        public delegate* unmanaged<int, int, int> ListGetSelected;                          // (pid, handle) -> index
        public delegate* unmanaged<int, int, int, int> ListSelect;                          // (pid, handle, index)
        public delegate* unmanaged<int, int, byte*, int, int> ListAddRow;                     // (pid, handle, buf, len) -> index
        public delegate* unmanaged<int, int, int, int, int, int, int> CreateTabs;          // (pid, parent, x, y, w, h)
        public delegate* unmanaged<int, int, byte*, int> TabAddPage;                        // (pid, handle, title) -> page handle
        public delegate* unmanaged<int, int, int, int> TabSelect;                           // (pid, handle, index)
        public delegate* unmanaged<int, int, int> TabGetSelected;                           // (pid, handle) -> index
        public delegate* unmanaged<int, int, int> MenuBarCreate;                            // (pid, window) -> handle
        public delegate* unmanaged<int, int, byte*, byte*, int> MenuAddItem;                // (pid, handle, page, text) -> item handle
        public delegate* unmanaged<int, byte*, byte*, byte*, int, int> MsgBox;              // (pid, title, text, buttons, len) -> dlg handle
        public delegate* unmanaged<int, int, int, int, int, int> StatusBarCreate;           // (pid, parent, x, y, w)
        public delegate* unmanaged<int, int, byte*, int, int> StatusAddPanel;               // (pid, handle, text, width) -> index
        public delegate* unmanaged<int, int, int, byte*, int> StatusSetText;                // (pid, handle, index, text)
        public delegate* unmanaged<int, int, int, int> WidgetSetEnabled;                    // (pid, handle, enabled)
        public delegate* unmanaged<int, int, int> WidgetGetEnabled;                         // (pid, handle) -> 0/1
        public delegate* unmanaged<int, int, int> WidgetFocus;                              // (pid, handle)
        public delegate* unmanaged<int, int, int, int, int> TextFieldSelect;                // (pid, handle, start, len)
        public delegate* unmanaged<int, int, int> TextFieldGetCaret;                        // (pid, handle) -> caret
        public delegate* unmanaged<int, int, int, int> TextFieldSetMultiline;               // (pid, handle, multiline)
        public delegate* unmanaged<int, int, int, int, int> ColorPickerCreate;                   // (pid, parent, x, y)
        public delegate* unmanaged<int, int, int> ColorPickerGet;                           // (pid, handle) -> ARGB
        public delegate* unmanaged<int, int, int, int> ColorPickerSet;                      // (pid, handle, argb)
        public delegate* unmanaged<int, int, int, int, int, int, int, int, int, int> NumericCreate; // (pid, parent, x, y, w, min, max, inc, value)
        public delegate* unmanaged<int, int, int, int> NumericSet;                           // (pid, handle, value)
        public delegate* unmanaged<int, int, int> NumericGet;                                // (pid, handle) -> value
        public delegate* unmanaged<int, int, int, int, int> CalendarCreate;                 // (pid, parent, x, y)
        public delegate* unmanaged<int, int, int> CalendarGet;                              // (pid, handle) -> yyyymmdd
        public delegate* unmanaged<int, int, int, int, int, int> CalendarSet;                // (pid, handle, y, m, d)
        public delegate* unmanaged<int, byte*, byte*, byte*, int, int> FileDialogOpen;      // (pid, title, initial, filter, save) -> dlg
        public delegate* unmanaged<int, int, int, int, int, int, int> GridCreate;           // (pid, parent, x, y, w, h)
        public delegate* unmanaged<int, int, byte*, int, int> GridAddColumn;                // (pid, handle, title, width) -> index
        public delegate* unmanaged<int, int, byte*, int, int> GridAddRow;                    // (pid, handle, buf, len) -> index
        public delegate* unmanaged<int, int, int> GridClear;                                // (pid, handle)
        public delegate* unmanaged<int, int, int> GridGetSelected;                          // (pid, handle) -> row
        public delegate* unmanaged<int, int, int, int> GridSelect;                          // (pid, handle, row)
        public delegate* unmanaged<int, int, int, int, byte*, int> GridSetCell;             // (pid, handle, row, col, text)
        public delegate* unmanaged<int, int, int, int, byte*, int, int> GridGetCell;        // (pid, handle, row, col, buf, max) -> len
        public delegate* unmanaged<int, int, int, int, int> GridSort;                       // (pid, handle, col, descending)
        public delegate* unmanaged<int, int, int, int> DropAccept;                          // (pid, window, enabled)
        public delegate* unmanaged<int, byte*, int, int> DndGetText;                        // (pid, buf, max) -> len
        public delegate* unmanaged<int, int, int, int, int, int, int> TreeCreate;           // (pid, parent, x, y, w, h)
        public delegate* unmanaged<int, int, byte*, byte*, int> TreeAddRoot;                // (pid, handle, text, icon) -> item
        public delegate* unmanaged<int, int, int, byte*, byte*, int> TreeAddChild;          // (pid, handle, item, text, icon) -> item
        public delegate* unmanaged<int, int, int> TreeClear;                                // (pid, handle)
        public delegate* unmanaged<int, int, int> TreeSelectedItem;                         // (pid, handle) -> item
        public delegate* unmanaged<int, int, int, int, int> TreeSetExpanded;                // (pid, handle, item, expanded)
        public delegate* unmanaged<int, int, int, int, int, int> ToolbarCreate;             // (pid, parent, x, y, w)
        public delegate* unmanaged<int, int, byte*, int> ToolbarAddButton;                  // (pid, handle, text) -> btn
        public delegate* unmanaged<int, int, int> ToolbarAddSeparator;                      // (pid, handle)
        public delegate* unmanaged<int, int> PopupCreate;                                   // (pid) -> popup
        public delegate* unmanaged<int, int, byte*, int> PopupAddItem;                      // (pid, handle, text) -> item
        public delegate* unmanaged<int, int, int, int, int> PopupShowAt;                    // (pid, handle, x, y)
        public delegate* unmanaged<int, int, byte*, int, int> ListAddColumn;                 // (pid, handle, title, width) -> index
        public delegate* unmanaged<int, int, int> WidgetScreenPos;                          // (pid, handle) -> (y << 16) | x
        public delegate* unmanaged<int, int, int, int, int> ListHitTest;                      // (pid, handle, x, y) -> index
        public delegate* unmanaged<int, byte*, int, int> ListDrives;                          // (pid, buf, max) -> count
        public delegate* unmanaged<int, byte*, DriveStat*, int> DriveStat;                    // (pid, mount, out) -> 0
        public delegate* unmanaged<int, byte*, byte*, int> LaunchApp;                         // (pid, name, arg) -> 0
        public delegate* unmanaged<int, byte*, int> OpenFile;                                 // (pid, path) -> 0
        public delegate* unmanaged<int, byte*, byte*, int> NotifySend;                        // (pid, title, text) -> 0
        public delegate* unmanaged<int, int> DesktopCount;                                    // (pid) -> count
        public delegate* unmanaged<int, int> DesktopCurrent;                                  // (pid) -> index
        public delegate* unmanaged<int, int, int> DesktopSwitch;                              // (pid, index) -> 0
        public delegate* unmanaged<int, int, int, int> WindowSetDesktop;                      // (pid, win, index) -> 0
        public delegate* unmanaged<int, byte*, int> TrashDelete;                              // (pid, path) -> 0
        public delegate* unmanaged<int, byte*, int, int> TrashList;                           // (pid, buf, max) -> count
        public delegate* unmanaged<int, byte*, int> TrashRestore;                             // (pid, name) -> 0
        public delegate* unmanaged<int, int> TrashEmpty;                                      // (pid)
    }

    /// <summary>Matches DriveStat in programs/include/kernel.h. int-only so it can cross the unmanaged boundary.</summary>
    public struct DriveStat
    {
        public int TotalLo;
        public int TotalHi;
        public int FreeLo;
        public int FreeHi;
    }

    /// <summary>Matches FileStat in programs/include/kernel.h. int-only so it can cross the unmanaged boundary.</summary>
    public struct ElfFileStat
    {
        public int IsDir;
        public int SizeLo;
        public int SizeHi;
    }

    public static unsafe class ElfRunner
    {
        public const int ApiVersion = 10;
        private const int KKilled = -100;
        private const int MaxStr = 260;
        private const int MaxIo = 1024 * 1024;
        private const int MaxObjectsPerProgram = 256;
        private const int MaxCanvasDim = 2048;
        private const int MaxSpawnArgs = 32;

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
            api.Yield = &ApiYield;
            api.SpawnProc = &ApiSpawn;
            api.KillProc = &ApiKill;
            api.CreateCanvas = &ApiCreateCanvas;
            api.DrawPixels = &ApiDrawPixels;
            api.ResizeWindow = &ApiResizeWindow;
            api.ListDir = &ApiListDir;
            api.StatFile = &ApiStatFile;
            api.MakeDir = &ApiMakeDir;
            api.DeletePath = &ApiDeletePath;
            api.RenamePath = &ApiRenamePath;
            api.CreateCheckbox = &ApiCreateCheckbox;
            api.SetChecked = &ApiSetChecked;
            api.GetChecked = &ApiGetChecked;
            api.CreateRadio = &ApiCreateRadio;
            api.CreateSlider = &ApiCreateSlider;
            api.SliderSet = &ApiSliderSet;
            api.SliderGet = &ApiSliderGet;
            api.CreateProgress = &ApiCreateProgress;
            api.ProgressSetup = &ApiProgressSetup;
            api.ProgressSet = &ApiProgressSet;
            api.CreateTextField = &ApiCreateTextField;
            api.TextFieldSetup = &ApiTextFieldSetup;
            api.GetText = &ApiGetText;
            api.CreateCombo = &ApiCreateCombo;
            api.ComboAddItem = &ApiComboAddItem;
            api.ComboRemoveItem = &ApiComboRemoveItem;
            api.ComboClear = &ApiComboClear;
            api.ComboGetSelected = &ApiComboGetSelected;
            api.ComboSetSelected = &ApiComboSetSelected;
            api.CreateListBox = &ApiCreateListBox;
            api.ListAddItem = &ApiListAddItem;
            api.ListRemoveItem = &ApiListRemoveItem;
            api.ListClear = &ApiListClear;
            api.ListGetSelected = &ApiListGetSelected;
            api.ListSelect = &ApiListSelect;
            api.ListAddRow = &ApiListAddRow;
            api.CreateTabs = &ApiCreateTabs;
            api.TabAddPage = &ApiTabAddPage;
            api.TabSelect = &ApiTabSelect;
            api.TabGetSelected = &ApiTabGetSelected;
            api.MenuBarCreate = &ApiMenuBarCreate;
            api.MenuAddItem = &ApiMenuAddItem;
            api.MsgBox = &ApiMsgBox;
            api.StatusBarCreate = &ApiStatusBarCreate;
            api.StatusAddPanel = &ApiStatusAddPanel;
            api.StatusSetText = &ApiStatusSetText;
            api.WidgetSetEnabled = &ApiWidgetSetEnabled;
            api.WidgetGetEnabled = &ApiWidgetGetEnabled;
            api.WidgetFocus = &ApiWidgetFocus;
            api.TextFieldSelect = &ApiTextFieldSelect;
            api.TextFieldGetCaret = &ApiTextFieldGetCaret;
            api.TextFieldSetMultiline = &ApiTextFieldSetMultiline;
            api.ColorPickerCreate = &ApiColorPickerCreate;
            api.ColorPickerGet = &ApiColorPickerGet;
            api.ColorPickerSet = &ApiColorPickerSet;
            api.NumericCreate = &ApiNumericCreate;
            api.NumericSet = &ApiNumericSet;
            api.NumericGet = &ApiNumericGet;
            api.CalendarCreate = &ApiCalendarCreate;
            api.CalendarGet = &ApiCalendarGet;
            api.CalendarSet = &ApiCalendarSet;
            api.FileDialogOpen = &ApiFileDialogOpen;
            api.GridCreate = &ApiGridCreate;
            api.GridAddColumn = &ApiGridAddColumn;
            api.GridAddRow = &ApiGridAddRow;
            api.GridClear = &ApiGridClear;
            api.GridGetSelected = &ApiGridGetSelected;
            api.GridSelect = &ApiGridSelect;
            api.GridSetCell = &ApiGridSetCell;
            api.GridGetCell = &ApiGridGetCell;
            api.GridSort = &ApiGridSort;
            api.DropAccept = &ApiDropAccept;
            api.DndGetText = &ApiDndGetText;
            api.TreeCreate = &ApiTreeCreate;
            api.TreeAddRoot = &ApiTreeAddRoot;
            api.TreeAddChild = &ApiTreeAddChild;
            api.TreeClear = &ApiTreeClear;
            api.TreeSelectedItem = &ApiTreeSelectedItem;
            api.TreeSetExpanded = &ApiTreeSetExpanded;
            api.ToolbarCreate = &ApiToolbarCreate;
            api.ToolbarAddButton = &ApiToolbarAddButton;
            api.ToolbarAddSeparator = &ApiToolbarAddSeparator;
            api.PopupCreate = &ApiPopupCreate;
            api.PopupAddItem = &ApiPopupAddItem;
            api.PopupShowAt = &ApiPopupShowAt;
            api.ListAddColumn = &ApiListAddColumn;
            api.WidgetScreenPos = &ApiWidgetScreenPos;
            api.ListHitTest = &ApiListHitTest;
            api.ListDrives = &ApiListDrives;
            api.DriveStat = &ApiDriveStat;
            api.LaunchApp = &ApiLaunchApp;
            api.OpenFile = &ApiOpenFile;
            api.NotifySend = &ApiNotifySend;
            api.DesktopCount = &ApiDesktopCount;
            api.DesktopCurrent = &ApiDesktopCurrent;
            api.DesktopSwitch = &ApiDesktopSwitch;
            api.WindowSetDesktop = &ApiWindowSetDesktop;
            api.TrashDelete = &ApiTrashDelete;
            api.TrashList = &ApiTrashList;
            api.TrashRestore = &ApiTrashRestore;
            api.TrashEmpty = &ApiTrashEmpty;
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

        [UnmanagedCallersOnly]
        private static int ApiYield(int pid)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                Thread.Sleep(0);
                return 0;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiSpawn(int pid, byte* path, byte* argbuf, int arglen)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                IElfHost host = Host;
                if (host == null || path == null || argbuf == null || arglen < 0 || arglen > 8192) return -1;
                string file = CStr(path);
                if (file.Length == 0) return -1;
                string[] extra = SplitArgs(argbuf, arglen);
                if (extra == null) return -1;
                byte[] data = host.ReadFile(file);
                if (data == null) return -1;
                string[] args = new string[extra.Length + 1];
                args[0] = file;
                for (int i = 0; i < extra.Length; i++) args[i + 1] = extra[i];
                ElfProgram child = Spawn(data, file, args, out string error);
                if (child == null) return -2;
                return child.Pid;
            }
            catch { return -2; }
        }

        private static string[] SplitArgs(byte* buf, int len)
        {
            string[] tmp = new string[MaxSpawnArgs];
            int count = 0, start = 0;
            for (int i = 0; i <= len; i++)
            {
                if (i == len || buf[i] == 0)
                {
                    int slen = i - start;
                    if (slen > 0)
                    {
                        if (slen > MaxStr || count >= MaxSpawnArgs) return null;
                        char[] c = new char[slen];
                        for (int j = 0; j < slen; j++) c[j] = (char)buf[start + j];
                        tmp[count++] = new string(c);
                    }
                    start = i + 1;
                }
            }
            string[] r = new string[count];
            for (int i = 0; i < count; i++) r[i] = tmp[i];
            return r;
        }

        [UnmanagedCallersOnly]
        private static int ApiKill(int pid, int target)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (target <= 0) return -1;
                ElfProgram t = Get(target);
                if (t == null) return -1;
                t.RequestKill();
                return target == pid ? KKilled : 0;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiCreateCanvas(int pid, int parent, int x, int y, int w, int h)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                IElfHost host = Host;
                if (host == null || p.Objs.Count >= MaxObjectsPerProgram) return 0;
                if (w < 1 || h < 1 || w > MaxCanvasDim || h > MaxCanvasDim) return 0;
                object par = p.Objs.Get(parent);
                if (par == null) return 0;
                object c = host.CreateCanvas(par, x, y, w, h);
                if (c == null) return 0;
                int handle = p.Objs.Add(c);
                if (handle == 0) { host.Close(c); return 0; }
                host.Bind(c, handle, p);
                return handle;
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiDrawPixels(int pid, int handle, int x, int y, int w, int h, byte* buf, int len)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null || buf == null) return -1;
                if (w < 1 || h < 1 || w > MaxCanvasDim || h > MaxCanvasDim) return -1;
                if (len != w * h * 4) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.DrawPixels(o, x, y, w, h, (IntPtr)buf, len) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiResizeWindow(int pid, int handle, int w, int h)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                if (w < 1 || h < 1 || w > 4096 || h > 4096) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.ResizeWindow(o, w, h) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiListDir(int pid, byte* path, byte* buf, int max)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null || buf == null || max <= 0 || max > MaxIo) return -1;
                string[] names = Host.ListDir(CStr(path));
                if (names == null) return -1;
                long total = 0;
                for (int i = 0; i < names.Length; i++) total += names[i].Length + 1;
                if (total > max) return -1;
                int o = 0;
                for (int i = 0; i < names.Length; i++)
                {
                    string n = names[i];
                    for (int j = 0; j < n.Length; j++) buf[o++] = (byte)n[j];
                    buf[o++] = 0;
                }
                return names.Length;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiStatFile(int pid, byte* path, ElfFileStat* outStat)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null || outStat == null) return -1;
                if (!Host.StatFile(CStr(path), out bool isDir, out long size)) return -1;
                outStat->IsDir = isDir ? 1 : 0;
                outStat->SizeLo = (int)(uint)size;
                outStat->SizeHi = (int)(uint)(size >> 32);
                return 0;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiMakeDir(int pid, byte* path)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                return Host.MakeDir(CStr(path)) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiDeletePath(int pid, byte* path)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                return Host.DeletePath(CStr(path)) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiRenamePath(int pid, byte* oldpath, byte* newpath)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                return Host.RenamePath(CStr(oldpath), CStr(newpath)) ? 0 : -3;
            }
            catch { return -2; }
        }

        private static object LiveParent(ElfProgram p, int parent)
        {
            if (p.Objs.Count >= MaxObjectsPerProgram) return null;
            return p.Objs.Get(parent);
        }

        private static int TrackObject(ElfProgram p, object o)
        {
            if (o == null) return 0;
            int handle = p.Objs.Add(o);
            if (handle == 0)
            {
                if (Host != null) Host.Close(o);
                return 0;
            }
            if (Host != null) Host.Bind(o, handle, p);
            return handle;
        }

        [UnmanagedCallersOnly]
        private static int ApiCreateCheckbox(int pid, int parent, int x, int y, byte* text)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object par = LiveParent(p, parent);
                if (par == null) return 0;
                return TrackObject(p, Host.CreateCheckbox(par, x, y, CStr(text)));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiSetChecked(int pid, int handle, int v)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.SetChecked(o, v != 0) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiGetChecked(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.GetChecked(o);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiCreateRadio(int pid, int parent, int x, int y, byte* text, byte* group)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object par = LiveParent(p, parent);
                if (par == null) return 0;
                return TrackObject(p, Host.CreateRadio(par, x, y, CStr(text), CStr(group)));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiCreateSlider(int pid, int parent, int x, int y, int w, int horiz, int min, int max, int value)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object par = LiveParent(p, parent);
                if (par == null) return 0;
                return TrackObject(p, Host.CreateSlider(par, x, y, w, horiz != 0, min, max, value));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiSliderSet(int pid, int handle, int v)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.SliderSet(o, v) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiSliderGet(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.SliderGet(o);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiCreateProgress(int pid, int parent, int x, int y, int w, int h)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object par = LiveParent(p, parent);
                if (par == null) return 0;
                return TrackObject(p, Host.CreateProgress(par, x, y, w, h));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiProgressSetup(int pid, int handle, int min, int max)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.ProgressSetup(o, min, max) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiProgressSet(int pid, int handle, int v)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.ProgressSet(o, v) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiCreateTextField(int pid, int parent, int x, int y, int w)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object par = LiveParent(p, parent);
                if (par == null) return 0;
                return TrackObject(p, Host.CreateTextField(par, x, y, w));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTextFieldSetup(int pid, int handle, int maxlen, int password, int ro)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.TextFieldSetup(o, maxlen, password, ro) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiGetText(int pid, int handle, byte* buf, int max)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null || buf == null || max <= 0 || max > MaxIo) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                string s = Host.GetText(o);
                if (s == null) return -1;
                if (s.Length + 1 > max) return -1;
                int i = 0;
                for (; i < s.Length; i++) buf[i] = (byte)s[i];
                buf[i] = 0;
                return s.Length;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiCreateCombo(int pid, int parent, int x, int y, int w)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object par = LiveParent(p, parent);
                if (par == null) return 0;
                return TrackObject(p, Host.CreateCombo(par, x, y, w));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiComboAddItem(int pid, int handle, byte* text)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.ComboAddItem(o, CStr(text));
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiComboRemoveItem(int pid, int handle, int index)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.ComboRemoveItem(o, index) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiComboClear(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                Host.ComboClear(o);
                return 0;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiComboGetSelected(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.ComboGetSelected(o);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiComboSetSelected(int pid, int handle, int index)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.ComboSetSelected(o, index) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiCreateListBox(int pid, int parent, int x, int y, int w, int h)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object par = LiveParent(p, parent);
                if (par == null) return 0;
                return TrackObject(p, Host.CreateListBox(par, x, y, w, h));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiListAddItem(int pid, int handle, byte* text)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.ListAddItem(o, CStr(text));
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiListRemoveItem(int pid, int handle, int index)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.ListRemoveItem(o, index) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiListClear(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                Host.ListClear(o);
                return 0;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiListGetSelected(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.ListGetSelected(o);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiListSelect(int pid, int handle, int index)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.ListSelect(o, index) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiListAddRow(int pid, int handle, byte* buf, int len)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                if (buf == null || len < 0 || len > MaxIo) return -1;
                string[] cells = SplitArgs(buf, len);
                if (cells == null) return -1;
                return Host.ListAddRow(o, cells);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiCreateTabs(int pid, int parent, int x, int y, int w, int h)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object par = LiveParent(p, parent);
                if (par == null) return 0;
                return TrackObject(p, Host.CreateTabs(par, x, y, w, h));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTabAddPage(int pid, int handle, byte* title)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object o = p.Objs.Get(handle);
                if (o == null) return 0;
                object page = Host.TabAddPage(o, CStr(title));
                if (page == null) return 0;
                if (p.Objs.Count >= MaxObjectsPerProgram) { Host.Close(page); return 0; }
                return TrackObject(p, page);
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTabSelect(int pid, int handle, int index)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.TabSelect(o, index) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTabGetSelected(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.TabGetSelected(o);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiMenuBarCreate(int pid, int window)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object par = LiveParent(p, window);
                if (par == null) return 0;
                return TrackObject(p, Host.MenuBarCreate(par));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiMenuAddItem(int pid, int handle, byte* page, byte* text)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object o = p.Objs.Get(handle);
                if (o == null) return 0;
                object item = Host.MenuAddItem(o, CStr(page), CStr(text));
                if (item == null) return 0;
                if (p.Objs.Count >= MaxObjectsPerProgram) { Host.Close(item); return 0; }
                return TrackObject(p, item);
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiMsgBox(int pid, byte* title, byte* text, byte* buttons, int len)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                if (p.Objs.Count >= MaxObjectsPerProgram) return 0;
                if (buttons == null || len < 0 || len > 1024) return 0;
                string[] labels = SplitArgs(buttons, len);
                if (labels == null || labels.Length == 0) return 0;
                return TrackObject(p, Host.MsgBox(CStr(title), CStr(text), labels));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiStatusBarCreate(int pid, int parent, int x, int y, int w)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object par = LiveParent(p, parent);
                if (par == null) return 0;
                return TrackObject(p, Host.StatusBarCreate(par, x, y, w));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiStatusAddPanel(int pid, int handle, byte* text, int width)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.StatusAddPanel(o, CStr(text), width);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiStatusSetText(int pid, int handle, int index, byte* text)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.StatusSetText(o, index, CStr(text)) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiWidgetSetEnabled(int pid, int handle, int v)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.WidgetSetEnabled(o, v != 0) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiWidgetGetEnabled(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.WidgetGetEnabled(o);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiWidgetFocus(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.WidgetFocus(o) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTextFieldSelect(int pid, int handle, int start, int len)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.TextFieldSelect(o, start, len) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTextFieldGetCaret(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.TextFieldGetCaret(o);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTextFieldSetMultiline(int pid, int handle, int v)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.TextFieldSetMultiline(o, v != 0) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiColorPickerCreate(int pid, int parent, int x, int y)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object par = LiveParent(p, parent);
                if (par == null) return 0;
                return TrackObject(p, Host.ColorPickerCreate(par, x, y));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiColorPickerGet(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.ColorPickerGet(o);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiColorPickerSet(int pid, int handle, int argb)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.ColorPickerSet(o, argb) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiNumericCreate(int pid, int parent, int x, int y, int w, int min, int max, int inc, int value)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object par = LiveParent(p, parent);
                if (par == null) return 0;
                return TrackObject(p, Host.NumericCreate(par, x, y, w, min, max, inc, value));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiNumericSet(int pid, int handle, int v)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.NumericSet(o, v) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiNumericGet(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.NumericGet(o);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiCalendarCreate(int pid, int parent, int x, int y)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object par = LiveParent(p, parent);
                if (par == null) return 0;
                return TrackObject(p, Host.CalendarCreate(par, x, y));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiCalendarGet(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.CalendarGet(o);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiCalendarSet(int pid, int handle, int y, int m, int d)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.CalendarSet(o, y, m, d) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiFileDialogOpen(int pid, byte* title, byte* initial, byte* filter, int save)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                if (p.Objs.Count >= MaxObjectsPerProgram) return 0;
                return TrackObject(p, Host.FileDialogOpen(CStr(title), CStr(initial), CStr(filter), save != 0));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiGridCreate(int pid, int parent, int x, int y, int w, int h)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object par = LiveParent(p, parent);
                if (par == null) return 0;
                return TrackObject(p, Host.GridCreate(par, x, y, w, h));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiGridAddColumn(int pid, int handle, byte* title, int width)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.GridAddColumn(o, CStr(title), width);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiGridAddRow(int pid, int handle, byte* buf, int len)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                if (buf == null || len < 0 || len > MaxIo) return -1;
                string[] cells = SplitArgs(buf, len);
                if (cells == null) return -1;
                return Host.GridAddRow(o, cells);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiGridClear(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                Host.GridClear(o);
                return 0;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiGridGetSelected(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.GridGetSelected(o);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiGridSelect(int pid, int handle, int row)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.GridSelect(o, row) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiGridSetCell(int pid, int handle, int row, int col, byte* text)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.GridSetCell(o, row, col, CStr(text)) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiGridGetCell(int pid, int handle, int row, int col, byte* buf, int max)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null || buf == null || max <= 0 || max > MaxIo) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                string s = Host.GridGetCell(o, row, col);
                if (s == null) return -1;
                if (s.Length + 1 > max) return -1;
                int i = 0;
                for (; i < s.Length; i++) buf[i] = (byte)s[i];
                buf[i] = 0;
                return s.Length;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiGridSort(int pid, int handle, int col, int descending)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.GridSort(o, col, descending != 0) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiDropAccept(int pid, int handle, int v)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.DropAccept(o, v != 0) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiDndGetText(int pid, byte* buf, int max)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null || buf == null || max <= 0 || max > MaxIo) return -1;
                string s = p.LastDropText ?? "";
                if (s.Length + 1 > max) return -1;
                int i = 0;
                for (; i < s.Length; i++) buf[i] = (byte)s[i];
                buf[i] = 0;
                return s.Length;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTreeCreate(int pid, int parent, int x, int y, int w, int h)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object par = LiveParent(p, parent);
                if (par == null) return 0;
                return TrackObject(p, Host.TreeCreate(par, x, y, w, h));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTreeAddRoot(int pid, int handle, byte* text, byte* icon)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object o = p.Objs.Get(handle);
                if (o == null) return 0;
                object item = Host.TreeAddRoot(o, CStr(text), CStr(icon));
                if (item == null) return 0;
                if (p.Objs.Count >= MaxObjectsPerProgram) { Host.Close(item); return 0; }
                return TrackObject(p, item);
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTreeAddChild(int pid, int handle, int item, byte* text, byte* icon)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object o = p.Objs.Get(handle);
                if (o == null) return 0;
                object par = p.Objs.Get(item);
                if (par == null) return 0;
                object child = Host.TreeAddChild(o, par, CStr(text), CStr(icon));
                if (child == null) return 0;
                if (p.Objs.Count >= MaxObjectsPerProgram) { Host.Close(child); return 0; }
                return TrackObject(p, child);
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTreeClear(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                Host.TreeClear(o);
                return 0;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTreeSelectedItem(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.TreeSelectedItem(o);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTreeSetExpanded(int pid, int handle, int item, int v)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                object node = p.Objs.Get(item);
                if (node == null) return -1;
                return Host.TreeSetExpanded(o, node, v != 0) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiToolbarCreate(int pid, int parent, int x, int y, int w)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object par = LiveParent(p, parent);
                if (par == null) return 0;
                return TrackObject(p, Host.ToolbarCreate(par, x, y, w));
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiToolbarAddButton(int pid, int handle, byte* text)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object o = p.Objs.Get(handle);
                if (o == null) return 0;
                object b = Host.ToolbarAddButton(o, CStr(text));
                if (b == null) return 0;
                if (p.Objs.Count >= MaxObjectsPerProgram) { Host.Close(b); return 0; }
                return TrackObject(p, b);
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiToolbarAddSeparator(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                Host.ToolbarAddSeparator(o);
                return 0;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiPopupCreate(int pid)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                if (p.Objs.Count >= MaxObjectsPerProgram) return 0;
                return TrackObject(p, Host.PopupCreate());
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiPopupAddItem(int pid, int handle, byte* text)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return 0;
                object o = p.Objs.Get(handle);
                if (o == null) return 0;
                object item = Host.PopupAddItem(o, CStr(text));
                if (item == null) return 0;
                if (p.Objs.Count >= MaxObjectsPerProgram) { Host.Close(item); return 0; }
                return TrackObject(p, item);
            }
            catch { return 0; }
        }

        [UnmanagedCallersOnly]
        private static int ApiPopupShowAt(int pid, int handle, int x, int y)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                Host.PopupShowAt(o, x, y);
                return 0;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiListAddColumn(int pid, int handle, byte* title, int width)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.ListAddColumn(o, CStr(title), width);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiWidgetScreenPos(int pid, int handle)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.WidgetScreenPos(o);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiListHitTest(int pid, int handle, int x, int y)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.ListHitTest(o, x, y);
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiListDrives(int pid, byte* buf, int max)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null || buf == null || max <= 0 || max > MaxIo) return -1;
                string[] mounts = Host.ListDrives();
                if (mounts == null) return -1;
                long total = 0;
                for (int i = 0; i < mounts.Length; i++) total += mounts[i].Length + 1;
                if (total > max) return -1;
                int o = 0;
                for (int i = 0; i < mounts.Length; i++)
                {
                    string n = mounts[i];
                    for (int j = 0; j < n.Length; j++) buf[o++] = (byte)n[j];
                    buf[o++] = 0;
                }
                return mounts.Length;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiDriveStat(int pid, byte* mount, DriveStat* outStat)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null || outStat == null) return -1;
                if (!Host.DriveStat(CStr(mount), out ulong total, out ulong free)) return -1;
                outStat->TotalLo = (int)(uint)total;
                outStat->TotalHi = (int)(uint)(total >> 32);
                outStat->FreeLo = (int)(uint)free;
                outStat->FreeHi = (int)(uint)(free >> 32);
                return 0;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiLaunchApp(int pid, byte* name, byte* arg)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                return Host.LaunchApp(CStr(name), CStr(arg)) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiOpenFile(int pid, byte* path)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                return Host.OpenFile(CStr(path)) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiNotifySend(int pid, byte* title, byte* text)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                Host.NotifySend(CStr(title), CStr(text));
                return 0;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiDesktopCount(int pid)
        {
            try
            {
                if (Live(pid) == null) return KKilled;
                return Host != null ? Host.DesktopCount() : -1;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiDesktopCurrent(int pid)
        {
            try
            {
                if (Live(pid) == null) return KKilled;
                return Host != null ? Host.DesktopCurrent() : -1;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiDesktopSwitch(int pid, int index)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                return Host.DesktopSwitch(index) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiWindowSetDesktop(int pid, int handle, int index)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                object o = p.Objs.Get(handle);
                if (o == null) return -1;
                return Host.WindowSetDesktop(o, index) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTrashDelete(int pid, byte* path)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                return Host.TrashDelete(CStr(path)) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTrashList(int pid, byte* buf, int max)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null || buf == null || max <= 0 || max > MaxIo) return -1;
                string[] names = Host.TrashList();
                if (names == null) return -1;
                long total = 0;
                for (int i = 0; i < names.Length; i++) total += names[i].Length + 1;
                if (total > max) return -1;
                int o = 0;
                for (int i = 0; i < names.Length; i++)
                {
                    string n = names[i];
                    for (int j = 0; j < n.Length; j++) buf[o++] = (byte)n[j];
                    buf[o++] = 0;
                }
                return names.Length;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTrashRestore(int pid, byte* name)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                return Host.TrashRestore(CStr(name)) ? 0 : -3;
            }
            catch { return -2; }
        }

        [UnmanagedCallersOnly]
        private static int ApiTrashEmpty(int pid)
        {
            try
            {
                ElfProgram p = Live(pid); if (p == null) return KKilled;
                if (Host == null) return -1;
                Host.TrashEmpty();
                return 0;
            }
            catch { return -2; }
        }
    }
}
