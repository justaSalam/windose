
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.System.Graphics;

public static class Cursors
{
    public static Png arrow;
    public static byte[] resourceSpan;

    public static void Init()
    {
        arrow = new Png(ResourceManager.GetResourceAsSpan("Windose.Resources.Cursors.arrow.png").ToArray());
    }
}