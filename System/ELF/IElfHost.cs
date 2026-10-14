// IElfHost.cs - the ONLY place the loader touches your OS. Implement it once (WindoseBridge.cs).
// The runner validates handles/strings/sizes and owns per-program state; you just create real objects.
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
