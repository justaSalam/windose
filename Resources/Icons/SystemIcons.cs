using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.System.Graphics;

namespace Windose.Resources.Icons
{
    public static class SystemIcons
    {
        public static Png file_windows = new Png(ResourceManager.GetResourceAsSpan("Windose.Resources.Icons.file_windows.png").ToArray());
        public static Png directory_closed = new Png(ResourceManager.GetResourceAsSpan("Windose.Resources.Icons.directory_closed.png").ToArray());
    }
}
