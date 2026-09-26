namespace Windose.System.ABI.WIN
{
    public sealed class HandleTable
    {
        private uint _nextHandle = 1;

        private readonly Dictionary<uint, object> _objects = new();

        public uint Add(object value)
        {
            uint handle = _nextHandle++;

            if (handle == 0)
                handle = _nextHandle++;

            _objects.Add(handle, value);

            return handle;
        }

        public bool Remove(uint handle)
        {
            return _objects.Remove(handle);
        }

        public T? Get<T>(uint handle) where T : class
        {
            if (!_objects.TryGetValue(handle, out object? value))
                return null;

            return value as T;
        }

        public bool Exists(uint handle)
        {
            return _objects.ContainsKey(handle);
        }
    }
}

