using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using Windose;
using Windose.Programs;

namespace Windose.Programs
{
    public enum PaintTool
    {
        Pencil,
        Brush,
        Eraser,
        Bucket,
        Line,
        Rectangle,
        Ellipse
    }

    /// <summary>
    /// Windows 7 MS Paint native applet.
    /// Provides freehand drawing, shapes, color palette, brush sizes, and flood fill.
    /// </summary>
    public class Paint : Window
    {
        private const int CanvasWidth = 640;
        private const int CanvasHeight = 440;
        private const int CanvasX = 12;
        private const int CanvasY = 64;

        private readonly Bitmap canvasBitmap;
        private PaintTool currentTool = PaintTool.Pencil;
        private Color primaryColor = Color.Black;
        private Color secondaryColor = Color.White;
        private int strokeSize = 2;

        private bool isDrawing;
        private int lastCanvasX = -1;
        private int lastCanvasY = -1;
        private int dragStartX = -1;
        private int dragStartY = -1;
        private int previewX = -1;
        private int previewY = -1;

        public Paint(int x = 120, int y = 60)
            : base(x, y, 664, 524, "Paint", true)
        {
            canResize = false;
            canMaximize = true;

            if (Palette.GlassEnabled)
            {
                useGlass = true;
            }

            canvasBitmap = new Bitmap(CanvasWidth, CanvasHeight, ColorDepth.ColorDepth32);
            ClearCanvas();

            // Toolbar: Tools Row
            int toolX = 12;
            int toolY = 10;
            int toolW = 54;
            int toolH = 24;
            int gap = 4;

            CreateToolButton("Pencil", PaintTool.Pencil, toolX, toolY, toolW, toolH);
            toolX += toolW + gap;
            CreateToolButton("Brush", PaintTool.Brush, toolX, toolY, toolW, toolH);
            toolX += toolW + gap;
            CreateToolButton("Eraser", PaintTool.Eraser, toolX, toolY, toolW, toolH);
            toolX += toolW + gap;
            CreateToolButton("Fill", PaintTool.Bucket, toolX, toolY, toolW, toolH);
            toolX += toolW + gap;
            CreateToolButton("Line", PaintTool.Line, toolX, toolY, toolW, toolH);
            toolX += toolW + gap;
            CreateToolButton("Rect", PaintTool.Rectangle, toolX, toolY, toolW, toolH);
            toolX += toolW + gap;
            CreateToolButton("Oval", PaintTool.Ellipse, toolX, toolY, toolW, toolH);
            toolX += toolW + gap;

            Button clearBtn = new Button("Clear", toolX, toolY, 48, toolH);
            clearBtn.leftClickAction = () =>
            {
                ClearCanvas();
                MarkDirty();
            };
            AddChild(clearBtn);

            // Size Selector Buttons
            int sizeX = 430;
            int sizeY = 10;
            int sizeW = 28;
            int[] sizes = new int[] { 1, 2, 4, 8 };
            for (int i = 0; i < sizes.Length; i++)
            {
                int sz = sizes[i];
                Button szBtn = new Button($"{sz}px", sizeX + i * (sizeW + 2), sizeY, sizeW, toolH);
                szBtn.leftClickAction = () => strokeSize = sz;
                AddChild(szBtn);
            }

            // Color Palette Row
            Color[] paletteColors = new Color[]
            {
                Color.Black, Color.FromArgb(64, 64, 64), Color.FromArgb(128, 128, 128), Color.White,
                Color.FromArgb(192, 0, 0), Color.Red, Color.FromArgb(255, 128, 0), Color.Yellow,
                Color.FromArgb(0, 128, 0), Color.FromArgb(0, 255, 0), Color.FromArgb(0, 192, 255), Color.Blue,
                Color.FromArgb(0, 0, 128), Color.Purple, Color.Magenta, Color.FromArgb(128, 64, 0)
            };

            int palX = 12;
            int palY = 38;
            int swatchSize = 18;
            for (int i = 0; i < paletteColors.Length; i++)
            {
                Color c = paletteColors[i];
                Button swatch = new Button("", palX + i * (swatchSize + 3), palY, swatchSize, swatchSize);
                swatch.useBackground = true;
                swatch.leftClickAction = () =>
                {
                    primaryColor = c;
                    MarkDirty();
                };
                swatch.rightClickAction = () =>
                {
                    secondaryColor = c;
                    MarkDirty();
                };
                AddChild(swatch);
            }
        }

        private void CreateToolButton(string label, PaintTool tool, int x, int y, int w, int h)
        {
            Button btn = new Button(label, x, y, w, h);
            btn.leftClickAction = () =>
            {
                currentTool = tool;
                MarkDirty();
            };
            AddChild(btn);
        }

        private void ClearCanvas()
        {
            int bg = Color.White.ToArgb();
            int[] raw = canvasBitmap.RawData;
            for (int i = 0; i < raw.Length; i++)
                raw[i] = bg;
        }

