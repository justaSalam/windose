using System;
using System.Drawing;
using Windose;

namespace Windose.System.Media
{
    public enum VisualizerMode
    {
        Bars,
        Oscilloscope,
        Ambience,
        Off
    }

    /// <summary>
    /// Real-time audio and media visualizer for Windows Media Player.
    /// Provides Classic Spectrum Bars, Oscilloscope Wave, and Ambient Orb visuals.
    /// </summary>
    public class MediaVisualizer
    {
        public VisualizerMode Mode { get; set; } = VisualizerMode.Bars;

        private const int BarCount = 24;
        private readonly float[] barHeights = new float[BarCount];
        private readonly float[] peakHeights = new float[BarCount];
        private readonly float[] peakVelocity = new float[BarCount];

        public void CycleMode()
        {
            switch (Mode)
            {
                case VisualizerMode.Bars: Mode = VisualizerMode.Oscilloscope; break;
                case VisualizerMode.Oscilloscope: Mode = VisualizerMode.Ambience; break;
                case VisualizerMode.Ambience: Mode = VisualizerMode.Bars; break;
                default: Mode = VisualizerMode.Bars; break;
            }
        }

        public void Draw(ComponentBuffer buffer, int x, int y, int width, int height, bool isPlaying, int tick, string title, string subtitle)
        {
            if (width <= 0 || height <= 0) return;

            // Deep cinema charcoal backdrop with vignette
            buffer.DrawFilledRectangle(Color.FromArgb(12, 14, 20), x, y, width, height);

            if (Mode == VisualizerMode.Off) return;

            switch (Mode)
            {
                case VisualizerMode.Bars:
                    DrawSpectrumBars(buffer, x, y, width, height, isPlaying, tick);
                    break;
                case VisualizerMode.Oscilloscope:
                    DrawOscilloscope(buffer, x, y, width, height, isPlaying, tick);
                    break;
                case VisualizerMode.Ambience:
                    DrawAmbience(buffer, x, y, width, height, isPlaying, tick);
                    break;
            }

            // Media Title & Subtitle overlay
            if (!string.IsNullOrEmpty(title))
            {
                int textY = y + 16;
                buffer.DrawString(title, SystemFonts.msSansSerif, 16, Color.White, x + 20, textY);
                if (!string.IsNullOrEmpty(subtitle))
                {
                    buffer.DrawString(subtitle, SystemFonts.msSansSerif, 12, Color.FromArgb(160, 180, 210), x + 20, textY + 22);
                }
            }
        }

