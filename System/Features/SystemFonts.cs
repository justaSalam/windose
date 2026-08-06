
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.System.Graphics.Fonts;

//TODO Add font loading at runtime
public static class SystemFonts
{
    public static TrueTypeFont arial = new TrueTypeFont(ResourceManager.GetResourceAsSpan("Windose.Resources.Fonts.ARIAL.TTF").ToArray());

    public static Font lucida = PCScreenFont.LoadFont(ResourceManager.GetResourceAsSpan("Windose.Resources.Fonts.lucida-10x16.psf").ToArray());
    public static Font spleen12x24 = PCScreenFont.LoadFont(ResourceManager.GetResourceAsSpan("Windose.Resources.Fonts.spleen-12x24.psfu").ToArray());
    public static Font spleen16x32 = PCScreenFont.LoadFont(ResourceManager.GetResourceAsSpan("Windose.Resources.Fonts.spleen-16x32.psfu").ToArray());
    public static Font spleen32x64 = PCScreenFont.LoadFont(ResourceManager.GetResourceAsSpan("Windose.Resources.Fonts.spleen-32x64.psfu").ToArray());
    public static Font spleen5x8 = PCScreenFont.LoadFont(ResourceManager.GetResourceAsSpan("Windose.Resources.Fonts.spleen-5x8.psfu").ToArray());
    public static Font spleen6x12 = PCScreenFont.LoadFont(ResourceManager.GetResourceAsSpan("Windose.Resources.Fonts.spleen-6x12.psfu").ToArray());
    public static Font spleen8x16 = PCScreenFont.LoadFont(ResourceManager.GetResourceAsSpan("Windose.Resources.Fonts.spleen-8x16.psfu").ToArray());
}
