// IElfHost.cs - the ONLY place the loader touches your OS. Implement it once (WindoseBridge.cs).
// The runner validates handles/strings/sizes and owns per-program state; you just create real objects.
using System;

namespace Windose.System.Elf
{
    public interface IElfHost
    {
        /// <summary>Create a top-level window. Return null on failure.</summary>
        object CreateWindow(int w, int h, string title);

        /// <summary>Create a button inside 'parent' (an object you returned earlier). Null on failure.</summary>
        object CreateButton(object parent, int x, int y, int w, int h, string text);

        /// <summary>Create a label inside 'parent'. Null on failure.</summary>
        object CreateLabel(object parent, int x, int y, string text);

        /// <summary>Called right after an object got its handle. Wire UI events to the owner's queue:
        /// owner.PostEvent(ElfEvent.Close / ElfEvent.Click, handle, 0, 0).</summary>
        void Bind(object obj, int handle, ElfProgram owner);

        bool SetText(object obj, string text);
        bool FillRect(object window, int x, int y, int w, int h, uint argb);

        /// <summary>Create a pixel canvas inside 'parent'. Null on failure.</summary>
        object CreateCanvas(object parent, int x, int y, int w, int h);

        /// <summary>Copy w*h ARGB pixels (len == w*h*4 bytes) into the canvas at (x, y). False on failure.</summary>
        bool DrawPixels(object canvas, int x, int y, int w, int h, IntPtr buf, int len);

        /// <summary>Resize a top-level window's client area. False on failure.</summary>
        bool ResizeWindow(object window, int w, int h);

        /// <summary>Names (not paths) inside a directory, sorted. Null if missing / not a directory.</summary>
        string[] ListDir(string path);

        /// <summary>False if the path does not exist.</summary>
        bool StatFile(string path, out bool isDir, out long size);

        /// <summary>Create a directory (parents too). False on failure.</summary>
        bool MakeDir(string path);

        /// <summary>Delete a file or directory (recursively). False on failure.</summary>
        bool DeletePath(string path);

        /// <summary>Move/rename a file or directory. False on failure (e.g. destination exists).</summary>
        bool RenamePath(string oldPath, string newPath);

        object CreateCheckbox(object parent, int x, int y, string text);
        bool SetChecked(object obj, bool value);
        /// <summary>1 checked, 0 unchecked, -1 if not a checkbox/radio.</summary>
        int GetChecked(object obj);

        object CreateRadio(object parent, int x, int y, string text, string group);
        object CreateSlider(object parent, int x, int y, int w, bool horizontal, int min, int max, int value);
        bool SliderSet(object obj, int value);
        /// <summary>Rounded value, or int.MinValue if not a slider.</summary>
        int SliderGet(object obj);

        object CreateProgress(object parent, int x, int y, int w, int h);
        bool ProgressSetup(object obj, int min, int max);
        bool ProgressSet(object obj, int value);

        object CreateTextField(object parent, int x, int y, int w);
        bool TextFieldSetup(object obj, int maxLength, int passwordChar, int readOnly);
        /// <summary>Readable text of a button/label/textfield/tab/window. Null if none.</summary>
        string GetText(object obj);

        object CreateCombo(object parent, int x, int y, int w);
        /// <summary>Returns the new item index, or -1.</summary>
        int ComboAddItem(object obj, string text);
        bool ComboRemoveItem(object obj, int index);
        void ComboClear(object obj);
        int ComboGetSelected(object obj);
        bool ComboSetSelected(object obj, int index);

        object CreateListBox(object parent, int x, int y, int w, int h);
        /// <summary>Returns the new item index, or -1.</summary>
        int ListAddItem(object obj, string text);
        /// <summary>Adds a multi-column row. Returns the index, or -1.</summary>
        int ListAddRow(object obj, string[] cells);
        bool ListRemoveItem(object obj, int index);
        void ListClear(object obj);
        int ListGetSelected(object obj);
        bool ListSelect(object obj, int index);

        object CreateTabs(object parent, int x, int y, int w, int h);
        /// <summary>Adds a page and returns it (children attach to the page handle).</summary>
        object TabAddPage(object obj, string title);
        bool TabSelect(object obj, int index);
        int TabGetSelected(object obj);

        object MenuBarCreate(object window);
        /// <summary>Adds an item to a page (created if missing). "-" adds a separator. Returns the item.</summary>
        object MenuAddItem(object menubar, string page, string text);

        /// <summary>Shows a dialog with the given buttons. The button index arrives as EV_CLICK on the dialog handle.</summary>
        object MsgBox(string title, string text, string[] buttons);

