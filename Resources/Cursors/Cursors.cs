using System.IO;
using Cosmos.Kernel.System.Graphics;
using Windose.Drivers;

public static class Cursors
{
    private static Png? _arrow;
    public static Png? arrow
    {
        get
        {
            if (_arrow != null) return _arrow;
            try
            {
                if (File.Exists("/mnt/System/Cursors/arrow.png"))
                    _arrow = new Png("/mnt/System/Cursors/arrow.png");
            }
            catch { }
            return _arrow;
        }
    }
}
