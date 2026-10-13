// IElfHost.cs - the ONLY place ElfRunner touches your OS. Implement it once (WindoseBridge.cs).
// ElfRunner validates handles/strings/sizes; you just create real objects.
namespace Windose.System.Elf
{

    /// <summary>
    /// Interface for the ELF host bridge. Implement this interface to connect ElfRunner to your Kernel.
    /// </summary>
    public interface IElfHost
    {
        /// <summary>Create a top-level window. Return null on failure.</summary>
        object CreateWindow(int w, int h, string title);

        /// <summary>Create a button inside 'parent' (a Window/Panel you returned earlier). Null on failure.</summary>
        object CreateButton(object parent, int x, int y, int w, int h, string text);

        /// <summary>Create a label inside 'parent'. Null on failure.</summary>
        object CreateLabel(object parent, int x, int y, string text);

        /// <summary>Called right after an object got its handle. Store the handle on the object and wire events:
        /// window close -> ElfEvents.Push(ElfEvent.Close, handle, 0, 0); button click -> ElfEvents.Push(ElfEvent.Click, handle, 0, 0).</summary>
        void Bind(object obj, int handle);

        bool SetText(object obj, string text);
        bool FillRect(object window, int x, int y, int w, int h, uint argb);

        /// <summary>Close/remove. May be called twice for the same object or after its parent closed - be idempotent.</summary>
        void Close(object obj);

        byte[] ReadFile(string path);                 // null if missing
        bool WriteFile(string path, byte[] data);
    }
}
