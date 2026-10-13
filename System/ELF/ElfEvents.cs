// ElfEvents.cs - event queue the program polls. Kernel/UI threads Push, the program Pops.
using System.Threading;

namespace Windose.System.Elf
{
    public struct KEvent
    {
        public int Type;     // see ElfEvent.*
        public int Handle;   // which window/component
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

    public static class ElfEvents
    {
        private const int Size = 64;
        private static KEvent[] _q = new KEvent[Size];
        private static int _head, _tail;
        private static SpinLock _lock = new SpinLock();   // no lock() on Cosmos

        public static void Push(int type, int handle, int a, int b)
        {
            bool taken = false;
            _lock.Enter(ref taken);
            int next = (_tail + 1) % Size;
            if (next != _head)   // queue full -> drop
            {
                _q[_tail].Type = type; _q[_tail].Handle = handle; _q[_tail].A = a; _q[_tail].B = b;
                _tail = next;
            }
            if (taken) _lock.Exit();
        }

        public static bool Pop(out KEvent e)
        {
            bool taken = false;
            _lock.Enter(ref taken);
            bool has = _head != _tail;
            e = default(KEvent);
            if (has) { e = _q[_head]; _head = (_head + 1) % Size; }
            if (taken) _lock.Exit();
            return has;
        }

        public static void Clear()
        {
            bool taken = false;
            _lock.Enter(ref taken);
            _head = 0; _tail = 0;
            if (taken) _lock.Exit();
        }
    }
}
