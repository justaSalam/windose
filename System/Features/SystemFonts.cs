
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.System.Graphics.Fonts;

public static class SystemFonts
{

    public static TrueTypeFont sansSerif;

    public static void Init()
    {
        ReadOnlySpan<byte> resourceSpan = ResourceManager.GetResourceAsSpan("Windose.Resources.Fonts.ARIAL.TTF");
        sansSerif = new TrueTypeFont(resourceSpan.ToArray());
    }
}

public struct FontData
{
    public string Name;
    public string Path;
    public UnmanagedMemoryStream data;

}