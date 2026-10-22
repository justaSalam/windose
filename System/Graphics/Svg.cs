using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Cosmos.Kernel.System.Graphics
{
    /// <summary>
    /// Scalable Vector Graphics (SVG) parser and software rasterizer inheriting from Cosmos Image.
    /// Supports vector paths, Bézier curves, arcs, shapes, groups, viewBox scaling, CSS currentColor,
    /// Even-Odd scanline polygon filling, and anti-aliased thick stroking.
    /// </summary>
    public class Svg : Image
    {
        public Svg(string path) : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("SVG file not found", path);

            byte[] data = File.ReadAllBytes(path);
            Decode(data, 0, 0, 0xFFFFFFFF);
        }

        public Svg(byte[] data, int targetW = 0, int targetH = 0, uint defaultColor = 0xFFFFFFFF)
            : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentNullException(nameof(data));

            Decode(data, targetW, targetH, defaultColor);
        }

        public Svg(string svgXml, int targetW = 0, int targetH = 0, uint defaultColor = 0xFFFFFFFF)
            : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (string.IsNullOrEmpty(svgXml))
                throw new ArgumentNullException(nameof(svgXml));

            byte[] data = Encoding.UTF8.GetBytes(svgXml);
            Decode(data, targetW, targetH, defaultColor);
        }

        public Svg(DomNode svgNode, int targetW = 0, int targetH = 0, uint defaultColor = 0xFFFFFFFF)
            : base(0, 0, ColorDepth.ColorDepth32)
        {
            if (svgNode == null)
                throw new ArgumentNullException(nameof(svgNode));

            DecodeDom(svgNode, targetW, targetH, defaultColor);
        }

        public Svg(int width, int height) : base(width, height, ColorDepth.ColorDepth32)
        {
            ImageAccessor.Initialize(this, width, height, new int[width * height]);
        }

        public static Image FromDom(DomNode svgNode, int targetW, int targetH, uint currentColor)
        {
            if (svgNode == null) return new Svg(Math.Max(1, targetW > 0 ? targetW : 24), Math.Max(1, targetH > 0 ? targetH : 24));
            return new Svg(svgNode, targetW, targetH, currentColor);
        }

        private void Decode(byte[] data, int targetW, int targetH, uint defaultColor)
        {
            string xml = Encoding.UTF8.GetString(data);
            DomNode doc = HtmlDom.Parse(xml);
            DomNode svg = FindSvgNode(doc);
            if (svg == null)
            {
                int w = Math.Max(1, targetW > 0 ? targetW : 24);
                int h = Math.Max(1, targetH > 0 ? targetH : 24);
                ImageAccessor.Initialize(this, w, h, new int[w * h]);
                return;
            }
            DecodeDom(svg, targetW, targetH, defaultColor);
        }

        private static DomNode FindSvgNode(DomNode root)
        {
            if (root == null) return null;
            if (root.IsElement && root.Tag == "svg") return root;

            DomNode ch = root.FirstChild;
            while (ch != null)
            {
                DomNode found = FindSvgNode(ch);
                if (found != null) return found;
                ch = ch.Next;
            }
            return null;
        }

        private void DecodeDom(DomNode svgNode, int targetW, int targetH, uint defaultColor)
        {
            // 1. Determine viewBox
            float vbX = 0, vbY = 0, vbW = 0, vbH = 0;
            string vbStr = svgNode.GetAttr("viewBox");
            if (!string.IsNullOrEmpty(vbStr))
            {
                ParseViewBox(vbStr, out vbX, out vbY, out vbW, out vbH);
            }

            // Fallback viewBox from width/height attributes if missing
            int attrW = ParseDimension(svgNode.GetAttr("width"), 0);
            int attrH = ParseDimension(svgNode.GetAttr("height"), 0);

            if (vbW <= 0 && attrW > 0) vbW = attrW;
            if (vbH <= 0 && attrH > 0) vbH = attrH;
            if (vbW <= 0) vbW = 24.0f;
            if (vbH <= 0) vbH = 24.0f;

            // 2. Determine target raster size
            int outW = targetW;
            int outH = targetH;
            if (outW <= 0) outW = attrW > 0 ? attrW : (int)Math.Ceiling(vbW);
            if (outH <= 0) outH = attrH > 0 ? attrH : (int)Math.Ceiling(vbH);
            if (outW <= 0) outW = 24;
            if (outH <= 0) outH = 24;

            // Clamp dimensions to sane maximum to prevent out-of-memory
            if (outW > 1024) outW = 1024;
            if (outH > 1024) outH = 1024;
            if (outW < 1) outW = 1;
            if (outH < 1) outH = 1;

            int[] pixels = new int[outW * outH];

            // 3. Coordinate transform: map viewBox to target raster buffer (xMidYMid meet)
            float scale = Math.Min((float)outW / vbW, (float)outH / vbH);
            float offX = (outW - vbW * scale) / 2.0f - vbX * scale;
            float offY = (outH - vbH * scale) / 2.0f - vbY * scale;

            // 4. Index defs elements by id
            Dictionary<string, DomNode> defs = new Dictionary<string, DomNode>(StringComparer.OrdinalIgnoreCase);
            CollectDefs(svgNode, defs);

            // 5. Initial SVG State
            SvgState rootState = new SvgState
            {
                FillColor = null,
                StrokeColor = null,
                StrokeWidth = 1.0f,
                Opacity = 1.0f,
                Scale = scale,
                OffX = offX,
                OffY = offY,
                CurrentColor = defaultColor != 0 ? defaultColor : 0xFFFFFFFF
            };

            // Inspect root <svg> attributes
            ApplyElementStyle(svgNode, ref rootState);

            // If root has no explicit fill, default for SVG is black (0xFF000000)
            if (!rootState.FillColor.HasValue)
                rootState.FillColor = 0xFF000000;
            if (!rootState.StrokeColor.HasValue)
                rootState.StrokeColor = 0; // none

            // 6. Traverse and rasterize children
            DomNode child = svgNode.FirstChild;
            while (child != null)
            {
                if (child.IsElement && child.Tag != "defs")
                {
                    RenderElement(child, rootState, pixels, outW, outH, defs);
                }
                child = child.Next;
            }

            ImageAccessor.Initialize(this, outW, outH, pixels);
        }

        private static void CollectDefs(DomNode node, Dictionary<string, DomNode> defs)
        {
            if (node == null) return;
            if (node.IsElement)
            {
                string id = node.Id;
                if (!string.IsNullOrEmpty(id) && !defs.ContainsKey(id))
                    defs[id] = node;
            }
            DomNode ch = node.FirstChild;
            while (ch != null)
            {
                CollectDefs(ch, defs);
                ch = ch.Next;
            }
        }

        private static void RenderElement(DomNode node, SvgState parentState, int[] pixels, int outW, int outH, Dictionary<string, DomNode> defs)
        {
            if (node == null || !node.IsElement) return;

            string tag = node.Tag;
            SvgState state = parentState;
            ApplyElementStyle(node, ref state);

            switch (tag)
            {
                case "g":
                    DomNode gChild = node.FirstChild;
                    while (gChild != null)
                    {
                        RenderElement(gChild, state, pixels, outW, outH, defs);
                        gChild = gChild.Next;
                    }
                    break;

                case "path":
                    RenderPath(node, state, pixels, outW, outH);
                    break;

                case "rect":
                    RenderRect(node, state, pixels, outW, outH);
                    break;

                case "circle":
                    RenderCircle(node, state, pixels, outW, outH);
                    break;

                case "ellipse":
                    RenderEllipse(node, state, pixels, outW, outH);
                    break;

                case "line":
                    RenderLine(node, state, pixels, outW, outH);
                    break;

                case "polyline":
                    RenderPoly(node, state, pixels, outW, outH, false);
                    break;

                case "polygon":
                    RenderPoly(node, state, pixels, outW, outH, true);
                    break;

                case "use":
                    string href = node.GetAttr("href") ?? node.GetAttr("xlink:href");
                    if (!string.IsNullOrEmpty(href))
                    {
                        if (href.StartsWith("#")) href = href.Substring(1);
                        if (defs.TryGetValue(href, out DomNode target))
                        {
                            RenderElement(target, state, pixels, outW, outH, defs);
                        }
                    }
                    break;
            }
        }

        private static void ApplyElementStyle(DomNode node, ref SvgState state)
        {
            string fill = node.GetAttr("fill");
            if (!string.IsNullOrEmpty(fill))
            {
                if (fill.Equals("none", StringComparison.OrdinalIgnoreCase))
                    state.FillColor = 0;
                else if (fill.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
                    state.FillColor = state.CurrentColor;
                else if (TryParseColor(fill, state.CurrentColor, out uint fc))
                    state.FillColor = fc;
            }

            string stroke = node.GetAttr("stroke");
            if (!string.IsNullOrEmpty(stroke))
            {
                if (stroke.Equals("none", StringComparison.OrdinalIgnoreCase))
                    state.StrokeColor = 0;
                else if (stroke.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
                    state.StrokeColor = state.CurrentColor;
                else if (TryParseColor(stroke, state.CurrentColor, out uint sc))
                    state.StrokeColor = sc;
            }

            string sw = node.GetAttr("stroke-width");
            if (!string.IsNullOrEmpty(sw) && float.TryParse(sw, NumberStyles.Float, CultureInfo.InvariantCulture, out float swVal) && swVal >= 0)
            {
                state.StrokeWidth = swVal;
            }

            string op = node.GetAttr("opacity");
            if (!string.IsNullOrEmpty(op) && float.TryParse(op, NumberStyles.Float, CultureInfo.InvariantCulture, out float opVal))
            {
                state.Opacity *= Math.Clamp(opVal, 0.0f, 1.0f);
            }

            string colorAttr = node.GetAttr("color");
            if (!string.IsNullOrEmpty(colorAttr) && TryParseColor(colorAttr, state.CurrentColor, out uint parsedColor))
            {
                state.CurrentColor = parsedColor;
            }

            // Also parse inline CSS style attribute (e.g. style="fill: red; stroke-width: 2")
            string style = node.GetAttr("style");
            if (!string.IsNullOrEmpty(style))
            {
                string[] decls = style.Split(';');
                for (int i = 0; i < decls.Length; i++)
                {
                    string d = decls[i].Trim();
                    int colon = d.IndexOf(':');
                    if (colon <= 0) continue;
                    string prop = d.Substring(0, colon).Trim().ToLowerInvariant();
                    string val = d.Substring(colon + 1).Trim();

                    if (prop == "fill")
                    {
                        if (val.Equals("none", StringComparison.OrdinalIgnoreCase)) state.FillColor = 0;
                        else if (val.Equals("currentColor", StringComparison.OrdinalIgnoreCase)) state.FillColor = state.CurrentColor;
                        else if (TryParseColor(val, state.CurrentColor, out uint fc)) state.FillColor = fc;
                    }
                    else if (prop == "stroke")
                    {
                        if (val.Equals("none", StringComparison.OrdinalIgnoreCase)) state.StrokeColor = 0;
                        else if (val.Equals("currentColor", StringComparison.OrdinalIgnoreCase)) state.StrokeColor = state.CurrentColor;
                        else if (TryParseColor(val, state.CurrentColor, out uint sc)) state.StrokeColor = sc;
                    }
                    else if (prop == "stroke-width" && float.TryParse(val.Replace("px", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float swVal2))
                    {
                        state.StrokeWidth = swVal2;
                    }
                    else if (prop == "color" && TryParseColor(val, state.CurrentColor, out uint inlineColor))
                    {
                        state.CurrentColor = inlineColor;
                    }
                }
            }

            // Transform attribute (translate, scale)
            string transform = node.GetAttr("transform");
            if (!string.IsNullOrEmpty(transform))
            {
                ApplyTransform(transform, ref state);
            }
        }

        private static void ApplyTransform(string transform, ref SvgState state)
        {
            // Support translate(x[, y])
            int trIdx = transform.IndexOf("translate(", StringComparison.OrdinalIgnoreCase);
            if (trIdx >= 0)
            {
                int end = transform.IndexOf(')', trIdx);
                if (end > trIdx)
                {
                    string inner = transform.Substring(trIdx + 10, end - trIdx - 10);
                    string[] parts = inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float tx))
                    {
                        float ty = (parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float pty)) ? pty : 0;
                        state.OffX += tx * state.Scale;
                        state.OffY += ty * state.Scale;
                    }
                }
            }
        }

        // =========================================================================
        // PATH & GEOMETRY RENDERERS
        // =========================================================================

        private static void RenderPath(DomNode node, SvgState state, int[] pixels, int outW, int outH)
        {
            string d = node.GetAttr("d");
            if (string.IsNullOrEmpty(d)) return;

            List<List<PointF>> contours = ParsePath(d, state);
            if (contours.Count == 0) return;

            uint fill = state.EffectiveFill();
            uint stroke = state.EffectiveStroke();
            float strokeW = state.StrokeWidth * state.Scale;

            if (fill != 0)
            {
                FillContours(contours, fill, pixels, outW, outH);
            }

            if (stroke != 0 && strokeW > 0.1f)
            {
                StrokeContours(contours, stroke, strokeW, pixels, outW, outH);
            }
        }

        private static void RenderRect(DomNode node, SvgState state, int[] pixels, int outW, int outH)
        {
            float x = ParseFloat(node.GetAttr("x"), 0);
            float y = ParseFloat(node.GetAttr("y"), 0);
            float w = ParseFloat(node.GetAttr("width"), 0);
            float h = ParseFloat(node.GetAttr("height"), 0);
            float rx = ParseFloat(node.GetAttr("rx"), 0);
            float ry = ParseFloat(node.GetAttr("ry"), 0);
            if (w <= 0 || h <= 0) return;

            if (rx > 0 && ry <= 0) ry = rx;
            if (ry > 0 && rx <= 0) rx = ry;

            List<PointF> contour = new List<PointF>();
            if (rx > 0 && ry > 0)
            {
                // Rounded rect
                rx = Math.Min(rx, w / 2.0f);
                ry = Math.Min(ry, h / 2.0f);
                contour.Add(state.Transform(x + rx, y));
                contour.Add(state.Transform(x + w - rx, y));
                AddArcPoints(contour, state, x + w - rx, y + ry, rx, ry, -(float)Math.PI / 2, 0);
                contour.Add(state.Transform(x + w, y + h - ry));
                AddArcPoints(contour, state, x + w - rx, y + h - ry, rx, ry, 0, (float)Math.PI / 2);
                contour.Add(state.Transform(x + rx, y + h));
                AddArcPoints(contour, state, x + rx, y + h - ry, rx, ry, (float)Math.PI / 2, (float)Math.PI);
                contour.Add(state.Transform(x, y + ry));
                AddArcPoints(contour, state, x + rx, y + ry, rx, ry, (float)Math.PI, 3 * (float)Math.PI / 2);
            }
            else
            {
                contour.Add(state.Transform(x, y));
                contour.Add(state.Transform(x + w, y));
                contour.Add(state.Transform(x + w, y + h));
                contour.Add(state.Transform(x, y + h));
            }
            contour.Add(contour[0]);

            List<List<PointF>> contours = new List<List<PointF>> { contour };
            uint fill = state.EffectiveFill();
            uint stroke = state.EffectiveStroke();
            float strokeW = state.StrokeWidth * state.Scale;

            if (fill != 0) FillContours(contours, fill, pixels, outW, outH);
            if (stroke != 0 && strokeW > 0.1f) StrokeContours(contours, stroke, strokeW, pixels, outW, outH);
        }

        private static void RenderCircle(DomNode node, SvgState state, int[] pixels, int outW, int outH)
        {
            float cx = ParseFloat(node.GetAttr("cx"), 0);
            float cy = ParseFloat(node.GetAttr("cy"), 0);
            float r = ParseFloat(node.GetAttr("r"), 0);
            if (r <= 0) return;

            List<PointF> contour = new List<PointF>();
            int steps = Math.Clamp((int)(r * state.Scale * 1.5f), 16, 64);
            for (int i = 0; i <= steps; i++)
            {
                float angle = i * ((float)Math.PI * 2.0f / steps);
                float px = cx + r * (float)Math.Cos(angle);
                float py = cy + r * (float)Math.Sin(angle);
                contour.Add(state.Transform(px, py));
            }

            List<List<PointF>> contours = new List<List<PointF>> { contour };
            uint fill = state.EffectiveFill();
            uint stroke = state.EffectiveStroke();
            float strokeW = state.StrokeWidth * state.Scale;

            if (fill != 0) FillContours(contours, fill, pixels, outW, outH);
            if (stroke != 0 && strokeW > 0.1f) StrokeContours(contours, stroke, strokeW, pixels, outW, outH);
        }

        private static void RenderEllipse(DomNode node, SvgState state, int[] pixels, int outW, int outH)
        {
            float cx = ParseFloat(node.GetAttr("cx"), 0);
            float cy = ParseFloat(node.GetAttr("cy"), 0);
            float rx = ParseFloat(node.GetAttr("rx"), 0);
            float ry = ParseFloat(node.GetAttr("ry"), 0);
            if (rx <= 0 || ry <= 0) return;

            List<PointF> contour = new List<PointF>();
            int steps = Math.Clamp((int)(Math.Max(rx, ry) * state.Scale * 1.5f), 16, 64);
            for (int i = 0; i <= steps; i++)
            {
                float angle = i * ((float)Math.PI * 2.0f / steps);
                float px = cx + rx * (float)Math.Cos(angle);
                float py = cy + ry * (float)Math.Sin(angle);
                contour.Add(state.Transform(px, py));
            }

            List<List<PointF>> contours = new List<List<PointF>> { contour };
            uint fill = state.EffectiveFill();
            uint stroke = state.EffectiveStroke();
            float strokeW = state.StrokeWidth * state.Scale;

            if (fill != 0) FillContours(contours, fill, pixels, outW, outH);
            if (stroke != 0 && strokeW > 0.1f) StrokeContours(contours, stroke, strokeW, pixels, outW, outH);
        }

        private static void RenderLine(DomNode node, SvgState state, int[] pixels, int outW, int outH)
        {
            float x1 = ParseFloat(node.GetAttr("x1"), 0);
            float y1 = ParseFloat(node.GetAttr("y1"), 0);
            float x2 = ParseFloat(node.GetAttr("x2"), 0);
            float y2 = ParseFloat(node.GetAttr("y2"), 0);

            PointF p1 = state.Transform(x1, y1);
            PointF p2 = state.Transform(x2, y2);

            uint stroke = state.EffectiveStroke();
            float strokeW = state.StrokeWidth * state.Scale;
            if (stroke != 0 && strokeW > 0.1f)
            {
                DrawThickLine(pixels, outW, outH, p1, p2, strokeW, stroke);
            }
        }

        private static void RenderPoly(DomNode node, SvgState state, int[] pixels, int outW, int outH, bool close)
        {
            string points = node.GetAttr("points");
            if (string.IsNullOrEmpty(points)) return;

            List<PointF> contour = new List<PointF>();
            SvgTokenizer tok = new SvgTokenizer(points);
            while (tok.TryReadFloat(out float x) && tok.TryReadFloat(out float y))
            {
                contour.Add(state.Transform(x, y));
            }
            if (contour.Count < 2) return;
            if (close && (contour[0].X != contour[contour.Count - 1].X || contour[0].Y != contour[contour.Count - 1].Y))
            {
                contour.Add(contour[0]);
            }

            List<List<PointF>> contours = new List<List<PointF>> { contour };
            uint fill = state.EffectiveFill();
            uint stroke = state.EffectiveStroke();
            float strokeW = state.StrokeWidth * state.Scale;

            if (close && fill != 0) FillContours(contours, fill, pixels, outW, outH);
            if (stroke != 0 && strokeW > 0.1f) StrokeContours(contours, stroke, strokeW, pixels, outW, outH);
        }

        private static void AddArcPoints(List<PointF> contour, SvgState state, float cx, float cy, float rx, float ry, float startAngle, float endAngle)
        {
            int steps = 8;
            for (int i = 1; i <= steps; i++)
            {
                float a = startAngle + (endAngle - startAngle) * (i / (float)steps);
                float px = cx + rx * (float)Math.Cos(a);
                float py = cy + ry * (float)Math.Sin(a);
                contour.Add(state.Transform(px, py));
            }
        }

        // =========================================================================
        // SVG PATH PARSER (M, L, H, V, C, S, Q, T, A, Z)
        // =========================================================================

        private static List<List<PointF>> ParsePath(string d, SvgState state)
        {
            List<List<PointF>> contours = new List<List<PointF>>();
            List<PointF> currentContour = new List<PointF>();

            SvgTokenizer tok = new SvgTokenizer(d);
            char curCmd = '\0';
            float curX = 0, curY = 0;
            float startX = 0, startY = 0;
            float lastCtrlX = 0, lastCtrlY = 0;

            while (tok.HasMore)
            {
                if (tok.TryReadCommand(out char newCmd))
                {
                    curCmd = newCmd;
                }
                else if (curCmd == '\0')
                {
                    break;
                }

                bool rel = char.IsLower(curCmd);
                char cmd = char.ToUpperInvariant(curCmd);

                switch (cmd)
                {
                    case 'M':
                        if (tok.TryReadFloat(out float mx) && tok.TryReadFloat(out float my))
                        {
                            if (rel) { curX += mx; curY += my; }
                            else { curX = mx; curY = my; }
                            startX = curX; startY = curY;
                            lastCtrlX = curX; lastCtrlY = curY;

                            if (currentContour.Count > 0)
                            {
                                contours.Add(currentContour);
                                currentContour = new List<PointF>();
                            }
                            currentContour.Add(state.Transform(curX, curY));

                            // Subsequent coordinate pairs after M/m are treated as implicit L/l
                            curCmd = rel ? 'l' : 'L';
                        }
                        break;

                    case 'L':
                        while (tok.TryReadFloat(out float lx) && tok.TryReadFloat(out float ly))
                        {
                            if (rel) { curX += lx; curY += ly; }
                            else { curX = lx; curY = ly; }
                            lastCtrlX = curX; lastCtrlY = curY;
                            currentContour.Add(state.Transform(curX, curY));
                        }
                        break;

                    case 'H':
                        while (tok.TryReadFloat(out float hx))
                        {
                            if (rel) curX += hx;
                            else curX = hx;
                            lastCtrlX = curX; lastCtrlY = curY;
                            currentContour.Add(state.Transform(curX, curY));
                        }
                        break;

                    case 'V':
                        while (tok.TryReadFloat(out float vy))
                        {
                            if (rel) curY += vy;
                            else curY = vy;
                            lastCtrlX = curX; lastCtrlY = curY;
                            currentContour.Add(state.Transform(curX, curY));
                        }
                        break;

                    case 'C':
                        while (tok.TryReadFloat(out float x1) && tok.TryReadFloat(out float y1) &&
                               tok.TryReadFloat(out float x2) && tok.TryReadFloat(out float y2) &&
                               tok.TryReadFloat(out float x) && tok.TryReadFloat(out float y))
                        {
                            float cx1 = rel ? curX + x1 : x1;
                            float cy1 = rel ? curY + y1 : y1;
                            float cx2 = rel ? curX + x2 : x2;
                            float cy2 = rel ? curY + y2 : y2;
                            float endX = rel ? curX + x : x;
                            float endY = rel ? curY + y : y;

                            SubdivideCubic(currentContour, state, curX, curY, cx1, cy1, cx2, cy2, endX, endY);
                            curX = endX; curY = endY;
                            lastCtrlX = cx2; lastCtrlY = cy2;
                        }
                        break;

                    case 'S':
                        while (tok.TryReadFloat(out float x2) && tok.TryReadFloat(out float y2) &&
                               tok.TryReadFloat(out float x) && tok.TryReadFloat(out float y))
                        {
                            float cx1 = 2 * curX - lastCtrlX;
                            float cy1 = 2 * curY - lastCtrlY;
                            float cx2 = rel ? curX + x2 : x2;
                            float cy2 = rel ? curY + y2 : y2;
                            float endX = rel ? curX + x : x;
                            float endY = rel ? curY + y : y;

                            SubdivideCubic(currentContour, state, curX, curY, cx1, cy1, cx2, cy2, endX, endY);
                            curX = endX; curY = endY;
                            lastCtrlX = cx2; lastCtrlY = cy2;
                        }
                        break;

                    case 'Q':
                        while (tok.TryReadFloat(out float x1) && tok.TryReadFloat(out float y1) &&
                               tok.TryReadFloat(out float x) && tok.TryReadFloat(out float y))
                        {
                            float cx1 = rel ? curX + x1 : x1;
                            float cy1 = rel ? curY + y1 : y1;
                            float endX = rel ? curX + x : x;
                            float endY = rel ? curY + y : y;

                            SubdivideQuad(currentContour, state, curX, curY, cx1, cy1, endX, endY);
                            curX = endX; curY = endY;
                            lastCtrlX = cx1; lastCtrlY = cy1;
                        }
                        break;

                    case 'T':
                        while (tok.TryReadFloat(out float x) && tok.TryReadFloat(out float y))
                        {
                            float cx1 = 2 * curX - lastCtrlX;
                            float cy1 = 2 * curY - lastCtrlY;
                            float endX = rel ? curX + x : x;
                            float endY = rel ? curY + y : y;

                            SubdivideQuad(currentContour, state, curX, curY, cx1, cy1, endX, endY);
                            curX = endX; curY = endY;
                            lastCtrlX = cx1; lastCtrlY = cy1;
                        }
                        break;

                    case 'A':
                        while (tok.TryReadFloat(out float rx) && tok.TryReadFloat(out float ry) &&
                               tok.TryReadFloat(out float rot) &&
                               tok.TryReadFloat(out float largeArc) &&
                               tok.TryReadFloat(out float sweep) &&
                               tok.TryReadFloat(out float x) && tok.TryReadFloat(out float y))
                        {
                            float endX = rel ? curX + x : x;
                            float endY = rel ? curY + y : y;
                            SubdivideArc(currentContour, state, curX, curY, rx, ry, rot, largeArc != 0, sweep != 0, endX, endY);
                            curX = endX; curY = endY;
                            lastCtrlX = curX; lastCtrlY = curY;
                        }
                        break;

                    case 'Z':
                        if (currentContour.Count > 0)
                        {
                            currentContour.Add(state.Transform(startX, startY));
                        }
                        curX = startX; curY = startY;
                        lastCtrlX = curX; lastCtrlY = curY;
                        break;

                    default:
                        // Unsupported command; ignore and consume token
                        break;
                }
            }

            if (currentContour.Count > 0)
            {
                contours.Add(currentContour);
            }

            return contours;
        }

        private static void SubdivideCubic(List<PointF> contour, SvgState state, float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3)
        {
            const int steps = 12;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                float u = 1.0f - t;
                float px = u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3;
                float py = u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3;
                contour.Add(state.Transform(px, py));
            }
        }

        private static void SubdivideQuad(List<PointF> contour, SvgState state, float x0, float y0, float x1, float y1, float x2, float y2)
        {
            const int steps = 8;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                float u = 1.0f - t;
                float px = u * u * x0 + 2 * u * t * x1 + t * t * x2;
                float py = u * u * y0 + 2 * u * t * y1 + t * t * y2;
                contour.Add(state.Transform(px, py));
            }
        }

        private static void SubdivideArc(List<PointF> contour, SvgState state, float x1, float y1, float rx, float ry, float angleDeg, bool largeArc, bool sweep, float x2, float y2)
        {
            if (rx <= 0.0001f || ry <= 0.0001f)
            {
                contour.Add(state.Transform(x2, y2));
                return;
            }

            rx = Math.Abs(rx);
            ry = Math.Abs(ry);
            float phi = angleDeg * ((float)Math.PI / 180.0f);
            float cosPhi = (float)Math.Cos(phi);
            float sinPhi = (float)Math.Sin(phi);

            float dx = (x1 - x2) / 2.0f;
            float dy = (y1 - y2) / 2.0f;
            float x1p = cosPhi * dx + sinPhi * dy;
            float y1p = -sinPhi * dx + cosPhi * dy;

            float lambda = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
            if (lambda > 1.0f)
            {
                float s = (float)Math.Sqrt(lambda);
                rx *= s;
                ry *= s;
            }

            float rxSq = rx * rx;
            float rySq = ry * ry;
            float x1pSq = x1p * x1p;
            float y1pSq = y1p * y1p;

            float numerator = rxSq * rySq - rxSq * y1pSq - rySq * x1pSq;
            float denom = rxSq * y1pSq + rySq * x1pSq;
            float factor = 0;
            if (denom > 0.0001f)
            {
                float radicand = Math.Max(0.0f, numerator / denom);
                factor = (float)Math.Sqrt(radicand) * (largeArc == sweep ? -1.0f : 1.0f);
            }

            float cxp = factor * (rx * y1p / ry);
            float cyp = factor * (-ry * x1p / rx);

            float cx = cosPhi * cxp - sinPhi * cyp + (x1 + x2) / 2.0f;
            float cy = sinPhi * cxp + cosPhi * cyp + (y1 + y2) / 2.0f;

            float vx1 = (x1p - cxp) / rx;
            float vy1 = (y1p - cyp) / ry;
            float vx2 = (-x1p - cxp) / rx;
            float vy2 = (-y1p - cyp) / ry;

            float theta1 = (float)Math.Atan2(vy1, vx1);
            float dTheta = (float)Math.Atan2(vy2, vx2) - theta1;

            if (!sweep && dTheta > 0) dTheta -= 2.0f * (float)Math.PI;
            else if (sweep && dTheta < 0) dTheta += 2.0f * (float)Math.PI;

            int steps = Math.Clamp((int)(Math.Abs(dTheta) / ((float)Math.PI / 6.0f)) + 2, 4, 32);
            for (int i = 1; i <= steps; i++)
            {
                float t = theta1 + dTheta * (i / (float)steps);
                float pxp = rx * (float)Math.Cos(t);
                float pyp = ry * (float)Math.Sin(t);
                float px = cosPhi * pxp - sinPhi * pyp + cx;
                float py = sinPhi * pxp + cosPhi * pyp + cy;
                contour.Add(state.Transform(px, py));
            }
        }

        // =========================================================================
        // SOFTWARE RASTERIZATION: SCANLINE FILL & THICK STROKE
        // =========================================================================

        private struct Edge
        {
            public float X0, Y0, X1, Y1;
            public float MinY, MaxY;
        }

        private static void FillContours(List<List<PointF>> contours, uint fillColor, int[] pixels, int outW, int outH)
        {
            List<Edge> edges = new List<Edge>();
            for (int ci = 0; ci < contours.Count; ci++)
            {
                List<PointF> pts = contours[ci];
                if (pts.Count < 3) continue;

                for (int i = 0; i < pts.Count - 1; i++)
                {
                    PointF p0 = pts[i];
                    PointF p1 = pts[i + 1];
                    if (Math.Abs(p0.Y - p1.Y) < 0.0001f) continue;

                    Edge e = new Edge
                    {
                        X0 = p0.X, Y0 = p0.Y,
                        X1 = p1.X, Y1 = p1.Y,
                        MinY = Math.Min(p0.Y, p1.Y),
                        MaxY = Math.Max(p0.Y, p1.Y)
                    };
                    edges.Add(e);
                }
            }

            if (edges.Count == 0) return;

            List<float> intersections = new List<float>();

            for (int y = 0; y < outH; y++)
            {
                float scanY = y + 0.5f;
                intersections.Clear();

                for (int ei = 0; ei < edges.Count; ei++)
                {
                    Edge e = edges[ei];
                    if (scanY >= e.MinY && scanY < e.MaxY)
                    {
                        float t = (scanY - e.Y0) / (e.Y1 - e.Y0);
                        float x = e.X0 + t * (e.X1 - e.X0);
                        intersections.Add(x);
                    }
                }

                if (intersections.Count < 2) continue;
                intersections.Sort();

                for (int k = 0; k < intersections.Count - 1; k += 2)
                {
                    float xStart = intersections[k];
                    float xEnd = intersections[k + 1];
                    if (xEnd <= 0 || xStart >= outW) continue;

                    xStart = Math.Max(0, xStart);
                    xEnd = Math.Min(outW, xEnd);

                    int iStart = (int)xStart;
                    int iEnd = (int)xEnd;

                    if (iStart == iEnd)
                    {
                        float cov = xEnd - xStart;
                        BlendPixel(pixels, iStart, y, outW, fillColor, cov);
                    }
                    else
                    {
                        // Left edge anti-aliasing
                        float leftCov = 1.0f - (xStart - iStart);
                        BlendPixel(pixels, iStart, y, outW, fillColor, leftCov);

                        // Full pixels inside span
                        for (int px = iStart + 1; px < iEnd; px++)
                        {
                            BlendPixel(pixels, px, y, outW, fillColor, 1.0f);
                        }

                        // Right edge anti-aliasing
                        if (iEnd < outW)
                        {
                            float rightCov = xEnd - iEnd;
                            if (rightCov > 0.001f)
                                BlendPixel(pixels, iEnd, y, outW, fillColor, rightCov);
                        }
                    }
                }
            }
        }

        private static void StrokeContours(List<List<PointF>> contours, uint strokeColor, float strokeW, int[] pixels, int outW, int outH)
        {
            for (int ci = 0; ci < contours.Count; ci++)
            {
                List<PointF> pts = contours[ci];
                if (pts.Count < 2) continue;

                for (int i = 0; i < pts.Count - 1; i++)
                {
                    DrawThickLine(pixels, outW, outH, pts[i], pts[i + 1], strokeW, strokeColor);
                }
            }
        }

        private static void DrawThickLine(int[] pixels, int outW, int outH, PointF a, PointF b, float strokeW, uint color)
        {
            float radius = strokeW / 2.0f;
            float rSq = radius * radius;
            float rPlusHalf = radius + 0.5f;
            float rPlusHalfSq = rPlusHalf * rPlusHalf;

            float minX = Math.Min(a.X, b.X) - rPlusHalf;
            float maxX = Math.Max(a.X, b.X) + rPlusHalf;
            float minY = Math.Min(a.Y, b.Y) - rPlusHalf;
            float maxY = Math.Max(a.Y, b.Y) + rPlusHalf;

            int iMinX = Math.Clamp((int)Math.Floor(minX), 0, outW - 1);
            int iMaxX = Math.Clamp((int)Math.Ceiling(maxX), 0, outW - 1);
            int iMinY = Math.Clamp((int)Math.Floor(minY), 0, outH - 1);
            int iMaxY = Math.Clamp((int)Math.Ceiling(maxY), 0, outH - 1);

            float segDx = b.X - a.X;
            float segDy = b.Y - a.Y;
            float lenSq = segDx * segDx + segDy * segDy;

            for (int py = iMinY; py <= iMaxY; py++)
            {
                float cy = py + 0.5f;
                for (int px = iMinX; px <= iMaxX; px++)
                {
                    float cx = px + 0.5f;
                    float distSq;

                    if (lenSq < 0.0001f)
                    {
                        float ddx = cx - a.X;
                        float ddy = cy - a.Y;
                        distSq = ddx * ddx + ddy * ddy;
                    }
                    else
                    {
                        float t = Math.Clamp(((cx - a.X) * segDx + (cy - a.Y) * segDy) / lenSq, 0.0f, 1.0f);
                        float projX = a.X + t * segDx;
                        float projY = a.Y + t * segDy;
                        float ddx = cx - projX;
                        float ddy = cy - projY;
                        distSq = ddx * ddx + ddy * ddy;
                    }

                    if (distSq <= rPlusHalfSq)
                    {
                        float dist = (float)Math.Sqrt(distSq);
                        float cov = Math.Clamp(rPlusHalf - dist, 0.0f, 1.0f);
                        BlendPixel(pixels, px, py, outW, color, cov);
                    }
                }
            }
        }

        private static void BlendPixel(int[] pixels, int x, int y, int w, uint srcColor, float coverage)
        {
            if (coverage <= 0.001f || x < 0 || x >= w) return;
            int idx = y * w + x;
            uint dst = (uint)pixels[idx];

            uint sa = (srcColor >> 24) & 0xFF;
            if (coverage < 0.999f)
                sa = (uint)(sa * coverage);
            if (sa == 0) return;

            if (sa >= 255 && dst == 0)
            {
                pixels[idx] = (int)srcColor;
                return;
            }

            uint sr = (srcColor >> 16) & 0xFF;
            uint sg = (srcColor >> 8) & 0xFF;
            uint sb = srcColor & 0xFF;

            uint da = (dst >> 24) & 0xFF;
            uint dr = (dst >> 16) & 0xFF;
            uint dg = (dst >> 8) & 0xFF;
            uint db = dst & 0xFF;

            // Porter-Duff Over
            uint outA = sa + da * (255 - sa) / 255;
            if (outA == 0) return;

            uint outR = (sr * sa + dr * da * (255 - sa) / 255) / outA;
            uint outG = (sg * sa + dg * da * (255 - sa) / 255) / outA;
            uint outB = (sb * sa + db * da * (255 - sa) / 255) / outA;

            pixels[idx] = (int)((outA << 24) | (outR << 16) | (outG << 8) | outB);
        }

        // =========================================================================
        // PARSING HELPERS & TOKENIZERS
        // =========================================================================

        public static void ParseViewBox(string vb, out float minX, out float minY, out float width, out float height)
        {
            minX = 0; minY = 0; width = 0; height = 0;
            if (string.IsNullOrEmpty(vb)) return;

            SvgTokenizer tok = new SvgTokenizer(vb);
            if (tok.TryReadFloat(out minX) && tok.TryReadFloat(out minY) &&
                tok.TryReadFloat(out width) && tok.TryReadFloat(out height))
            {
                return;
            }
        }

        private static int ParseDimension(string s, int defaultVal)
        {
            if (string.IsNullOrEmpty(s)) return defaultVal;
            s = s.Trim().ToLowerInvariant().Replace("px", "");
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float val) && val > 0)
                return (int)Math.Round(val);
            return defaultVal;
        }

        private static float ParseFloat(string s, float defaultVal)
        {
            if (string.IsNullOrEmpty(s)) return defaultVal;
            s = s.Trim().ToLowerInvariant().Replace("px", "");
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                return val;
            return defaultVal;
        }

        public static bool TryParseColor(string s, uint currentColor, out uint color)
        {
            color = 0;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s.Equals("none", StringComparison.OrdinalIgnoreCase) || s.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            {
                color = 0x00000000;
                return true;
            }
            if (s.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
            {
                color = currentColor;
                return true;
            }

            // Hex format #rgb, #rgba, #rrggbb, #rrggbbaa
            if (s.StartsWith("#"))
            {
                string hex = s.Substring(1);
                if (hex.Length == 3)
                {
                    uint r = Convert.ToByte(new string(hex[0], 2), 16);
                    uint g = Convert.ToByte(new string(hex[1], 2), 16);
                    uint b = Convert.ToByte(new string(hex[2], 2), 16);
                    color = 0xFF000000 | (r << 16) | (g << 8) | b;
                    return true;
                }
                if (hex.Length == 4)
                {
                    uint r = Convert.ToByte(new string(hex[0], 2), 16);
                    uint g = Convert.ToByte(new string(hex[1], 2), 16);
                    uint b = Convert.ToByte(new string(hex[2], 2), 16);
                    uint a = Convert.ToByte(new string(hex[3], 2), 16);
                    color = (a << 24) | (r << 16) | (g << 8) | b;
                    return true;
                }
                if (hex.Length == 6)
                {
                    uint rgb = Convert.ToUInt32(hex, 16);
                    color = 0xFF000000 | rgb;
                    return true;
                }
                if (hex.Length == 8)
                {
                    uint rgba = Convert.ToUInt32(hex, 16);
                    uint r = (rgba >> 24) & 0xFF;
                    uint g = (rgba >> 16) & 0xFF;
                    uint b = (rgba >> 8) & 0xFF;
                    uint a = rgba & 0xFF;
                    color = (a << 24) | (r << 16) | (g << 8) | b;
                    return true;
                }
            }

            // rgb(r, g, b) or rgba(r, g, b, a)
            if (s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                int open = s.IndexOf('(');
                int close = s.IndexOf(')', open);
                if (open > 0 && close > open)
                {
                    string args = s.Substring(open + 1, close - open - 1);
                    string[] parts = args.Split(new[] { ',', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3 &&
                        byte.TryParse(parts[0].Trim(), out byte r) &&
                        byte.TryParse(parts[1].Trim(), out byte g) &&
                        byte.TryParse(parts[2].Trim(), out byte b))
                    {
                        byte a = 255;
                        if (parts.Length >= 4 && float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float af))
                        {
                            a = (byte)(Math.Clamp(af, 0.0f, 1.0f) * 255);
                        }
                        color = (uint)((a << 24) | (r << 16) | (g << 8) | b);
                        return true;
                    }
                }
            }

            // Named colors
            string lower = s.ToLowerInvariant();
            switch (lower)
            {
                case "white": color = 0xFFFFFFFF; return true;
                case "black": color = 0xFF000000; return true;
                case "red": color = 0xFFFF0000; return true;
                case "green": color = 0xFF008000; return true;
                case "blue": color = 0xFF0000FF; return true;
                case "yellow": color = 0xFFFFFF00; return true;
                case "gray": case "grey": color = 0xFF808080; return true;
                case "darkgray": case "darkgrey": color = 0xFFA9A9A9; return true;
                case "lightgray": case "lightgrey": color = 0xFFD3D3D3; return true;
                case "orange": color = 0xFFFFA500; return true;
                case "purple": color = 0xFF800080; return true;
                case "cyan": case "aqua": color = 0xFF00FFFF; return true;
                case "magenta": case "fuchsia": color = 0xFFFF00FF; return true;
            }

            return false;
        }

        private struct PointF
        {
            public float X;
            public float Y;
            public PointF(float x, float y) { X = x; Y = y; }
        }

        private struct SvgState
        {
            public uint? FillColor;
            public uint? StrokeColor;
            public float StrokeWidth;
            public float Opacity;
            public float Scale;
            public float OffX, OffY;
            public uint CurrentColor;

            public PointF Transform(float x, float y)
            {
                return new PointF(x * Scale + OffX, y * Scale + OffY);
            }

            public uint EffectiveFill()
            {
                if (!FillColor.HasValue || FillColor.Value == 0) return 0;
                if (Opacity >= 0.999f) return FillColor.Value;
                uint a = (FillColor.Value >> 24) & 0xFF;
                a = (uint)(a * Opacity);
                return (a << 24) | (FillColor.Value & 0x00FFFFFF);
            }

            public uint EffectiveStroke()
            {
                if (!StrokeColor.HasValue || StrokeColor.Value == 0) return 0;
                if (Opacity >= 0.999f) return StrokeColor.Value;
                uint a = (StrokeColor.Value >> 24) & 0xFF;
                a = (uint)(a * Opacity);
                return (a << 24) | (StrokeColor.Value & 0x00FFFFFF);
            }
        }

        private ref struct SvgTokenizer
        {
            private readonly string _s;
            private int _pos;

            public SvgTokenizer(string s)
            {
                _s = s ?? string.Empty;
                _pos = 0;
            }

            public bool HasMore
            {
                get
                {
                    SkipSeparators();
                    return _pos < _s.Length;
                }
            }

            public void SkipSeparators()
            {
                while (_pos < _s.Length)
                {
                    char c = _s[_pos];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == ',')
                        _pos++;
                    else
                        break;
                }
            }

            public bool TryReadCommand(out char cmd)
            {
                SkipSeparators();
                if (_pos < _s.Length && char.IsLetter(_s[_pos]))
                {
                    cmd = _s[_pos++];
                    return true;
                }
                cmd = '\0';
                return false;
            }

            public bool TryReadFloat(out float val)
            {
                SkipSeparators();
                if (_pos >= _s.Length)
                {
                    val = 0;
                    return false;
                }

                int start = _pos;
                char c = _s[_pos];
                if (c == '+' || c == '-') _pos++;

                bool hasDigits = false;
                while (_pos < _s.Length && char.IsDigit(_s[_pos]))
                {
                    hasDigits = true;
                    _pos++;
                }

                if (_pos < _s.Length && _s[_pos] == '.')
                {
                    _pos++;
                    while (_pos < _s.Length && char.IsDigit(_s[_pos]))
                    {
                        hasDigits = true;
                        _pos++;
                    }
                }

                if (hasDigits && _pos < _s.Length && (_s[_pos] == 'e' || _s[_pos] == 'E'))
                {
                    int ePos = _pos++;
                    if (_pos < _s.Length && (_s[_pos] == '+' || _s[_pos] == '-')) _pos++;
                    bool hasExpDigits = false;
                    while (_pos < _s.Length && char.IsDigit(_s[_pos]))
                    {
                        hasExpDigits = true;
                        _pos++;
                    }
                    if (!hasExpDigits) _pos = ePos;
                }

                if (!hasDigits)
                {
                    _pos = start;
                    val = 0;
                    return false;
                }

                string numStr = _s.Substring(start, _pos - start);
                if (float.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                    return true;

                val = 0;
                return false;
            }
        }
    }
}
