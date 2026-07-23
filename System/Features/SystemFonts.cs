
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.System.Graphics.Fonts;

//TODO Add font loading at runtime
public static class SystemFonts
{
    public static TrueTypeFont arial = new TrueTypeFont(ResourceManager.GetResourceAsSpan("Windose.Resources.Fonts.ARIAL.TTF").ToArray());
}
