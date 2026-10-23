using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Windose;

namespace Windose.System.Media
{
    /// <summary>
    /// Generates high-quality animated video frames and audio chords for the built-in demo media.
    /// Allows immediate testing and playback in Windows Media Player on any system.
    /// </summary>
    public static class DemoMediaGenerator
    {
        private static List<Bitmap>? cachedFrames;
        private static byte[]? cachedAudio;

        public static List<Bitmap> GetDemoFrames(int width = 320, int height = 240, int frameCount = 60)
        {
            if (cachedFrames != null && cachedFrames.Count == frameCount) return cachedFrames;

            List<Bitmap> frames = new List<Bitmap>(frameCount);
            int cx = width / 2;
            int cy = height / 2 - 10;

            for (int f = 0; f < frameCount; f++)
            {
                Bitmap bmp = new Bitmap(width, height, ColorDepth.ColorDepth32);
                int[] raw = bmp.RawData;
                if (raw == null)
                {
                    frames.Add(bmp);
                    continue;
                }

                float t = (float)f / frameCount;
                double angle = t * 2.0 * Math.PI;

                // Gradient background (Deep navy to dark steel)
                for (int y = 0; y < height; y++)
                {
                    float yRatio = (float)y / height;
                    int r = (int)(10 + yRatio * 15);
                    int g = (int)(14 + yRatio * 20);
                    int b = (int)(30 + yRatio * 45);
                    int col = unchecked((int)(0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
                    int row = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        raw[row + x] = col;
                    }
                }

                // Render orbiting glowing particles
                int particleCount = 12;
                for (int p = 0; p < particleCount; p++)
                {
                    double pAngle = angle + (p * 2.0 * Math.PI / particleCount);
                    int px = cx + (int)(Math.Cos(pAngle) * 85);
                    int py = cy + (int)(Math.Sin(pAngle) * 45); // isometric tilt

                    int pCol = (p % 2 == 0)
                        ? unchecked((int)0xFF00D2FF)  // Cyan
                        : unchecked((int)0xFFFFB400); // Amber

                    DrawFilledCircle(raw, width, height, px, py, 3, pCol);
                }

                // Render central Windose Emblem (4 floating tiles)
                int tileSz = 26;
                int gap = 4;
                float wobble = (float)Math.Sin(angle * 2.0) * 4f;

                // Red/Amber tile (Top-Left)
                DrawTile(raw, width, height, cx - tileSz - gap, cy - tileSz - gap + (int)wobble, tileSz, 0xFFFF3B30);
                // Green/Teal tile (Top-Right)
                DrawTile(raw, width, height, cx + gap, cy - tileSz - gap - (int)wobble, tileSz, 0xFF34C759);
                // Blue tile (Bottom-Left)
                DrawTile(raw, width, height, cx - tileSz - gap, cy + gap - (int)wobble, tileSz, 0xFF007AFF);
                // Yellow tile (Bottom-Right)
                DrawTile(raw, width, height, cx + gap, cy + gap + (int)wobble, tileSz, 0xFFFFCC00);

                // Animated lower waveform indicator
                int waveY = height - 25;
                for (int x = 20; x < width - 20; x++)
                {
                    float waveT = (float)(x - 20) / (width - 40);
                    int waveH = (int)(Math.Sin(waveT * 12.0 + angle * 3.0) * 8.0);
                    int wy = waveY + waveH;
                    if (wy >= 0 && wy < height)
                    {
                        raw[wy * width + x] = unchecked((int)0xFF00E5FF);
                    }
                }

                frames.Add(bmp);
            }

            cachedFrames = frames;
            return frames;
        }

        public static byte[] GetDemoAudio()
        {
            if (cachedAudio != null) return cachedAudio;
            cachedAudio = AudioEngine.CreateChimeWave(2500, 440f, 554.37f, 659.25f);
            return cachedAudio;
        }

        private static void DrawFilledCircle(int[] raw, int w, int h, int cx, int cy, int rad, int color)
        {
            int r2 = rad * rad;
            for (int dy = -rad; dy <= rad; dy++)
            {
                int py = cy + dy;
                if (py < 0 || py >= h) continue;
                int row = py * w;
                for (int dx = -rad; dx <= rad; dx++)
                {
                    int px = cx + dx;
                    if (px >= 0 && px < w && dx * dx + dy * dy <= r2)
                    {
                        raw[row + px] = color;
                    }
                }
            }
        }

        private static void DrawTile(int[] raw, int w, int h, int x, int y, int size, uint color)
        {
            for (int dy = 0; dy < size; dy++)
            {
                int py = y + dy;
                if (py < 0 || py >= h) continue;
                int row = py * w;
                for (int dx = 0; dx < size; dx++)
                {
                    int px = x + dx;
                    if (px >= 0 && px < w)
                    {
                        raw[row + px] = unchecked((int)color);
                    }
                }
            }
        }
    }
}
