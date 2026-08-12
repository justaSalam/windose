using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Pci;
using Cosmos.Kernel.HAL.Pci.Enums;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using System.Drawing;


public class DirectBitmap : Canvas
{

    public int Width { get; private set; }
    public int Height { get; private set; }

    internal int Stride;
    internal int Pitch;

    private const int MaxClipDepth = 32;
    private readonly int[] originXStack = new int[MaxClipDepth];
    private readonly int[] originYStack = new int[MaxClipDepth];
    private readonly Rectangle[] clipStack = new Rectangle[MaxClipDepth];
    private int contextDepth;
    private int originX;
    private int originY;
    private Rectangle clipBounds;

    public int[] GetBufferBitmap
    {
        get
        {
            return Buffer;
        }
    }

    public DirectBitmap(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Buffer = new int[width * height];

        Stride = 32 / 8;
        Pitch = Width * Stride;
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

    public bool PushContext(int x, int y, int width, int height)
    {
        return PushContext(x, y, width, height, new Rectangle(x, y, width, height));
    }

    public bool PushContext(int x, int y, int width, int height, Rectangle parentLocalClip)
    {
        if (contextDepth >= MaxClipDepth) return false;

        originXStack[contextDepth] = originX;
        originYStack[contextDepth] = originY;
        clipStack[contextDepth] = clipBounds;
        contextDepth++;

        Rectangle componentBounds = new Rectangle(originX + x, originY + y, width, height);
        Rectangle requestedClip = new Rectangle(
            originX + parentLocalClip.X,
            originY + parentLocalClip.Y,
            parentLocalClip.Width,
            parentLocalClip.Height);

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
        int index = x + y * Width;

        if (index >= 0 && index < Buffer.Length)
        {
            if ((colour >> 24) == 0xFF)
            {
                Buffer[index] = colour;
                return;
            }

            int bgColour = Buffer[index];
            int alpha = (colour >> 24) & 0xff;
            int invAlpha = 255 - alpha;
            int newRed = (((colour >> 16) & 0xff) * alpha + ((bgColour >> 16) & 0xff) * invAlpha) >> 8;
            int newGreen = (((colour >> 8) & 0xff) * alpha + ((bgColour >> 8) & 0xff) * invAlpha) >> 8;
            int newBlue = ((colour & 0xff) * alpha + (bgColour & 0xff) * invAlpha) >> 8;

            Buffer[index] = (alpha << 24) | (newRed << 16) | (newGreen << 8) | newBlue;
        }
    }

    public void SetPixel(int x, int y, int colour)
    {
        x += originX;
        y += originY;
        if (!ContainsClipped(x, y)) return;
        int index = x + y * Width;

        if (index >= 0 && index < Buffer.Length)
        {
            if ((colour >> 24) == 0xFF)
            {
                Buffer[index] = colour;
                return;
            }

            int bgColour = Buffer[index];
            int alpha = 255;
            int invAlpha = 255 - alpha;
            int newRed = (((colour >> 16) & 0xff) * alpha + ((bgColour >> 16) & 0xff) * invAlpha) >> 8;
            int newGreen = (((colour >> 8) & 0xff) * alpha + ((bgColour >> 8) & 0xff) * invAlpha) >> 8;
            int newBlue = ((colour & 0xff) * alpha + (bgColour & 0xff) * invAlpha) >> 8;

            Buffer[index] = (alpha << 24) | (newRed << 16) | (newGreen << 8) | newBlue;
        }
    }
    public override void Clear(Color color)
    {
        Array.Fill(Buffer, color.ToArgb());
    }
    public override void DrawPoint(Color color, int x, int y)
    {
        x += originX;
        y += originY;
        if (Buffer == null || Buffer == null || !ContainsClipped(x, y))
        {
            return;
        }

        if (color.A < byte.MaxValue)
        {
            if (color.A == 0)
            {
                return;
            }

            color = AlphaBlend(color, Color.FromArgb(Buffer[y * Width + x]), color.A);
        }

        Buffer[y * Width + x] = color.ToArgb();
    }


    public override void DrawImage(Image image, int x, int y, bool preventOffBoundPixels = true)
    {
        DrawArrayAlphaClipped(image.RawData, (int)image.Width, 0, 0, x, y,
            (int)image.Width, (int)image.Height);
    }


    public override Bitmap GetImage(int x, int y, int width, int height)
    {
        Bitmap bitmap = new Bitmap((uint)width, (uint)height, ColorDepth.ColorDepth32);
        for (int i = 0; i < height; i++)
        {
            for (int j = 0; j < width; j++)
            {
                bitmap.RawData[i * width + j] = GetRawPointColor(x + j, y + i);
            }
        }

        return bitmap;
    }


    public override void CroppedDrawImage(Image image, int x, int y, int maxWidth, int maxHeight, bool preventOffBoundPixels = true)
    {
        int num = Math.Min((int)image.Width, maxWidth);
        int num2 = Math.Min((int)image.Height, maxHeight);
        DrawArrayAlphaClipped(image.RawData, (int)image.Width, 0, 0, x, y, num, num2);
    }

    

    public new void DrawImageAlpha(Image image, int x, int y, bool preventOffBoundPixels = true)
    {
        DrawArrayAlphaClipped(
            image.RawData,
            (int)image.Width,
            0,
            0,
            x,
            y,
            (int)image.Width,
            (int)image.Height);
    }
    public void DrawImageStretchAlpha(Image image, Rectangle sourceRect, Rectangle destRect)
    {
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0 || destRect.Width <= 0 || destRect.Height <= 0)
            return;

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

    public void DrawImageStretch(Image image, Rectangle sourceRect, Rectangle destRect)
    {
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0 || destRect.Width <= 0 || destRect.Height <= 0)
            return;

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
                SetPixel(destRect.Left + xi, destRect.Top + yi, color);

            }
        }
    }

    public override Color GetPointColor(int x, int y)
    {
        if (Buffer == null)
        {
            return Color.Black;
        }

        x += originX;
        y += originY;
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return Color.Black;
        }

        return Color.FromArgb(Buffer[y * Width + x]);
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

    public int[]? GetBuffer()
    {
        return Buffer;
    }

    internal int GetPointOffset(int x, int y)
    {
        return x * Stride + y * Pitch;
    }

    public override void DrawArray(Color[] colors, int x, int y, int width, int height)
    {
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                DrawPoint(colors[j * width + i], x + i, y + j);
            }
        }
    }
    public override void DrawArray(int[] colors, int x, int y, int width, int height)
    {
        DrawArrayClipped(colors, width, 0, 0, x, y, width, height);
    }

    private bool FontPixelSet(char c, Font font, int x, int y)
    {
        int bytesPerRow = (font.Width + 7) / 8;
        int glyphOffset = font.Height * bytesPerRow * (byte)c;
        byte value = font.Data[glyphOffset + y * bytesPerRow + x / 8];

        return font.ConvertByteToBitAddress(value, x % 8 + 1);
    }

    public virtual void DrawArrayClipped(int[] colors, int sourceWidth, int sourceX, int sourceY, int destinationX, int destinationY, int width, int height)
    {
        if (colors == null || sourceWidth <= 0) return;
        int sourceHeight = colors.Length / sourceWidth;

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
            return;

        if (width > Width - destinationX)
            width = Width - destinationX;

        if (height > Height - destinationY)
            height = Height - destinationY;

        if (width <= 0 || height <= 0) return;

        for (int j = 0; j < height; j++)
        {
            int sourceIndex = (sourceY + j) * sourceWidth + sourceX;
            int destinationIndex = (destinationY + j) * Width + destinationX;

            Array.Copy(colors, sourceIndex, Buffer, destinationIndex, width);
        }
    }

    public virtual void DrawArrayAlphaClipped(int[] colors, int sourceWidth, int sourceX, int sourceY, int destinationX, int destinationY, int width, int height)
    {
        DrawArrayAlphaClipped(colors, sourceWidth, sourceX, sourceY, destinationX, destinationY, width, height, 255);
    }

    public virtual void DrawArrayAlphaClipped(int[] colors, int sourceWidth, int sourceX, int sourceY, int destinationX, int destinationY, int width, int height, byte globalOpacity)
    {
        if (colors == null || sourceWidth <= 0) return;
        int sourceHeight = colors.Length / sourceWidth;

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
            width = Width - destinationX;

        if (destinationY + height > Height)
            height = Height - destinationY;

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
            return;

        if (width > Width - destinationX)
            width = Width - destinationX;

        if (height > Height - destinationY)
            height = Height - destinationY;

        if (width <= 0 || height <= 0) return;

        for (int y = 0; y < height; y++)
        {
            int sourceIndex = (sourceY + y) * sourceWidth + sourceX;
            int destinationIndex = (destinationY + y) * Width + destinationX;

            for (int x = 0; x < width; x++)
            {
                int color = colors[sourceIndex + x];
                int alpha = (color >> 24) & 0xff;

                if (globalOpacity < 255)
                    alpha = (alpha * globalOpacity) >> 8;

                if (alpha == 0)
                    continue;

                if (alpha == 0xff)
                {
                    Buffer[destinationIndex + x] = color;
                    continue;
                }

                int bgColor = Buffer[destinationIndex + x];
                int invAlpha = 255 - alpha;
                int red = (((color >> 16) & 0xff) * alpha + ((bgColor >> 16) & 0xff) * invAlpha) >> 8;
                int green = (((color >> 8) & 0xff) * alpha + ((bgColor >> 8) & 0xff) * invAlpha) >> 8;
                int blue = ((color & 0xff) * alpha + (bgColor & 0xff) * invAlpha) >> 8;

                Buffer[destinationIndex + x] = (0xff << 24) | (red << 16) | (green << 8) | blue;
            }
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

        for (int y = top; y < bottom; y++)
        {
            if (color.A == byte.MaxValue)
                Buffer[y * Width + targetX] = argb;
            else
                BlendTargetPixel(targetX, y, argb);
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
        int index = targetY * Width + left;
        if (color.A == byte.MaxValue)
        {
            Array.Fill(Buffer, argb, index, right - left);
            return;
        }

        for (int target = left; target < right; target++)
            BlendTargetPixel(target, targetY, argb);
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

    public override void DrawCircle(Color color, int xCenter, int yCenter, int radius)
    {
        int num = radius;
        int num2 = 0;
        int num3 = 0;
        while (num >= num2)
        {
            DrawPoint(color, xCenter + num, yCenter + num2);
            DrawPoint(color, xCenter + num2, yCenter + num);
            DrawPoint(color, xCenter - num2, yCenter + num);
            DrawPoint(color, xCenter - num, yCenter + num2);
            DrawPoint(color, xCenter - num, yCenter - num2);
            DrawPoint(color, xCenter - num2, yCenter - num);
            DrawPoint(color, xCenter + num2, yCenter - num);
            DrawPoint(color, xCenter + num, yCenter - num2);
            num2++;
            if (num3 <= 0)
            {
                num3 += 2 * num2 + 1;
            }

            if (num3 > 0)
            {
                num--;
                num3 -= 2 * num + 1;
            }
        }
    }

    public override void DrawFilledCircle(Color color, int x0, int y0, int radius)
    {
        int num = radius;
        int num2 = 0;
        int num3 = 1 - (radius << 1);
        int num4 = 0;
        int num5 = 0;
        while (num >= num2)
        {
            for (int i = x0 - num; i <= x0 + num; i++)
            {
                DrawPoint(color, i, y0 + num2);
                DrawPoint(color, i, y0 - num2);
            }

            for (int j = x0 - num2; j <= x0 + num2; j++)
            {
                DrawPoint(color, j, y0 + num);
                DrawPoint(color, j, y0 - num);
            }

            num2++;
            num5 += num4;
            num4 += 2;
            if ((num5 << 1) + num3 > 0)
            {
                num--;
                num5 += num3;
                num3 += 2;
            }
        }
    }

    public override void DrawEllipse(Color color, int xCenter, int yCenter, int xR, int yR)
    {
        int num = 2 * xR;
        int num2 = 2 * yR;
        int num3 = num2 & 1;
        int num4 = 4 * (1 - num) * num2 * num2;
        int num5 = 4 * (num3 + 1) * num * num;
        int num6 = num4 + num5 + num3 * num * num;
        int num7 = 0;
        int num8 = xR;
        num *= 8 * num;
        num3 = 8 * num2 * num2;
        while (num8 >= 0)
        {
            DrawPoint(color, xCenter + num8, yCenter + num7);
            DrawPoint(color, xCenter - num8, yCenter + num7);
            DrawPoint(color, xCenter - num8, yCenter - num7);
            DrawPoint(color, xCenter + num8, yCenter - num7);
            int num9 = 2 * num6;
            if (num9 <= num5)
            {
                num7++;
                num6 += (num5 += num);
            }

            if (num9 >= num4 || 2 * num6 > num5)
            {
                num8--;
                num6 += (num4 += num3);
            }
        }
    }

    public override void DrawFilledEllipse(Color color, int xCenter, int yCenter, int yR, int xR)
    {
        for (int i = -yR; i <= yR; i++)
        {
            for (int j = -xR; j <= xR; j++)
            {
                if (j * j * yR * yR + i * i * xR * xR <= yR * yR * xR * xR)
                {
                    DrawPoint(color, xCenter + j, yCenter + i);
                }
            }
        }
    }

    public override void DrawArc(int x, int y, int width, int height, Color color, int startAngle = 0, int endAngle = 360)
    {
        if (width != 0 && height != 0)
        {
            for (double num = startAngle; num < (double)endAngle; num += 0.5)
            {
                double num2 = Math.PI * num / 180.0;
                int num3 = (int)((double)width * Math.Cos(num2));
                int num4 = (int)((double)height * Math.Sin(num2));
                DrawPoint(color, x + num3, y + num4);
            }
        }
    }

    public override void DrawPolygon(Color color, params Point[] points)
    {
        if (points == null || points.Length < 3) return;

        for (int i = 0; i < points.Length - 1; i++)
        {
            Point point = points[i];
            Point point2 = points[i + 1];
            DrawLine(color, point.X, point.Y, point2.X, point2.Y);
        }

        Point point3 = points[0];
        Point point4 = points[^1];
        DrawLine(color, point3.X, point3.Y, point4.X, point4.Y);
    }

    public override void DrawSquare(Color color, int x, int y, int size)
    {
        DrawRectangle(color, x, y, size, size);
    }

    public override void DrawRectangle(Color color, int x, int y, int width, int height)
    {
        DrawLine(color, x, y, x + width, y);
        DrawLine(color, x, y, x, y + height);
        DrawLine(color, x, y + height - 1, x + width, y + height - 1);
        DrawLine(color, x + width - 1, y, x + width - 1, y + height);
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

    public override void DrawFilledRectangle(Color color, int xStart, int yStart, int width, int height, bool preventOffBoundPixels = true)
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
        if (color.A == byte.MaxValue)
        {
            for (int y = target.Top; y < target.Bottom; y++)
                Array.Fill(Buffer, argb, y * Width + target.Left, target.Width);
            return;
        }

        for (int y = target.Top; y < target.Bottom; y++)
            for (int x = target.Left; x < target.Right; x++)
                BlendTargetPixel(x, y, argb);
    }

    public override void DrawTriangle(Color color, int v1x, int v1y, int v2x, int v2y, int v3x, int v3y)
    {
        DrawLine(color, v1x, v1y, v2x, v2y);
        DrawLine(color, v1x, v1y, v3x, v3y);
        DrawLine(color, v2x, v2y, v3x, v3y);
    }

    protected bool IsCoordinateValid(int x, int y)
    {
        x += originX;
        y += originY;
        return ContainsClipped(x, y);
    }

    private bool ContainsClipped(int x, int y)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height &&
            x >= clipBounds.Left && x < clipBounds.Right &&
            y >= clipBounds.Top && y < clipBounds.Bottom;
    }

    private void BlendTargetPixel(int x, int y, int color)
    {
        int index = x + y * Width;
        int alpha = (color >> 24) & 0xff;
        if (alpha == 0) return;
        if (alpha == 0xff)
        {
            Buffer[index] = color;
            return;
        }

        int background = Buffer[index];
        int inverse = 255 - alpha;
        int red = (((color >> 16) & 0xff) * alpha + ((background >> 16) & 0xff) * inverse) >> 8;
        int green = (((color >> 8) & 0xff) * alpha + ((background >> 8) & 0xff) * inverse) >> 8;
        int blue = ((color & 0xff) * alpha + (background & 0xff) * inverse) >> 8;
        Buffer[index] = (0xff << 24) | (red << 16) | (green << 8) | blue;
    }

    protected void TrimLine(ref int x1, ref int y1, ref int x2, ref int y2)
    {
        if (x1 == x2)
        {
            x1 = Math.Min((Width - 1), Math.Max(0, x1));
            x2 = x1;
            y1 = Math.Min((Height - 1), Math.Max(0, y1));
            y2 = Math.Min((Height - 1), Math.Max(0, y2));
            return;
        }

        float num = x1;
        float num2 = y1;
        float num3 = x2;
        float num4 = y2;
        float num5 = (num4 - num2) / (num3 - num);
        float num6 = num2 - num5 * num;
        if (num < 0f)
        {
            num = 0f;
            num2 = num6;
        }
        else if (num >= (float)Width)
        {
            num = Width - 1;
            num2 = (float)(Width - 1) * num5 + num6;
        }

        if (num3 < 0f)
        {
            num3 = 0f;
            num4 = num6;
        }
        else if (num3 >= (float)Width)
        {
            num3 = Width - 1;
            num4 = (float)(Width - 1) * num5 + num6;
        }

        if (num2 < 0f)
        {
            num = (0f - num6) / num5;
            num2 = 0f;
        }
        else if (num2 >= (float)Height)
        {
            num = ((float)(Height - 1) - num6) / num5;
            num2 = Height - 1;
        }

        if (num4 < 0f)
        {
            num3 = (0f - num6) / num5;
            num4 = 0f;
        }
        else if (num4 >= (float)Height)
        {
            num3 = ((float)(Height - 1) - num6) / num5;
            num4 = Height - 1;
        }

        if (num < 0f || num >= (float)Width || num2 < 0f || num2 >= (float)Height)
        {
            num = 0f;
            num3 = 0f;
            num2 = 0f;
            num4 = 0f;
        }

        if (num3 < 0f || num3 >= (float)Width || num4 < 0f || num4 >= (float)Height)
        {
            num = 0f;
            num3 = 0f;
            num2 = 0f;
            num4 = 0f;
        }

        x1 = (int)num;
        y1 = (int)num2;
        x2 = (int)num3;
        y2 = (int)num4;
    }

    public new Color AlphaBlend(Color to, Color from, byte alpha)
    {
        byte red = (byte)(to.R * alpha + from.R * (255 - alpha) >> 8);
        byte green = (byte)(to.G * alpha + from.G * (255 - alpha) >> 8);
        byte blue = (byte)(to.B * alpha + from.B * (255 - alpha) >> 8);
        return Color.FromArgb(red, green, blue);
    }

    private bool disposed;

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Buffer = null!;
    }
}
