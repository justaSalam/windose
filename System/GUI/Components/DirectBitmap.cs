using System.Drawing;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;

public unsafe class DirectBitmap
{
    protected Bitmap buffer;
    public int Width { get; private set; }
    public int Height { get; private set; }

    internal int Stride;
    internal int Pitch;

    public Bitmap GetBufferBitmap
    {
        get
        {
            return buffer;
        }
    }

    public DirectBitmap(int width, int height)
    {
        Width = width;
        Height = height;
        buffer = new Bitmap((uint)Width, (uint)Height, ColorDepth.ColorDepth32);


        Stride = 32 / 8;
        Pitch = Width * Stride;
    }

    public void SetPixel(int x, int y, Color color)
    {
        buffer.RawData[x + y * Width] = color.ToArgb();
    }

    public void SetPixelAlpha(int x, int y, int colour)
    {
        int index = x + y * Width;

        if (index < buffer.RawData.Length)
        {
            if ((colour >> 24) == 0xFF)
            {
                buffer.RawData[index] = colour;
                return;
            }

            int bgColour = buffer.RawData[index];
            int alpha = (colour >> 24) & 0xff;
            int invAlpha = 255 - alpha;
            int newRed = (((colour >> 16) & 0xff) * alpha + ((bgColour >> 16) & 0xff) * invAlpha) >> 8;
            int newGreen = (((colour >> 8) & 0xff) * alpha + ((bgColour >> 8) & 0xff) * invAlpha) >> 8;
            int newBlue = ((colour & 0xff) * alpha + (bgColour & 0xff) * invAlpha) >> 8;

            buffer.RawData[index] = (alpha << 24) | (newRed << 16) | (newGreen << 8) | newBlue;
        }
    }
    public void Clear(Color color)
    {
        fixed (int* ptr = buffer.RawData)
            NativeMemory.Fill(ptr, (nuint)(buffer.RawData.Length * 4), (byte)(color.ToArgb() & 0xFF));
    }
    public virtual void DrawPoint(Color color, int x, int y)
    {
        if (buffer == null || x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return;
        }

        if (color.A < byte.MaxValue)
        {
            if (color.A == 0)
            {
                return;
            }

            color = AlphaBlend(color, GetPointColor(x, y), color.A);
        }

        buffer.RawData[y * Width + x] = color.ToArgb();
    }

    public virtual void DrawPoint(int color, int x, int y)
    {
        if (buffer == null || x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return;
        }
        buffer.RawData[y * Width + x] = color;
    }

    public virtual void DrawImage(Image image, int x, int y, bool preventOffBoundPixels = true)
    {
        if (preventOffBoundPixels)
        {
            uint num = Math.Min(image.Width, (uint)(Width - x));
            uint num2 = Math.Min(image.Height, (uint)(Height - y));
            for (int i = 0; i < num; i++)
            {
                for (int j = 0; j < num2; j++)
                {
                    Color color = Color.FromArgb(image.RawData[i + j * image.Width]);
                    DrawPoint(color, x + i, y + j);
                }
            }

            return;
        }

        for (int k = 0; k < image.Width; k++)
        {
            for (int l = 0; l < image.Height; l++)
            {
                Color color = Color.FromArgb(image.RawData[k + l * image.Width]);
                DrawPoint(color, x + k, y + l);
            }
        }
    }

    public virtual Bitmap GetImage(int x, int y, int width, int height)
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

    private static int[] ScaleImage(Image image, int newWidth, int newHeight)
    {
        int[] rawData = image.RawData;
        int width = (int)image.Width;
        uint height = image.Height;
        int[] array = new int[newWidth * newHeight];
        int num = (width << 16) / newWidth + 1;
        int num2 = (int)(height << 16) / newHeight + 1;
        for (int i = 0; i < newHeight; i++)
        {
            for (int j = 0; j < newWidth; j++)
            {
                int num3 = j * num >> 16;
                int num4 = i * num2 >> 16;
                array[i * newWidth + j] = rawData[num4 * width + num3];
            }
        }

        return array;
    }

    public virtual void DrawImage(Image image, int x, int y, int w, int h, bool preventOffBoundPixels = true)
    {
        int[] array = ScaleImage(image, w, h);
        if (preventOffBoundPixels)
        {
            int num = Math.Min(w, (int)Width - x);
            int num2 = Math.Min(h, (int)Height - y);
            for (int i = 0; i < num; i++)
            {
                for (int j = 0; j < num2; j++)
                {
                    Color color = Color.FromArgb(array[i + j * w]);
                    DrawPoint(color, x + i, y + j);
                }
            }

            return;
        }

        for (int k = 0; k < w; k++)
        {
            for (int l = 0; l < h; l++)
            {
                Color color = Color.FromArgb(array[k + l * w]);
                DrawPoint(color, x + k, y + l);
            }
        }
    }