        public override void DrawLocal()
        {
            base.DrawLocal();

            // Toolbar background frame
            DrawGradientVertical(Color.FromArgb(245, 247, 250), Color.FromArgb(228, 232, 238), 6, 6, Width - 12, 54);
            DrawRoundedRectangle(Color.FromArgb(180, 195, 210), 6, 6, Width - 12, 54, 3);

            // Active Color Preview Swatches (Primary and Secondary)
            DrawFilledRectangle(secondaryColor, 560, 36, 20, 20);
            DrawRectangle(Color.FromArgb(100, 100, 100), 560, 36, 20, 20);
            DrawFilledRectangle(primaryColor, 550, 26, 20, 20);
            DrawRectangle(Color.Black, 550, 26, 20, 20);

            // Canvas frame
            DrawSunkenRectangle(CanvasX - 1, CanvasY - 1, CanvasWidth + 2, CanvasHeight + 2);
            DrawImage(canvasBitmap, CanvasX, CanvasY);

            // Shape drag preview
            if (isDrawing && previewX >= 0 && previewY >= 0)
            {
                Color previewCol = currentTool == PaintTool.Eraser ? secondaryColor : primaryColor;
                int absStartX = CanvasX + dragStartX;
                int absStartY = CanvasY + dragStartY;
                int absCurX = CanvasX + previewX;
                int absCurY = CanvasY + previewY;

                if (currentTool == PaintTool.Line)
                {
                    DrawLine(previewCol, absStartX, absStartY, absCurX, absCurY);
                }
                else if (currentTool == PaintTool.Rectangle)
                {
                    int rx = Math.Min(absStartX, absCurX);
                    int ry = Math.Min(absStartY, absCurY);
                    int rw = Math.Abs(absCurX - absStartX);
                    int rh = Math.Abs(absCurY - absStartY);
                    DrawRectangle(previewCol, rx, ry, rw, rh);
                }
                else if (currentTool == PaintTool.Ellipse)
                {
                    int rx = Math.Min(absStartX, absCurX);
                    int ry = Math.Min(absStartY, absCurY);
                    int rw = Math.Abs(absCurX - absStartX);
                    int rh = Math.Abs(absCurY - absStartY);
                    DrawRectangle(previewCol, rx, ry, rw, rh);
                }
            }
        }

        public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
        {
            int cx = mouseX - CanvasX;
            int cy = mouseY - CanvasY;
            bool insideCanvas = (cx >= 0 && cx < CanvasWidth && cy >= 0 && cy < CanvasHeight);

            switch (mouse.left)
            {
                case MouseEvents.Press:
                    if (insideCanvas)
                    {
                        isDrawing = true;
                        dragStartX = cx;
                        dragStartY = cy;
                        lastCanvasX = cx;
                        lastCanvasY = cy;
                        previewX = cx;
                        previewY = cy;

                        if (currentTool == PaintTool.Pencil || currentTool == PaintTool.Brush)
                        {
                            DrawBrushPoint(cx, cy, primaryColor, currentTool == PaintTool.Brush ? strokeSize * 2 : strokeSize);
                            MarkDirty();
                        }
                        else if (currentTool == PaintTool.Eraser)
                        {
                            DrawBrushPoint(cx, cy, secondaryColor, strokeSize * 4);
                            MarkDirty();
                        }
                        else if (currentTool == PaintTool.Bucket)
                        {
                            FloodFill(cx, cy, primaryColor);
                            isDrawing = false;
                            MarkDirty();
                        }
                        return true;
                    }
                    break;

                case MouseEvents.Hold:
                    if (isDrawing)
                    {
                        previewX = Math.Clamp(cx, 0, CanvasWidth - 1);
                        previewY = Math.Clamp(cy, 0, CanvasHeight - 1);

                        if (currentTool == PaintTool.Pencil || currentTool == PaintTool.Brush)
                        {
                            DrawLineSegment(lastCanvasX, lastCanvasY, previewX, previewY, primaryColor, currentTool == PaintTool.Brush ? strokeSize * 2 : strokeSize);
                            lastCanvasX = previewX;
                            lastCanvasY = previewY;
                        }
                        else if (currentTool == PaintTool.Eraser)
                        {
                            DrawLineSegment(lastCanvasX, lastCanvasY, previewX, previewY, secondaryColor, strokeSize * 4);
                            lastCanvasX = previewX;
                            lastCanvasY = previewY;
                        }

                        MarkDirty();
                        return true;
                    }
                    break;

                case MouseEvents.Release:
                    if (isDrawing)
                    {
                        int endX = Math.Clamp(cx, 0, CanvasWidth - 1);
                        int endY = Math.Clamp(cy, 0, CanvasHeight - 1);

                        if (currentTool == PaintTool.Line)
                        {
                            DrawLineSegment(dragStartX, dragStartY, endX, endY, primaryColor, strokeSize);
                        }
                        else if (currentTool == PaintTool.Rectangle)
                        {
                            CommitRectangle(dragStartX, dragStartY, endX, endY, primaryColor, strokeSize);
                        }
                        else if (currentTool == PaintTool.Ellipse)
                        {
                            CommitEllipse(dragStartX, dragStartY, endX, endY, primaryColor, strokeSize);
                        }

                        isDrawing = false;
                        lastCanvasX = -1;
                        lastCanvasY = -1;
                        previewX = -1;
                        previewY = -1;
                        MarkDirty();
                        return true;
                    }
                    break;
            }

            return base.HandleInput(mouseX, mouseY, mouse);
        }