        object StatusBarCreate(object parent, int x, int y, int w);
        /// <summary>Returns the panel index, or -1.</summary>
        int StatusAddPanel(object obj, string text, int width);
        bool StatusSetText(object obj, int index, string text);

        bool WidgetSetEnabled(object obj, bool value);
        /// <summary>1 enabled, 0 disabled, -1 if not a component.</summary>
        int WidgetGetEnabled(object obj);
        bool WidgetFocus(object obj);

        bool TextFieldSelect(object obj, int start, int len);
        int TextFieldGetCaret(object obj);
        bool TextFieldSetMultiline(object obj, bool multiline);

        object ColorPickerCreate(object parent, int x, int y);
        /// <summary>ARGB as int, or 0 if not a color picker (black is 0xFF000000, never 0).</summary>
        int ColorPickerGet(object obj);
        bool ColorPickerSet(object obj, int argb);

        object NumericCreate(object parent, int x, int y, int w, int min, int max, int inc, int value);
        bool NumericSet(object obj, int value);
        int NumericGet(object obj);

        object CalendarCreate(object parent, int x, int y);
        /// <summary>Date as yyyymmdd, or 0 if not a calendar.</summary>
        int CalendarGet(object obj);
        bool CalendarSet(object obj, int year, int month, int day);

        /// <summary>Non-blocking open/save dialog. Result arrives as EV_CLICK (1 = chosen, 0 = cancelled);
        /// the path is readable with GetText on the dialog handle.</summary>
        object FileDialogOpen(string title, string initialPath, string filter, bool save);

        object GridCreate(object parent, int x, int y, int w, int h);
        /// <summary>Returns the column index, or -1.</summary>
        int GridAddColumn(object obj, string title, int width);
        /// <summary>Returns the row index, or -1.</summary>
        int GridAddRow(object obj, string[] cells);
        void GridClear(object obj);
        int GridGetSelected(object obj);
        bool GridSelect(object obj, int row);
        bool GridSetCell(object obj, int row, int col, string text);
        /// <summary>Null when out of range.</summary>
        string GridGetCell(object obj, int row, int col);
        bool GridSort(object obj, int col, bool descending);

        bool DropAccept(object obj, bool accept);

        object TreeCreate(object parent, int x, int y, int w, int h);
        /// <summary>Adds a root node. Icon path may be "" for none. Returns the node.</summary>
        object TreeAddRoot(object obj, string text, string icon);
        /// <summary>Adds a child node. Returns the node.</summary>
        object TreeAddChild(object obj, object parentItem, string text, string icon);
        void TreeClear(object obj);
        /// <summary>Handle of the selected node, or 0.</summary>
        int TreeSelectedItem(object obj);
        bool TreeSetExpanded(object obj, object node, bool expanded);

        object ToolbarCreate(object parent, int x, int y, int w);
        /// <summary>Returns the button.</summary>
        object ToolbarAddButton(object obj, string text);
        void ToolbarAddSeparator(object obj);

        /// <summary>Standalone context popup (no parent).</summary>
        object PopupCreate();
        /// <summary>"-" adds a separator. Returns the item.</summary>
        object PopupAddItem(object obj, string text);
        /// <summary>Shows the popup at screen coordinates (clamped).</summary>
        void PopupShowAt(object obj, int x, int y);

        /// <summary>Adds a details column to a listbox. Returns the index, or -1.</summary>
        int ListAddColumn(object obj, string title, int width);

        /// <summary>Screen position of a component, for placing popups. Returns (y << 16) | x.</summary>
        int WidgetScreenPos(object obj);
        /// <summary>Row index at listbox-local coordinates, or -1.</summary>
        int ListHitTest(object obj, int x, int y);

        /// <summary>Mount points of all mounted filesystems, sorted. Null on failure.</summary>
        string[] ListDrives();

        /// <summary>Total/free bytes of a mount. False on failure.</summary>
        bool DriveStat(string mount, out ulong total, out ulong free);

        /// <summary>Close/remove. Called when the program closes it, exits, or is killed - possibly twice for the
        /// same object or after its parent closed. Be idempotent.</summary>
        void Close(object obj);

        byte[] ReadFile(string path);                 // null if missing
        bool WriteFile(string path, byte[] data);

        /// <summary>A program was loaded and its thread is about to start (e.g. register it in the process list).</summary>
        void ProgramStarted(ElfProgram program);

        /// <summary>The program finished (exit, kill or crash-return) and all its resources were released.</summary>
        void ProgramExited(ElfProgram program);
    }
}
