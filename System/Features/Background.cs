using Cosmos.Kernel.System.Graphics;

namespace Windose.System.Features
{
    public static class Background
    {
        private static Image Default;
        public static Image Current;

        public static void Load()
        {
            Default = Wallpapers.Lithium;
            //Default = new Png("/mnt/System/Wallpapers/Lithium.png");
            Current = Default;
        }
    }
}