        private void DrawSpectrumBars(ComponentBuffer buffer, int x, int y, int width, int height, bool isPlaying, int tick)
        {
            int marginX = Math.Max(20, width / 12);
            int availW = width - marginX * 2;
            int barSpacing = 4;
            int barW = Math.Max(4, (availW - (BarCount - 1) * barSpacing) / BarCount);
            int maxBarH = Math.Max(30, height - 80);
            int baseY = y + height - 30;

            float time = tick * 0.003f;

            for (int i = 0; i < BarCount; i++)
            {
                float target = 0f;
                if (isPlaying)
                {
                    // Algorithmic music rhythm spectrum
                    float freq = (i + 1) * 0.4f;
                    float wave1 = (float)Math.Sin(time * 4f + i * 0.5f);
                    float wave2 = (float)Math.Cos(time * 2.5f - i * 0.3f);
                    float wave3 = (float)Math.Sin(time * 7f + i * freq);
                    target = Math.Clamp((wave1 * 0.4f + wave2 * 0.35f + wave3 * 0.25f + 0.6f) * (1.0f - (i * 0.02f)), 0.05f, 1.0f);
                }
                else
                {
                    target = 0.04f;
                }

                // Smooth attack and release
                barHeights[i] = barHeights[i] * 0.7f + target * 0.3f;
                float currentH = barHeights[i] * maxBarH;

                // Peaks with gravity
                if (currentH > peakHeights[i])
                {
                    peakHeights[i] = currentH;
                    peakVelocity[i] = 0f;
                }
                else
                {
                    peakVelocity[i] += 0.4f;
                    peakHeights[i] -= peakVelocity[i];
                    if (peakHeights[i] < currentH) peakHeights[i] = currentH;
                }

                int bx = x + marginX + i * (barW + barSpacing);
                int bh = (int)currentH;
                int by = baseY - bh;

                // Draw segmented vertical gradient bar
                int segH = 4;
                int segGap = 1;
                for (int sy = 0; sy < bh; sy += segH + segGap)
                {
                    float ratio = (float)sy / maxBarH;
                    Color segColor;
                    if (ratio < 0.55f)
                    {
                        // Cyan to Bright Green
                        int r = (int)(ratio * 80);
                        int g = 180 + (int)(ratio * 75);
                        int b = 255 - (int)(ratio * 200);
                        segColor = Color.FromArgb(Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
                    }
                    else if (ratio < 0.82f)
                    {
                        // Yellow / Amber
                        segColor = Color.FromArgb(240, 210, 40);
                    }
                    else
                    {
                        // Red / Coral Peak
                        segColor = Color.FromArgb(255, 60, 60);
                    }

                    int curSegH = Math.Min(segH, bh - sy);
                    buffer.DrawFilledRectangle(segColor, bx, baseY - sy - curSegH, barW, curSegH);
                }

                // Draw peak cap
                int py = baseY - (int)peakHeights[i] - 3;
                if (py >= y + 10 && py < baseY)
                {
                    buffer.DrawFilledRectangle(Color.FromArgb(255, 255, 255), bx, py, barW, 2);
                }
            }
        }

        private void DrawOscilloscope(ComponentBuffer buffer, int x, int y, int width, int height, bool isPlaying, int tick)
        {
            int centerY = y + height / 2 + 10;
            int amp = Math.Min(height / 3, 100);
            float time = tick * 0.005f;

            Color glowColor = Color.FromArgb(30, 80, 160);
            Color beamColor = Color.FromArgb(0, 230, 255);
            Color coreColor = Color.FromArgb(255, 255, 255);

            // Draw center reference grid line
            buffer.DrawLine(Color.FromArgb(25, 35, 50), x + 10, centerY, width - 20, 1);

            int prevY = centerY;
            for (int px = 0; px < width - 40; px += 2)
            {
                float t = (float)px / (width - 40);
                float wave = 0f;
                if (isPlaying)
                {
                    wave = (float)(Math.Sin(t * 18.0 + time * 6.0) * 0.5 +
                                   Math.Sin(t * 36.0 - time * 9.0) * 0.3 +
                                   Math.Sin(t * 6.0 + time * 3.0) * 0.2);
                }
                else
                {
                    wave = (float)(Math.Sin(t * 8.0 + time * 1.5) * 0.08);
                }

                int curY = centerY + (int)(wave * amp);
                if (px > 0)
                {
                    // Draw neon glow beam
                    int drawX = x + 20 + px;
                    buffer.DrawLine(glowColor, drawX - 2, prevY - 1, 2, curY - prevY + 2);
                    buffer.DrawLine(beamColor, drawX - 2, prevY, 2, curY - prevY);
                    buffer.DrawLine(coreColor, drawX - 2, prevY, 1, Math.Max(1, Math.Abs(curY - prevY)));
                }
                prevY = curY;
            }
        }

        private void DrawAmbience(ComponentBuffer buffer, int x, int y, int width, int height, bool isPlaying, int tick)
        {
            int cx = x + width / 2;
            int cy = y + height / 2 + 10;
            float time = tick * 0.002f;

            int rings = isPlaying ? 5 : 2;
            for (int r = 1; r <= rings; r++)
            {
                float pulse = (float)Math.Sin(time * 3f + r * 1.2f);
                int radius = (int)(r * 28 + pulse * 14);
                if (radius <= 0) continue;

                int alpha = Math.Clamp(180 - r * 30, 20, 255);
                Color ringCol = Color.FromArgb(alpha, 60 + r * 25, 140 + r * 20, 255);

                // Approximate circle with polygon points
                int steps = 24;
                int prevPx = cx + radius, prevPy = cy;
                for (int s = 1; s <= steps; s++)
                {
                    double angle = (s * 2.0 * Math.PI) / steps;
                    int px = cx + (int)(Math.Cos(angle) * radius);
                    int py = cy + (int)(Math.Sin(angle) * (radius * 0.65)); // 3D ellipse perspective
                    buffer.DrawLine(ringCol, prevPx, prevPy, px - prevPx, py - prevPy);
                    prevPx = px;
                    prevPy = py;
                }
            }
        }
    }
}
