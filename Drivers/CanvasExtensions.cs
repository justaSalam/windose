using System.Drawing;
using Cosmos.Kernel.System.Graphics;

public class CanvasExtensions : Canvas
{
    public override void DrawFilledRectangle(Color color, int xStart, int yStart, int width, int height, bool preventOffBoundPixels = true)
    {
        base.DrawFilledRectangle(color, xStart, yStart, width, height, preventOffBoundPixels);
    }
    public void DrawFilledRectangle(Color color, int xStart, int yStart, int width, int height, Rectangle clipRect, bool preventOffBoundPixels = true)
    {

        if (height == -1)
        {
            height = width;
        }

        int x1 = xStart;
        int y1 = yStart;
        int x2 = xStart + width;
        int y2 = yStart + height;

        x1 = Math.Max(x1, clipRect.X);
        y1 = Math.Max(y1, clipRect.Y);
        x2 = Math.Min(x2, clipRect.X + clipRect.Width);
        y2 = Math.Min(y2, clipRect.Y + clipRect.Height);

        if (preventOffBoundPixels)
        {
            x1 = Math.Max(x1, 0);
            y1 = Math.Max(y1, 0);
            x2 = Math.Min(x2, (int)Mode.Width);
            y2 = Math.Min(y2, (int)Mode.Height);
        }

        // Nothing visible
        if (x2 <= x1 || y2 <= y1)
            return;

        for (int y = y1; y < y2; y++)
        {
            DrawLine(color, x1, y, x2 - 1, y);
        }
    }
}