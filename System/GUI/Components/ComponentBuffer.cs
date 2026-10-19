using Cosmos.Kernel.System.Graphics;
using System.Drawing;


public class ComponentBuffer : Canvas
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int[] Buffer => GetBuffer();

    private const int MaxClipDepth = 32;
    private readonly int[] originXStack = new int[MaxClipDepth];
    private readonly int[] originYStack = new int[MaxClipDepth];
    private readonly Rectangle[] clipStack = new Rectangle[MaxClipDepth];
    private int contextDepth;
    private int originX;
    private int originY;
    private Rectangle clipBounds;


    public ComponentBuffer(int width, int height) : base(Math.Max(1, width), Math.Max(1, height), ColorDepth.ColorDepth32)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        clipBounds = new Rectangle(0, 0, Width, Height);
    }
    public bool HasVisibleClip => clipBounds.Width > 0 && clipBounds.Height > 0;

    public void ResetContext(Rectangle clip)
    {
        contextDepth = 0;
        originX = 0;
        originY = 0;
        clipBounds = Rectangle.Intersect(new Rectangle(0, 0, Width, Height), clip);
    }

    public void ResetContext()
    {
        ResetContext(new Rectangle(0, 0, Width, Height));
    }


    public bool PushContext(int x, int y, int width, int height, Rectangle parentLocalClip)
    {
        if (contextDepth >= MaxClipDepth) return false;

        originXStack[contextDepth] = originX;
        originYStack[contextDepth] = originY;
        clipStack[contextDepth] = clipBounds;
        contextDepth++;

        Rectangle componentBounds = new Rectangle(originX + x, originY + y, width, height);
        Rectangle requestedClip = new Rectangle(originX + parentLocalClip.X, originY + parentLocalClip.Y, parentLocalClip.Width, parentLocalClip.Height);

        originX += x;
        originY += y;
        clipBounds = Rectangle.Intersect(clipBounds, componentBounds);
        clipBounds = Rectangle.Intersect(clipBounds, requestedClip);
        return true;
    }

    public void PopContext()
    {
        if (contextDepth <= 0) return;
        contextDepth--;
        originX = originXStack[contextDepth];
        originY = originYStack[contextDepth];
        clipBounds = clipStack[contextDepth];
    }


    public void SetPixelAlpha(int x, int y, int colour)
    {
        x += originX;
        y += originY;
        if (!ContainsClipped(x, y)) return;
        BlendPixel(GetBuffer(), x + y * Width, colour);
    }
    public override void Clear(Color color)
    {
        Array.Fill(GetBuffer(), color.ToArgb());
    }
    public override void DrawPoint(Color color, int x, int y)
    {
        x += originX;
        y += originY;
        int[] buffer = GetBuffer();
        if (buffer == null || !ContainsClipped(x, y))
        {
            return;
        }

        BlendPixel(buffer, y * Width + x, color.ToArgb());
    }

    public new void DrawImage(Image image, int x, int y, bool preventOffBoundPixels = true)
    {
        DrawArrayAlphaClipped(image.RawData, image.Width, 0, 0, x, y, image.Width, image.Height);
    }

    /// <summary>
    /// Draw Image stretch method overload, auto source rect.
    /// </summary>
    public void DrawImageStretch(Image image, in Rectangle destRect) => DrawImageStretch(image, new Rectangle(0, 0, (int)image.Width, (int)image.Height), destRect);

    /// <summary>
    /// Use the overloaded method instead of this one
    /// </summary>
    public void DrawImageStretch(Image image, in Rectangle sourceRect, in Rectangle destRect)
    {
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0 || destRect.Width <= 0 || destRect.Height <= 0)
            return;

        if (sourceRect.Width == destRect.Width && sourceRect.Height == destRect.Height)
        {
            DrawArrayAlphaClipped(image.RawData, image.Width, sourceRect.Left, sourceRect.Top,
                destRect.Left, destRect.Top, destRect.Width, destRect.Height);
            return;
        }

        float scaleX = (float)sourceRect.Width / destRect.Width;
        float scaleY = (float)sourceRect.Height / destRect.Height;

        for (int xi = 0; xi < destRect.Width; xi++)
        {
            for (int yi = 0; yi < destRect.Height; yi++)
            {
                int srcX = (int)(xi * scaleX) + sourceRect.Left;
                int srcY = (int)(yi * scaleY) + sourceRect.Top;

                srcX = Math.Min(srcX, sourceRect.Right - 1);
                srcY = Math.Min(srcY, sourceRect.Bottom - 1);

                if (srcX < 0 || srcY < 0 || srcX >= image.Width || srcY >= image.Height)
                    continue;

                int color = image.RawData[srcX + srcY * image.Width];
                SetPixelAlpha(destRect.Left + xi, destRect.Top + yi, color);

            }
        }
    }

    public override int GetRawPointColor(int x, int y)
    {
        if (Buffer == null)
        {
            return 0;
        }

        x += originX;
        y += originY;
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return 0;
        }

        return Buffer[y * Width + x];
    }

    public new void DrawFilledRectangle(Color color, int xStart, int yStart, int width, int height, bool preventOffBoundPixels = true)
    {
        if (height == -1)
        {
            height = width;
        }

        Rectangle target = new Rectangle(originX + xStart, originY + yStart, width, height);
        target = Rectangle.Intersect(target, clipBounds);
        target = Rectangle.Intersect(target, new Rectangle(0, 0, Width, Height));
        if (target.Width <= 0 || target.Height <= 0) return;

        int argb = color.ToArgb();
        int[] buffer = GetBuffer();
        if (color.A == byte.MaxValue)
        {
            for (int y = target.Top; y < target.Bottom; y++)
                Array.Fill(buffer, argb, y * Width + target.Left, target.Width);
            return;
        }

        int rowStart = target.Top * Width + target.Left;
        for (int y = 0; y < target.Height; y++, rowStart += Width)
        {
            int index = rowStart;
            int end = index + target.Width;
            for (; index < end; index++)
                BlendPixel(buffer, index, argb);
        }
    }

    public void Test_DrawFilledAlphaRectangle(Color color, int xStart, int yStart, int width, int height, int alpha)
    {
        if (height == -1)
        {
            height = width;
        }

        Rectangle target = new Rectangle(originX + xStart, originY + yStart, width, height);
        target = Rectangle.Intersect(target, clipBounds);
        target = Rectangle.Intersect(target, new Rectangle(0, 0, Width, Height));
        if (target.Width <= 0 || target.Height <= 0) return;

        int argb = color.ToArgb();
        int[] buffer = GetBuffer();
        if (color.A == byte.MaxValue)
        {
            for (int y = target.Top; y < target.Bottom; y++)
                Array.Fill(buffer, argb, y * Width + target.Left, target.Width);
            return;
        }

        int rowStart = target.Top * Width + target.Left;
        for (int y = 0; y < target.Height; y++, rowStart += Width)
        {
            int index = rowStart;
            int end = index + target.Width;
            for (; index < end; index++)
            {
                BlendPixel(buffer, index, argb, alpha);
            }
        }
    }

    public new void DrawRectangle(Color color, int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        DrawFilledRectangle(color, x, y, width, 1);

        if (height > 1)
            DrawFilledRectangle(color, x, y + height - 1, width, 1);

        if (height > 2)
        {
            DrawFilledRectangle(color, x, y + 1, 1, height - 2);

            if (width > 1)
                DrawFilledRectangle(color, x + width - 1, y + 1, 1, height - 2);
        }
    }

    private bool TryClipArrayRegion(int sourceWidth, int sourceHeight, ref int sourceX, ref int sourceY,
        ref int destinationX, ref int destinationY, ref int width, ref int height)
    {
        destinationX += originX;
        destinationY += originY;

        if (sourceX < 0)
        {
            int clipped = -sourceX;
            destinationX += clipped;
            width -= clipped;
            sourceX = 0;
        }

        if (sourceY < 0)
        {
            int clipped = -sourceY;
            destinationY += clipped;
            height -= clipped;
            sourceY = 0;
        }

        if (destinationX < 0)
        {
            int clipped = -destinationX;
            sourceX += clipped;
            width -= clipped;
            destinationX = 0;
        }

        if (destinationY < 0)
        {
            int clipped = -destinationY;
            sourceY += clipped;
            height -= clipped;
            destinationY = 0;
        }

        if (destinationX + width > Width)
        {
            width = Width - destinationX;
        }

        if (destinationY + height > Height)
        {
            height = Height - destinationY;
        }

        if (destinationX < clipBounds.Left)
        {
            int clipped = clipBounds.Left - destinationX;
            sourceX += clipped;
            width -= clipped;
            destinationX = clipBounds.Left;
        }

        if (destinationY < clipBounds.Top)
        {
            int clipped = clipBounds.Top - destinationY;
            sourceY += clipped;
            height -= clipped;
            destinationY = clipBounds.Top;
        }

        if (destinationX + width > clipBounds.Right)
            width = clipBounds.Right - destinationX;

        if (destinationY + height > clipBounds.Bottom)
            height = clipBounds.Bottom - destinationY;

        if (sourceX + width > sourceWidth)
            width = sourceWidth - sourceX;

        if (sourceY + height > sourceHeight)
            height = sourceHeight - sourceY;

        if (destinationX < 0 || destinationY < 0 || destinationX >= Width || destinationY >= Height)
            return false;

        if (width > Width - destinationX)
            width = Width - destinationX;

        if (height > Height - destinationY)
            height = Height - destinationY;

        return width > 0 && height > 0;
    }

    public virtual void DrawArrayClipped(int[] colors, int sourceWidth, int sourceX, int sourceY, int destinationX, int destinationY, int width, int height)
    {
        if (colors == null || sourceWidth <= 0) return;
        int sourceHeight = colors.Length / sourceWidth;
        if (!TryClipArrayRegion(sourceWidth, sourceHeight, ref sourceX, ref sourceY,
            ref destinationX, ref destinationY, ref width, ref height)) return;

        int[] buffer = GetBuffer();

        for (int j = 0; j < height; j++)
        {
            int sourceIndex = (sourceY + j) * sourceWidth + sourceX;
            int destinationIndex = (destinationY + j) * Width + destinationX;

            Array.Copy(colors, sourceIndex, buffer, destinationIndex, width);
        }
    }
    public override void DrawArray(int[] colors, int x, int y, int width, int height)
    {
        DrawArrayClipped(colors, width, 0, 0, x, y, width, height);
    }
    public virtual void DrawArrayAlphaClipped(int[] colors, int sourceWidth, int sourceX, int sourceY, int destinationX, int destinationY, int width, int height)
    {
        if (colors == null || sourceWidth <= 0) return;
        int sourceHeight = colors.Length / sourceWidth;
        if (!TryClipArrayRegion(sourceWidth, sourceHeight, ref sourceX, ref sourceY,
            ref destinationX, ref destinationY, ref width, ref height)) return;

        int[] buffer = GetBuffer();
        for (int y = 0; y < height; y++)
        {
            int sourceIndex = (sourceY + y) * sourceWidth + sourceX;
            int destinationIndex = (destinationY + y) * Width + destinationX;

            for (int x = 0; x < width; x++)
                BlendPixel(buffer, destinationIndex + x, colors[sourceIndex + x]);
        }
    }
    internal void DrawHorizontalLine(Color color, int dx, int x1, int y1)
    {
        if (dx >= 0)
            DrawHorizontalSpan(color, x1, y1, dx);
        else
            DrawHorizontalSpan(color, x1 + dx, y1, -dx);
    }

    internal void DrawVerticalLine(Color color, int dy, int x1, int y1)
    {
        int startY = dy >= 0 ? y1 : y1 + dy;
        int length = Math.Abs(dy);
        int targetX = originX + x1;
        int targetY = originY + startY;

        if (targetX < clipBounds.Left || targetX >= clipBounds.Right ||
            targetX < 0 || targetX >= Width || length <= 0)
            return;

        int top = Math.Max(Math.Max(targetY, clipBounds.Top), 0);
        int bottom = Math.Min(Math.Min(targetY + length, clipBounds.Bottom), Height);
        int argb = color.ToArgb();

        int[] buffer = GetBuffer();

        int index = top * Width + targetX;
        for (int y = top; y < bottom; y++, index += Width)
        {
            if (color.A == byte.MaxValue)
                buffer[index] = argb;
            else
                BlendPixel(buffer, index, argb);
        }
    }

    private void DrawHorizontalSpan(Color color, int x, int y, int length)
    {
        if (length <= 0 || color.A == 0) return;

        int targetX = originX + x;
        int targetY = originY + y;
        if (targetY < clipBounds.Top || targetY >= clipBounds.Bottom ||
            targetY < 0 || targetY >= Height)
            return;

        int left = Math.Max(Math.Max(targetX, clipBounds.Left), 0);
        int right = Math.Min(Math.Min(targetX + length, clipBounds.Right), Width);
        if (left >= right) return;

        int argb = color.ToArgb();
        int[] buffer = GetBuffer();
        int index = targetY * Width + left;
        if (color.A == byte.MaxValue)
        {
            Array.Fill(buffer, argb, index, right - left);
            return;
        }

        int end = index + right - left;
        for (; index < end; index++)
            BlendPixel(buffer, index, argb);
    }

    internal void DrawDiagonalLine(Color color, int dx, int dy, int x1, int y1)
    {
        int num = Math.Abs(dx);
        int num2 = Math.Abs(dy);
        int num3 = Math.Sign(dx);
        int num4 = Math.Sign(dy);
        int num5 = num2 >> 1;
        int num6 = num >> 1;
        int num7 = x1;
        int num8 = y1;
        if (num >= num2)
        {
            for (int i = 0; i < num; i++)
            {
                num6 += num2;
                if (num6 >= num)
                {
                    num6 -= num;
                    num8 += num4;
                }

                num7 += num3;
                DrawPoint(color, num7, num8);
            }

            return;
        }

        for (int i = 0; i < num2; i++)
        {
            num5 += num;
            if (num5 >= num2)
            {
                num5 -= num2;
                num7 += num3;
            }

            num8 += num4;
            DrawPoint(color, num7, num8);
        }
    }

    public override void DrawLine(Color color, int x1, int y1, int x2, int y2)
    {
        int num = x2 - x1;
        int num2 = y2 - y1;
        if (num2 == 0)
        {
            DrawHorizontalLine(color, num, x1, y1);
        }
        else if (num == 0)
        {
            DrawVerticalLine(color, num2, x1, y1);
        }
        else
        {
            TrimLine(ref x1, ref y1, ref x2, ref y2);
            num = x2 - x1;
            num2 = y2 - y1;
            DrawDiagonalLine(color, num, num2, x1, y1);
        }
    }
    public void DrawDottedRectangle(Color color, int x, int y, int width, int height, int dotLength = 2, int gapLength = 2)
    {
        DrawDottedHorizontalLine(color, x, y, width, dotLength, gapLength);
        DrawDottedHorizontalLine(color, x, y + height - 1, width, dotLength, gapLength);

        DrawDottedVerticalLine(color, x, y, height, dotLength, gapLength);
        DrawDottedVerticalLine(color, x + width - 1, y, height, dotLength, gapLength);
    }

    public void DrawDottedHorizontalLine(Color color, int x, int y, int length, int dotLength = 2, int gapLength = 2)
    {
        int step = dotLength + gapLength;

        for (int i = 0; i < length; i += step)
        {
            int currentDotLength = Math.Min(dotLength, length - i);
            DrawLine(color, x + i, y, x + i + currentDotLength, y);
        }
    }
    public void DrawDottedVerticalLine(Color color, int x, int y, int length, int dotLength = 2, int gapLength = 2)
    {
        int step = dotLength + gapLength;

        for (int i = 0; i < length; i += step)
        {
            int currentDotLength = Math.Min(dotLength, length - i);
            DrawLine(color, x, y + i, x, y + i + currentDotLength);
        }
    }

    public virtual void DrawRaisedRect(int x, int y, int width, int height)
    {
        DrawRaisedRect(x, y, width, height, Palette.ControlFace, Palette.ControlWhite, Palette.ControlShadow, Palette.ControlBlack);
    }

    public virtual void DrawRaisedRect(int x, int y, int width, int height, Color face, Color highlight, Color shadow, Color darkShadow)
    {
        if (width <= 0 || height <= 0) return;

        DrawFilledRectangle(face, x, y, width, height);

        int right = x + width - 1;
        int bottom = y + height - 1;

        DrawLine(highlight, x, y, right, y);
        DrawLine(highlight, x, y, x, bottom);
        DrawLine(darkShadow, x, bottom, right, bottom);
        DrawLine(darkShadow, right, y, right, bottom);

        if (width < 3 || height < 3) return;

        DrawLine(face, x + 1, y + 1, right - 1, y + 1);
        DrawLine(face, x + 1, y + 1, x + 1, bottom - 1);
        DrawLine(shadow, x + 1, bottom - 1, right - 1, bottom - 1);
        DrawLine(shadow, right - 1, y + 1, right - 1, bottom - 1);
    }

    public virtual void DrawSunkenRect(int x, int y, int width, int height)
    {
        DrawSunkenRect(x, y, width, height, Palette.ControlFace, Palette.ControlBlack, Palette.ControlShadow, Palette.ControlHighlight);
    }

    public virtual void DrawSunkenRect(int x, int y, int width, int height, Color face, Color darkShadow, Color shadow, Color highlight)
    {
        if (width <= 0 || height <= 0) return;

        DrawFilledRectangle(face, x, y, width, height);

        int right = x + width - 1;
        int bottom = y + height - 1;

        DrawLine(darkShadow, x, y, right, y);
        DrawLine(darkShadow, x, y, x, bottom);
        DrawLine(highlight, x, bottom, right, bottom);
        DrawLine(highlight, right, y, right, bottom);

        if (width < 3 || height < 3) return;

        DrawLine(shadow, x + 1, y + 1, right - 1, y + 1);
        DrawLine(shadow, x + 1, y + 1, x + 1, bottom - 1);
        DrawLine(face, x + 1, bottom - 1, right - 1, bottom - 1);
        DrawLine(face, right - 1, y + 1, right - 1, bottom - 1);
    }

    public virtual void DrawEtchedRect(int x, int y, int width, int height)
    {
        DrawEtchedRect(x, y, width, height, Palette.ControlShadow, Palette.ControlWhite);
    }

    public virtual void DrawEtchedRect(int x, int y, int width, int height, Color shadow, Color highlight)
    {
        if (width <= 1 || height <= 1) return;

        int right = x + width - 1;
        int bottom = y + height - 1;

        DrawLine(shadow, x, y, right - 1, y);
        DrawLine(shadow, x, y, x, bottom - 1);
        DrawLine(highlight, x + 1, bottom, right, bottom);
        DrawLine(highlight, right, y + 1, right, bottom);
    }

    private bool ContainsClipped(int x, int y)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height &&
            x >= clipBounds.Left && x < clipBounds.Right &&
            y >= clipBounds.Top && y < clipBounds.Bottom;
    }

    private void BlendPixel(int[] buffer, int index, int color)
    {
        if ((uint)index >= (uint)buffer.Length) return;
        int alpha = (color >> 24) & 0xff;
        if (alpha == 0) return;
        if (alpha == 0xff)
        {
            buffer[index] = color;
            return;
        }

        int background = buffer[index];
        int inverse = 255 - alpha;
        int red = (((color >> 16) & 0xff) * alpha + ((background >> 16) & 0xff) * inverse) >> 8;
        int green = (((color >> 8) & 0xff) * alpha + ((background >> 8) & 0xff) * inverse) >> 8;
        int blue = ((color & 0xff) * alpha + (background & 0xff) * inverse) >> 8;
        buffer[index] = (0xff << 24) | (red << 16) | (green << 8) | blue;
    }

    /// <summary>
    /// TEST Alpha blending
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="index"></param>
    /// <param name="color"></param>
    /// <param name="alpha"></param>
    private void BlendPixel(int[] buffer, int index, int color, int alpha)
    {
        if ((uint)index >= (uint)buffer.Length)
        {
            return;
        }
        alpha = Math.Clamp(alpha, 0, 255);

        if (alpha == 0) return;
        if (alpha == 0xff)
        {
            buffer[index] = color;
            return;
        }

        int background = buffer[index];
        int inverse = 255 - alpha;
        int red = (((color >> 16) & 0xff) * alpha + ((background >> 16) & 0xff) * inverse) >> 8;
        int green = (((color >> 8) & 0xff) * alpha + ((background >> 8) & 0xff) * inverse) >> 8;
        int blue = ((color & 0xff) * alpha + (background & 0xff) * inverse) >> 8;
        buffer[index] = (0xff << 24) | (red << 16) | (green << 8) | blue;
    }

    private bool disposed;

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        // Canvas owns its backing store; callers release this wrapper by dropping it.
    }
}
