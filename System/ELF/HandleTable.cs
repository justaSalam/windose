// HandleTable.cs - maps int handles <-> kernel objects. The program only ever sees the int.
// handle = (generation << 16) | (slot + 1).  0 is never a valid handle.
// A stale handle (object already removed) fails Get() instead of hitting a reused slot.
namespace Windose.System.Elf
{
    public sealed class HandleTable
    {
        private object[] _items = new object[16];
        private ushort[] _gen = new ushort[16];

        public int Add(object item)
        {
            if (item == null) return 0;
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
            return ((_gen[i] & 0x7FFF) << 16) | (i + 1);
        }

        public object Get(int h)
        {
            int i = (h & 0xFFFF) - 1;
            if (i < 0 || i >= _items.Length) return null;
            if ((_gen[i] & 0x7FFF) != ((h >> 16) & 0x7FFF)) return null;
            return _items[i];
        }

        public object Remove(int h)
        {
            object o = Get(h);
            if (o == null) return null;
            int i = (h & 0xFFFF) - 1;
            _items[i] = null;
            _gen[i]++;
            return o;
        }

        /// <summary>Removes and returns everything still open (used when a program exits).</summary>
        public object[] Drain()
        {
            int n = 0;
            for (int i = 0; i < _items.Length; i++) if (_items[i] != null) n++;
            object[] r = new object[n];
            n = 0;
            for (int i = 0; i < _items.Length; i++)
            {
                if (_items[i] == null) continue;
                r[n++] = _items[i];
                _items[i] = null;
                _gen[i]++;
            }
            return r;
        }
    }
}
