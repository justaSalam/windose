
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.System.Graphics.Fonts;

//TODO Add font loading at runtime
public static class SystemFonts
{

    public static TrueTypeFont sansSerif;
    public static byte[] resourceSpan;

    public static void Init()
    {
        resourceSpan = ResourceManager.GetResourceAsSpan("Windose.Resources.Fonts.ARIAL.TTF").ToArray();
        sansSerif = new TrueTypeFont(resourceSpan);
    }
}