        private void SetPixel(int x, int y, int argb)
        {
            if (x >= 0 && x < CanvasWidth && y >= 0 && y < CanvasHeight)
            {
                canvasBitmap.RawData[y * CanvasWidth + x] = argb;
            }
        }

        private void DrawBrushPoint(int cx, int cy, Color color, int radius)
        {
            int argb = color.ToArgb();
            int r = Math.Max(1, radius);
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy <= r * r)
                    {
                        SetPixel(cx + dx, cy + dy, argb);
                    }
                }
            }
        }

        private void DrawLineSegment(int x0, int y0, int x1, int y1, Color color, int radius)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                DrawBrushPoint(x0, y0, color, radius);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private void CommitRectangle(int x0, int y0, int x1, int y1, Color color, int width)
        {
            int rx = Math.Min(x0, x1);
            int ry = Math.Min(y0, y1);
            int rw = Math.Abs(x1 - x0);
            int rh = Math.Abs(y1 - y0);

            for (int t = 0; t < width; t++)
            {
                for (int x = rx; x <= rx + rw; x++)
                {
                    SetPixel(x, ry + t, color.ToArgb());
                    SetPixel(x, ry + rh - t, color.ToArgb());
                }
                for (int y = ry; y <= ry + rh; y++)
                {
                    SetPixel(rx + t, y, color.ToArgb());
                    SetPixel(rx + rw - t, y, color.ToArgb());
                }
            }
        }

        private void CommitEllipse(int x0, int y0, int x1, int y1, Color color, int width)
        {
            int rx = Math.Abs(x1 - x0) / 2;
            int ry = Math.Abs(y1 - y0) / 2;
            int xc = Math.Min(x0, x1) + rx;
            int yc = Math.Min(y0, y1) + ry;

            if (rx <= 0 || ry <= 0) return;

            int argb = color.ToArgb();
            int x = 0;
            int y = ry;
            long rx2 = (long)rx * rx;
            long ry2 = (long)ry * ry;
            long p = ry2 - (rx2 * ry) + (rx2 / 4);

            while (2 * ry2 * x <= 2 * rx2 * y)
            {
                PlotEllipsePoints(xc, yc, x, y, argb, width);
                x++;
                if (p < 0)
                {
                    p += 2 * ry2 * x + ry2;
                }
                else
                {
                    y--;
                    p += 2 * ry2 * x - 2 * rx2 * y + ry2;
                }
            }

            p = (long)(ry2 * (x + 0.5) * (x + 0.5) + rx2 * (y - 1) * (y - 1) - rx2 * ry2);
            while (y >= 0)
            {
                PlotEllipsePoints(xc, yc, x, y, argb, width);
                y--;
                if (p > 0)
                {
                    p -= 2 * rx2 * y + rx2;
                }
                else
                {
                    x++;
                    p += 2 * ry2 * x - 2 * rx2 * y + rx2;
                }
            }
        }

        private void PlotEllipsePoints(int xc, int yc, int x, int y, int argb, int width)
        {
            for (int w = 0; w < width; w++)
            {
                SetPixel(xc + x + w, yc + y, argb);
                SetPixel(xc - x - w, yc + y, argb);
                SetPixel(xc + x + w, yc - y, argb);
                SetPixel(xc - x - w, yc - y, argb);
            }
        }

        private void FloodFill(int startX, int startY, Color fillCol)
        {
            int targetArgb = canvasBitmap.RawData[startY * CanvasWidth + startX];
            int fillArgb = fillCol.ToArgb();
            if (targetArgb == fillArgb) return;

            Queue<int> q = new Queue<int>();
            q.Enqueue((startY << 16) | startX);

            int[] raw = canvasBitmap.RawData;
            while (q.Count > 0 && q.Count < 30000)
            {
                int val = q.Dequeue();
                int x = val & 0xFFFF;
                int y = (val >> 16) & 0xFFFF;

                if (x < 0 || x >= CanvasWidth || y < 0 || y >= CanvasHeight) continue;
                int idx = y * CanvasWidth + x;
                if (raw[idx] != targetArgb) continue;

                raw[idx] = fillArgb;
                q.Enqueue((y << 16) | (x + 1));
                q.Enqueue((y << 16) | (x - 1));
                q.Enqueue(((y + 1) << 16) | x);
                q.Enqueue(((y - 1) << 16) | x);
            }
        }

        public override string GetComponentName() => "Paint";
    }
}