    public virtual void CroppedDrawImage(Image image, int x, int y, int maxWidth, int maxHeight, bool preventOffBoundPixels = true)
    {
        int num = Math.Min((int)image.Width, maxWidth);
        int num2 = Math.Min((int)image.Height, maxHeight);
        int[] rawData = image.RawData;
        for (int i = 0; i < num; i++)
        {
            for (int j = 0; j < num2; j++)
            {
                Color color = Color.FromArgb(rawData[i + j * image.Width]);
                DrawPoint(color, x + i, y + j);
            }
        }
    }

    public void DrawImageAlpha(Image image, int x, int y, bool preventOffBoundPixels = true)
    {
        if (preventOffBoundPixels)
        {
            uint num = Math.Min(image.Width, (uint)(Width - x));
            uint num2 = Math.Min(image.Height, (uint)(Height - y));
            for (int i = 0; i < num; i++)
            {
                for (int j = 0; j < num2; j++)
                {
                    Color color = Color.FromArgb(image.RawData[i + j * image.Width]);
                    DrawPoint(color, x + i, y + j);
                }
            }

            return;
        }

        for (int k = 0; k < image.Width; k++)
        {
            for (int l = 0; l < image.Height; l++)
            {
                Color color = Color.FromArgb(image.RawData[k + l * image.Width]);
                DrawPoint(color, x + k, y + l);
            }
        }
    }
    public void DrawImageStretchAlpha(Bitmap image, Rectangle sourceRect, Rectangle destRect)
    {
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

                int color = image.RawData[srcX + srcY * image.Width];
                SetPixelAlpha(destRect.Left + xi, destRect.Top + yi, color);
            }
        }
    }

    public virtual void DrawString(string str, Font font, Color color, int x, int y)
    {
        int length = str.Length;
        byte width = font.Width;
        for (int i = 0; i < length; i++)
        {
            DrawChar(str[i], font, color, x, y);
            x += width;
        }
    }

    public virtual void DrawString(string str, Font font, Color color, int x, int y, int fontSize)
    {
        int nativeHeight = font.Height;
        if (fontSize <= 0) return;

        int scaledWidth = Math.Max(1, font.Width * fontSize / nativeHeight);
        int cursorX = x;

        for (int i = 0; i < str.Length; i++)
        {
            DrawChar(str[i], font, color, cursorX, y, scaledWidth, fontSize);
            cursorX += scaledWidth;
        }
    }

    public virtual void DrawChar(char c, Font font, Color color, int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return;

        for (int destY = 0; destY < height; destY++)
        {
            int sourceY = destY * font.Height / height;

            for (int destX = 0; destX < width; destX++)
            {
                int sourceX = destX * font.Width / width;

                if (FontPixelSet(c, font, sourceX, sourceY))
                {
                    DrawPoint(color, x + destX, y + destY);
                }
            }
        }
    }

    public virtual void DrawChar(char c, Font font, Color color, int x, int y)
    {
        byte height = font.Height;
        byte width = font.Width;
        byte[] data = font.Data;
        int num = (width + 7) / 8;
        int num2 = height * num * (byte)c;
        for (int i = 0; i < height; i++)
        {
            for (byte b = 0; b < width; b++)
            {
                byte byteToConvert = data[num2 + i * num + b / 8];
                if (font.ConvertByteToBitAddress(byteToConvert, b % 8 + 1))
                {
                    DrawPoint(color, (ushort)(x + b), (ushort)(y + i));
                }
            }
        }
    }
    public virtual Color GetPointColor(int x, int y)
    {
        if (buffer == null)
        {
            return Color.Black;
        }

        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return Color.Black;
        }

        return Color.FromArgb(buffer.RawData[y * Width + x]);
    }
    public virtual int GetRawPointColor(int x, int y)
    {
        if (buffer == null)
        {
            return 0;
        }

        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return 0;
        }

        return buffer.RawData[y * Width + x];
    }

    public int[]? GetBuffer()
    {
        return buffer.RawData;
    }

    internal int GetPointOffset(int x, int y)
    {
        return x * Stride + y * Pitch;
    }

    public virtual void DrawArray(Color[] colors, int x, int y, int width, int height)
    {
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                DrawPoint(colors[j * width + i], x + i, y + j);
            }
        }
    }
    public virtual void DrawArray(int[] colors, int x, int y, int width, int height)
    {
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                DrawPoint(colors[j * width + i], x + i, y + j);
            }
        }
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

        if (width <= 0 || height <= 0) return;

        for (int j = 0; j < height; j++)
        {
            int sourceIndex = (sourceY + j) * sourceWidth + sourceX;
            int destinationIndex = (destinationY + j) * Width + destinationX;

            for (int i = 0; i < width; i++)
            {
                buffer.RawData[destinationIndex + i] = colors[sourceIndex + i];
            }
        }
    }
    internal void DrawHorizontalLine(Color color, int dx, int x1, int y1)
    {
        for (int i = 0; i < dx; i++)
        {
            DrawPoint(color, x1 + i, y1);
        }
    }

    internal void DrawVerticalLine(Color color, int dy, int x1, int y1)
    {
        for (int i = 0; i < dy; i++)
        {
            DrawPoint(color, x1, y1 + i);
        }
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

    public virtual void DrawLine(Color color, int x1, int y1, int x2, int y2)
    {
        TrimLine(ref x1, ref y1, ref x2, ref y2);
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

    public virtual void DrawCircle(Color color, int xCenter, int yCenter, int radius)
    {
        ThrowIfCoordNotValid(xCenter + radius, yCenter);
        ThrowIfCoordNotValid(xCenter - radius, yCenter);
        ThrowIfCoordNotValid(xCenter, yCenter + radius);
        ThrowIfCoordNotValid(xCenter, yCenter - radius);
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

    public virtual void DrawFilledCircle(Color color, int x0, int y0, int radius)
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

    public virtual void DrawEllipse(Color color, int xCenter, int yCenter, int xR, int yR)
    {
        ThrowIfCoordNotValid(xCenter + xR, yCenter);
        ThrowIfCoordNotValid(xCenter - xR, yCenter);
        ThrowIfCoordNotValid(xCenter, yCenter + yR);
        ThrowIfCoordNotValid(xCenter, yCenter - yR);
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

    public virtual void DrawFilledEllipse(Color color, int xCenter, int yCenter, int yR, int xR)
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

    public virtual void DrawArc(int x, int y, int width, int height, Color color, int startAngle = 0, int endAngle = 360)
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

    public virtual void DrawPolygon(Color color, params Point[] points)
    {
        if (points.Length < 3)
        {
            throw new ArgumentException("A polygon requires more than 3 points.");
        }

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

    public virtual void DrawSquare(Color color, int x, int y, int size)
    {
        DrawRectangle(color, x, y, size, size);
    }

    public virtual void DrawRectangle(Color color, int x, int y, int width, int height)
    {
        DrawLine(color, x, y, x + width, y);
        DrawLine(color, x, y, x, y + height);
        DrawLine(color, x, y + height - 1, x + width, y + height - 1);
        DrawLine(color, x + width - 1, y, x + width - 1, y + height);
    }

    public virtual void DrawFilledRectangle(Color color, int xStart, int yStart, int width, int height, bool preventOffBoundPixels = true)
    {
        if (height == -1)
        {
            height = width;
        }

        if (preventOffBoundPixels)
        {
            width = Math.Min(width, (int)Width - xStart);
            height = Math.Min(height, (int)Height - yStart);
        }
        for (int i = yStart; i < yStart + height; i++)
        {
            DrawLine(color, xStart, i, xStart + width - 1, i);
        }
    }

    public virtual void DrawTriangle(Color color, int v1x, int v1y, int v2x, int v2y, int v3x, int v3y)
    {
        DrawLine(color, v1x, v1y, v2x, v2y);
        DrawLine(color, v1x, v1y, v3x, v3y);
        DrawLine(color, v2x, v2y, v3x, v3y);
    }

    protected void ThrowIfCoordNotValid(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            throw new ArgumentOutOfRangeException("x", $"X coordinate ({x}) is not between 0 and {Width}");
        }

        if (y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException("y", $"Y coordinate ({y}) is not between 0 and {Height}");
        }
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

    public virtual Color AlphaBlend(Color to, Color from, byte alpha)
    {
        byte red = (byte)(to.R * alpha + from.R * (255 - alpha) >> 8);
        byte green = (byte)(to.G * alpha + from.G * (255 - alpha) >> 8);
        byte blue = (byte)(to.B * alpha + from.B * (255 - alpha) >> 8);
        return Color.FromArgb(red, green, blue);
    }
}
