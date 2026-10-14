// ElfEvents.cs - per-program event queue. UI/kernel threads Push (via ElfProgram.PostEvent), the program polls.
namespace Windose.System.Elf
{
    public struct KEvent
    {
        public int Type;     // see ElfEvent.*
        public int Handle;   // which window/component (the program's own handle)
        public int A;
        public int B;
    }

    public static class ElfEvent
    {
        public const int None = 0;
        public const int Click = 1;   // Handle = button
        public const int Close = 2;   // Handle = window (user pressed X)
        public const int Key = 3;     // A = key code
    }

    public sealed class ElfEventQueue
    {
        private const int Size = 64;
        private readonly object _lock = new object();
        private readonly KEvent[] _q = new KEvent[Size];
        private int _head, _tail;

        public void Push(int type, int handle, int a, int b)
        {
            lock (_lock)
            {
                int next = (_tail + 1) % Size;
                if (next == _head) return;     // full -> drop newest
                _q[_tail].Type = type; _q[_tail].Handle = handle; _q[_tail].A = a; _q[_tail].B = b;
                _tail = next;
            }
        }

        public bool Pop(out KEvent e)
        {
            lock (_lock)
            {
                if (_head == _tail) { e = default(KEvent); return false; }
                e = _q[_head];
                _head = (_head + 1) % Size;
                return true;
            }
        }

        public void Clear() { lock (_lock) { _head = 0; _tail = 0; } }
    }
}
