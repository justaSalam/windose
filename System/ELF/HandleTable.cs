// HandleTable.cs - maps int handles <-> kernel objects. One table per running program.
// handle = (generation << 16) | (slot + 1).  0 is never valid, handles are always positive.
// A stale handle (object already removed) fails Get() instead of hitting a reused slot. Thread-safe.
namespace Windose.System.Elf
{
    public sealed class HandleTable
    {
        private readonly object _lock = new object();
        private object[] _items = new object[16];
        private ushort[] _gen = new ushort[16];
        private int _count;

        public int Count { get { lock (_lock) return _count; } }

        public int Add(object item)
        {
            if (item == null) return 0;
            lock (_lock)
            {
                int i = 0;
                while (i < _items.Length && _items[i] != null) i++;
                if (i == _items.Length)
                {
                    if (i >= 0x7FFF) return 0;
                    object[] ni = new object[i * 2];
                    ushort[] ng = new ushort[i * 2];
                    global::System.Array.Copy(_items, ni, i);
                    global::System.Array.Copy(_gen, ng, i);
                    _items = ni; _gen = ng;
                }
                _items[i] = item;
                _count++;
                return ((_gen[i] & 0x7FFF) << 16) | (i + 1);
            }
        }

        public object Get(int h)
        {
            lock (_lock)
            {
                int i = (h & 0xFFFF) - 1;
                if (i < 0 || i >= _items.Length) return null;
                if ((_gen[i] & 0x7FFF) != ((h >> 16) & 0x7FFF)) return null;
                return _items[i];
            }
        }

        public object Remove(int h)
        {
            lock (_lock)
            {
                object o = Get(h);
                if (o == null) return null;
                int i = (h & 0xFFFF) - 1;
                _items[i] = null;
                _gen[i]++;
                _count--;
                return o;
            }
        }

        /// <summary>Removes and returns everything still open (program exit / kill).</summary>
        public object[] Drain()
        {
            lock (_lock)
            {
                object[] r = new object[_count];
                int n = 0;
                for (int i = 0; i < _items.Length; i++)
                {
                    if (_items[i] == null) continue;
                    r[n++] = _items[i];
                    _items[i] = null;
                    _gen[i]++;
                }
                _count = 0;
                return r;
            }
        }
    }
}
