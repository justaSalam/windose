using System.Runtime.CompilerServices;

namespace Cosmos.Kernel.System.Graphics
{
    /// <summary>
    /// Helper to safely initialize read-only/internal-set properties on Cosmos Image instances
    /// using standard .NET UnsafeAccessor without requiring modified Cosmos binaries or reflection.
    /// </summary>
    internal static class ImageAccessor
    {
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_RawData")]
        public static extern void SetRawData(Image image, int[] rawData);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Width")]
        public static extern void SetWidth(Image image, int width);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Height")]
        public static extern void SetHeight(Image image, int height);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_ColorDepth")]
        public static extern void SetColorDepth(Image image, ColorDepth colorDepth);

        public static void Initialize(Image image, int width, int height, int[] rawData, ColorDepth colorDepth = ColorDepth.ColorDepth32)
        {
            SetWidth(image, width);
            SetHeight(image, height);
            SetColorDepth(image, colorDepth);
            SetRawData(image, rawData);
        }
    }
}
