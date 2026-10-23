using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using Cosmos.Kernel.System.Graphics;

// ---------------------------------------------------------------------------
// Walks the styled DOM and produces a positioned, word-wrapped RenderPlan that
// WebView draws directly and hit-tests for links. The CSS engine (CssSheet)
// computes a full style per element (UA defaults -> inheritance -> cascade ->
// inline style). Block boxes (background/border/padding) are emitted as
// RenderBox decorations painted behind the text lines. Font measurement is
// injected so layout reuses the GUI font metrics.
//
// Approximations: floats/absolute positioning are laid out as normal flow;
// tables are rendered as merged single-line rows; inline <img> drops to its own
// line; inline borders are not drawn.
// ---------------------------------------------------------------------------

public delegate int TextMeasure(string text, int fontSize);

public sealed class RenderSpan
{
    public string Text;
    public bool Bold, Italic, Underline, LineThrough, Overline;
    public string Href;
    public Color Color;
    public Color Bg; public bool HasBg;
    public bool Hidden;
    public int Size;
    public int X, Y, YOff;
    public int Width, Height;
    public int PadL, PadR, PadT, PadB;
    public int BorderRadius;
    public DomNode Node;   // element that owns this text (for click targeting)
    public bool IsImage;
    public Image Image;
    public string ImageAlt;
}

public sealed class RenderBox
{
    public int X, Y, W, H;
    public bool HasBg; public Color Bg;
    public Image BgImage;
    public CssRepeat BgRepeat;
    public int BorderRadius;
    public int BorderT, BorderB, BorderL, BorderR;
    public Color BorderCT, BorderCB, BorderCL, BorderCR;
    public CssBorder StyleT, StyleB, StyleL, StyleR;
    public bool Hidden;
    public DomNode Node;   // element this box decorates (for click targeting)
    public string BoxShadow;
    public bool IsSticky;
    public int StickyTop;
    public int ParentBottom;
    public string Transform;
    public bool HasClip;
    public int ClipX, ClipY, ClipW, ClipH;
    public int ZIndex;
}

public sealed class RenderLine
{
    public List<RenderSpan> Spans = new List<RenderSpan>();
    public int Y, Height, TextX;
    public bool IsRule, IsCode, IsQuote, IsImage;
    public int ImageW, ImageH;
    public string ImageAlt;
    public Image Image;
    public string Bullet; public int BulletX;
    public bool Hidden;
    public Color RuleColor;
    public DomNode Node;   // img element for image lines (click targeting)
    public bool HasClip;
    public int ClipX, ClipY, ClipW, ClipH;
    public int ZIndex;
}

/// <summary>Form-control widget kinds (byte constants; no enums so the OS
/// build never needs Enum.ToString).</summary>
public static class WidgetKind
{
    public const byte Text = 0;        // <input type=text|search|email|...>
    public const byte Password = 1;
    public const byte Checkbox = 2;
    public const byte Radio = 3;
    public const byte Button = 4;      // <input type=submit|button|reset>, <button>
    public const byte Textarea = 5;
    public const byte Select = 6;
    public const byte NotAWidget = 255; // hidden input / unknown
}

/// <summary>A laid-out form control. The WebView draws it, hit-tests it for
/// focus/clicks and routes typing at it; live state (value/checked/selected)
/// lives on the DomNode so scripts and layout always agree.</summary>
public sealed class RenderWidget
{
    public DomNode Node;
    public byte Kind;
    public int X, Y, W, H;
    public string Text;       // displayed text: value, button label, select label
    public bool Checked;      // checkbox / radio
    public bool Disabled;
    public int FontSize;
    public int TextX, TextY;  // origin of the text inside the control
    public int TextW;         // measured width of Text (caret math)
    public bool Hidden;
    public bool HasClip;
    public int ClipX, ClipY, ClipW, ClipH;
    public int ZIndex;
}

public sealed class RenderPlan
{
    public List<RenderLine> Lines = new List<RenderLine>();
    public List<RenderBox> Boxes = new List<RenderBox>();
    public List<RenderWidget> Widgets = new List<RenderWidget>();
    public List<LinkRect> Links = new List<LinkRect>();
    public int ContentHeight;
    public Color PageBg = Color.White;
    public Image PageBgImage;

    public sealed class LinkRect
    {
        public string Href;
        public int X, Y, W, H;
    }

    public string HitTestLink(int x, int y)
    {
        for (int i = 0; i < Links.Count; i++)
        {
            LinkRect r = Links[i];
            if (x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H)
                return r.Href;
        }
        return null;
    }

    /// <summary>The form control under a content-space point (topmost wins).</summary>
    public RenderWidget HitTestWidget(int x, int y)
    {
        for (int i = Widgets.Count - 1; i >= 0; i--)
        {
            RenderWidget w = Widgets[i];
            if (w.Hidden) continue;
            if (x >= w.X && x < w.X + w.W && y >= w.Y && y < w.Y + w.H)
                return w;
        }
        return null;
    }

    /// <summary>Deepest DOM node under a content-space point (for click events).
    /// Widgets and text spans take priority (they sit on top of block boxes);
    /// block boxes are emitted children-first so the first hit is the
    /// innermost element.</summary>
    public DomNode HitTestNode(int x, int y)
    {
        // form controls sit on top of everything else
        for (int i = Widgets.Count - 1; i >= 0; i--)
        {
            RenderWidget w = Widgets[i];
            if (w.Hidden) continue;
            if (x >= w.X && x < w.X + w.W && y >= w.Y && y < w.Y + w.H)
                return w.Node;
        }
        // inline text spans first
        for (int i = 0; i < Lines.Count; i++)
        {
            RenderLine line = Lines[i];
            if (line.Hidden) continue;
            if (line.IsImage)
            {
                if (line.Node != null && y >= line.Y && y < line.Y + line.Height &&
                    x >= line.TextX && x < line.TextX + line.ImageW)
                    return line.Node;
                continue;
            }
            for (int j = 0; j < line.Spans.Count; j++)
            {
                RenderSpan s = line.Spans[j];
                if (s.Hidden || s.Node == null) continue;
                int sy = s.Y + s.YOff;
                if (x >= s.X && x < s.X + s.Width && y >= sy && y < sy + s.Height)
                    return s.Node;
            }
        }
        // block boxes (children appear before parents in the list)
        for (int i = 0; i < Boxes.Count; i++)
        {
            RenderBox b = Boxes[i];
            if (b.Hidden || b.Node == null) continue;
            if (x >= b.X && x < b.X + b.W && y >= b.Y && y < b.Y + b.H)
                return b.Node;
        }
        return null;
    }
}

public static class HtmlLayout
{
    private const int LeftMargin = 10;
    private const int ScrollbarReserve = 16;

    // ---- layout context --------------------------------------------------

    private sealed class Ctx
    {
        public TextMeasure Measure;
        public CssSheet Sheet;
        public Dictionary<string, Image> Images;
        public string BaseHref;
        public int Vw, Vh;
        public RenderPlan Plan;
        public bool IsMeasuring;

        // The containing block for absolutely/fixed positioned descendants:
        // the nearest positioned (relative/absolute/fixed) ancestor's padding
        // box, or the initial containing block (viewport) at the top. Pushed
        // and popped around a positioned element's children. CbBValid is false
        // when the bottom is circular (an auto-height positioned ancestor), so
        // bottom-anchored absolute children fall back to top instead of
        // collapsing against a placeholder bound.
        public int CbL, CbT, CbR, CbB;
        public bool CbBValid;
    }

    private sealed class Run
    {
        public string Text;
        public CssComputed Style;
        public string Href;
        public bool Break;      // forced line break (<br>)
        public bool IsImage;
        public string ImageSrc, ImageAlt;
        public int ImageW, ImageH;
        public bool IsWidget;   // form control flowing inline
        public byte WidgetKind;
        public int WidgetW, WidgetH;
        public DomNode Node;    // owning element for click targeting
    }

    private sealed class Tok
    {
        public string Text;
        public CssComputed Style;
        public string Href;
        public bool Space;
        public bool Break;
        public bool IsImage;
        public string ImageSrc, ImageAlt;
        public int ImageW, ImageH;
        public bool IsWidget;
        public byte WidgetKind;
        public int WidgetW, WidgetH;
        public DomNode Node;    // owning element for click targeting
    }

    // A single flex item: either an element node or an anonymous run of inline content.
    private sealed class FlexItem
    {
        public DomNode Node;             // element item (null for anonymous inline)
        public CssComputed Style;        // item computed style
        public List<Run> Runs;           // anonymous inline items only
        public bool IsAnonymous;

        // box-model parts (main = horizontal for row, vertical for column)
        public int MarginMain0, MarginMain1;   // start/end margins along main axis
        public int MarginCross0, MarginCross1;
        public int PadMain, PadCross;          // padding start+end combined
        public int BorderMain, BorderCross;    // border start+end combined

        public int BaseSize;      // flex base size (content-box, main axis)
        public int MainSize;      // resolved main size (content-box)
        public int CrossSize;     // resolved cross size (content-box); 0 = auto/stretch
        public int MinMain, MaxMain;
        public float Grow, Shrink;
        public int Order;
        public CssAlignItems AlignSelf;

        public int MainPos, CrossPos;   // resolved border-box origin in container content coords
    }

    // ---- entry point -----------------------------------------------------

    public static RenderPlan Build(DomNode root, string cssText, int viewWidth, int viewHeight,
        int baseFont, TextMeasure measure, Dictionary<string, Image> images, string baseHref)
    {
        RenderPlan plan = new RenderPlan();
        if (root == null) return plan;

        Ctx ctx = new Ctx
        {
            Measure = measure,
            Sheet = CssSheet.Parse(cssText),
            Images = images != null ? images : new Dictionary<string, Image>(),
            BaseHref = baseHref ?? "",
            Vw = Math.Max(200, viewWidth),
            Vh = Math.Max(200, viewHeight),
            Plan = plan,
        };

        DomNode body = FindBody(root);
        if (body == null) body = root;

        // Root style: seed from a synthetic <html>, then <body>.
        CssComputed htmlStyle = ctx.Sheet.Compute(FindHtml(root) ?? body, null, ctx.Vw, ctx.Vh);
        CssComputed bodyStyle = ctx.Sheet.Compute(body, htmlStyle, ctx.Vw, ctx.Vh);
        if (bodyStyle.HasBg) plan.PageBg = ToColor(bodyStyle.BgColor, 1.0f);
        else if (htmlStyle.HasBg) plan.PageBg = ToColor(htmlStyle.BgColor, 1.0f);
        else if (bodyStyle.HasColor)
        {
            int r = (int)((bodyStyle.Color >> 16) & 0xFF);
            int g = (int)((bodyStyle.Color >> 8) & 0xFF);
            int b = (int)(bodyStyle.Color & 0xFF);
            int lum = (r * 299 + g * 587 + b * 114) / 1000;
            if (lum > 180)
            {
                // Dark mode fallback: light text on unstated page background should not be on white
                plan.PageBg = Color.FromArgb(18, 18, 24);
            }
        }
        if (bodyStyle.FontSize <= 0) bodyStyle.FontSize = baseFont;

        string pageBgImgSrc = !string.IsNullOrEmpty(bodyStyle.BgImage) ? bodyStyle.BgImage : htmlStyle.BgImage;
        if (!string.IsNullOrEmpty(pageBgImgSrc))
        {
            string abs = WebUrl.Resolve(ctx.BaseHref, pageBgImgSrc);
            if (abs != null && ctx.Images.ContainsKey(abs)) ctx.Images.TryGetValue(abs, out plan.PageBgImage);
            else if (ctx.Images.ContainsKey(pageBgImgSrc)) ctx.Images.TryGetValue(pageBgImgSrc, out plan.PageBgImage);
        }

        int contentWidth = Math.Max(60, viewWidth - LeftMargin * 2 - ScrollbarReserve);
        int y = 4;

        // The initial containing block is the viewport (used by position:fixed
        // and by absolute elements with no positioned ancestor).
        ctx.CbL = 0;
        ctx.CbT = 0;
        ctx.CbR = ctx.Vw;
        ctx.CbB = ctx.Vh;
        ctx.CbBValid = true;

        int bodyAvailW = contentWidth - bodyStyle.MarginL - bodyStyle.MarginR;
        int bodyW = bodyAvailW;
        if (bodyStyle.HasW && bodyStyle.W > 0) bodyW = bodyStyle.W;
        if (bodyStyle.MaxW < int.MaxValue && bodyW > bodyStyle.MaxW) bodyW = bodyStyle.MaxW;
        if (bodyStyle.MinW > 0 && bodyW < bodyStyle.MinW) bodyW = bodyStyle.MinW;
        if (bodyW < 40) bodyW = Math.Min(40, contentWidth);

        int bodyX = LeftMargin + bodyStyle.MarginL;
        if (contentWidth > bodyW)
        {
            if (bodyStyle.MarginLAuto && bodyStyle.MarginRAuto)
                bodyX = LeftMargin + (contentWidth - bodyW) / 2;
            else if (bodyStyle.MarginLAuto)
                bodyX = LeftMargin + (contentWidth - bodyW - bodyStyle.MarginR);
        }

        int bodyContentX = bodyX + bodyStyle.PadL + bodyStyle.BorderLW;
        int bodyContentRight = bodyX + bodyW - bodyStyle.PadR - bodyStyle.BorderRW;
        if (bodyContentRight - bodyContentX < 40) bodyContentRight = bodyContentX + 40;

        y += bodyStyle.MarginT + bodyStyle.PadT + bodyStyle.BorderTW;

        LayoutContainer(body, bodyStyle, bodyStyle, bodyContentX, bodyContentRight, ref y, ctx);
        y += bodyStyle.PadB + bodyStyle.BorderBW + bodyStyle.MarginB;
        plan.ContentHeight = y + 8;
        return plan;
    }

    private static DomNode FindHtml(DomNode root)
    {
        DomNode n = root.FirstElementChild();
        while (n != null)
        {
            if (n.Tag == "html") return n;
            n = n.NextElementSibling();
        }
        return null;
    }

    private static DomNode FindBody(DomNode root)
    {
        DomNode n = root.FirstElementChild();
        while (n != null)
        {
            if (n.Tag == "body") return n;
            if (n.Tag == "html")
            {
                DomNode b = n.FirstElementChild();
                while (b != null) { if (b.Tag == "body") return b; b = b.NextElementSibling(); }
            }
            n = n.NextElementSibling();
        }
        // No explicit body: use the document's element children as an anonymous body.
        return root;
    }

    // ---- container: lay out a block container's children ------------------

    private static void LayoutContainer(DomNode container, CssComputed cstyle, CssComputed baseStyle,
        int contentX, int contentRight, ref int y, Ctx ctx)
    {
        List<Run> runs = new List<Run>();
        DomNode child = container.FirstChild;
        while (child != null)
        {
            if (child.NodeType == DomNode.TextNode)
            {
                AddTextRuns(runs, child.Text, baseStyle, null, container);
            }
            else if (child.IsElement)
            {
                CssComputed cs = ctx.Sheet.Compute(child, baseStyle, ctx.Vw, ctx.Vh);
                if (cs.Display == CssDisplay.None) { child = child.Next; continue; }
                string href = child.Tag == "a" ? child.GetAttr("href") : null;

                switch (child.Tag)
                {
                    case "br":
                        FlushRuns(runs, baseStyle, contentX, contentRight, ref y, ctx, null, 0);
                        runs.Add(new Run { Break = true });
                        break;
                    case "input": case "button": case "select": case "textarea":
                        // form controls: inline-block flow, or their own line
                        // when a rule made them block-level
                        if (IsBlockLevel(cs))
                        {
                            FlushRuns(runs, baseStyle, contentX, contentRight, ref y, ctx, null, 0);
                            EmitBlockWidget(child, cs, contentX, ref y, ctx);
                        }
                        else
                        {
                            byte wkind = WidgetKindOf(child);
                            if (wkind != WidgetKind.NotAWidget)
                            {
                                int ww, wh;
                                MeasureWidget(child, wkind, cs, ctx, out ww, out wh);
                                runs.Add(new Run
                                {
                                    IsWidget = true,
                                    WidgetKind = wkind,
                                    WidgetW = ww,
                                    WidgetH = wh,
                                    Style = cs,
                                    Node = child,
                                });
                            }
                        }
                        break;
                    case "img":
                        if (cs.Display != CssDisplay.None)
                        {
                            if (IsBlockLevel(cs))
                            {
                                FlushRuns(runs, baseStyle, contentX, contentRight, ref y, ctx, null, 0);
                                EmitImage(child, cs, contentX, contentRight, ref y, ctx);
                            }
                            else
                            {
                                AddImageRun(child, cs, runs, ctx, href);
                            }
                        }
                        break;
                    case "svg":
                        if (cs.Display != CssDisplay.None)
                        {
                            if (IsBlockLevel(cs))
                            {
                                FlushRuns(runs, baseStyle, contentX, contentRight, ref y, ctx, null, 0);
                                EmitSvg(child, cs, contentX, contentRight, ref y, ctx);
                            }
                            else
                            {
                                AddSvgRun(child, cs, baseStyle, runs, ctx, href);
                            }
                        }
                        break;
                    case "canvas":
                        if (cs.Display != CssDisplay.None)
                        {
                            if (IsBlockLevel(cs))
                            {
                                FlushRuns(runs, baseStyle, contentX, contentRight, ref y, ctx, null, 0);
                                EmitCanvas(child, cs, contentX, contentRight, ref y, ctx);
                            }
                            else
                            {
                                AddCanvasRun(child, cs, runs, ctx, href);
                            }
                        }
                        break;
                    case "video":
                        if (cs.Display != CssDisplay.None)
                        {
                            FlushRuns(runs, baseStyle, contentX, contentRight, ref y, ctx, null, 0);
                            EmitVideo(child, cs, contentX, contentRight, ref y, ctx);
                        }
                        break;
                    case "audio":
                        if (cs.Display != CssDisplay.None)
                        {
                            FlushRuns(runs, baseStyle, contentX, contentRight, ref y, ctx, null, 0);
                            EmitAudio(child, cs, contentX, contentRight, ref y, ctx);
                        }
                        break;
                    case "iframe":
                        if (cs.Display != CssDisplay.None)
                        {
                            FlushRuns(runs, baseStyle, contentX, contentRight, ref y, ctx, null, 0);
                            EmitIframe(child, cs, contentX, contentRight, ref y, ctx);
                        }
                        break;
                    case "hr":
                        FlushRuns(runs, baseStyle, contentX, contentRight, ref y, ctx, null, 0);
                        EmitRule(cs, contentX, contentRight, ref y, ctx);
                        break;
                    default:
                        if (IsBlockLevel(cs))
                        {
                            FlushRuns(runs, baseStyle, contentX, contentRight, ref y, ctx, null, 0);
                            LayoutBlockNode(child, cs, contentX, contentRight, ref y, ctx, 0);
                        }
                        else
                        {
                            CollectInline(child, cs, runs, ctx, href);
                        }
                        break;
                }
            }
            child = child.Next;
        }
        FlushRuns(runs, baseStyle, contentX, contentRight, ref y, ctx, null, 0);
    }

    private static bool IsBlockLevel(CssComputed cs)
    {
        // absolutely/fixed positioned elements are blockified (CSS 2.1 §9.7)
        if (cs.Position == CssPos.Absolute || cs.Position == CssPos.Fixed) return true;
        return cs.Display == CssDisplay.Block || cs.Display == CssDisplay.ListItem ||
               cs.Display == CssDisplay.Flex || cs.Display == CssDisplay.Grid || cs.Display == CssDisplay.Table ||
               cs.Display == CssDisplay.TableRow || cs.Display == CssDisplay.TableCell ||
               cs.Display == CssDisplay.TableCaption;
    }

    // ---- block node: box + children --------------------------------------

    private static void LayoutBlockNode(DomNode node, CssComputed style, int availX, int availRight,
        ref int y, Ctx ctx, int listIndent)
    {
        switch (node.Tag)
        {
            case "ul": case "ol": case "menu":
                LayoutList(node, style, availX, availRight, ref y, ctx, listIndent);
                return;
            case "li":
                LayoutListItem(node, style, availX, availRight, ref y, ctx, listIndent, "\u2022", 1);
                return;
            case "table":
                LayoutTable(node, style, availX, availRight, ref y, ctx);
                return;
            case "tr": case "thead": case "tbody": case "tfoot": case "colgroup": case "col":
            case "td": case "th": case "caption":
                // Structural table parts reached directly: lay out children as a container.
                break;
            case "input": case "button": case "select": case "textarea":
                // Block-level form control (display:block): its own line.
                EmitBlockWidget(node, style, availX, ref y, ctx);
                return;
            case "img":
                EmitImage(node, style, availX, availRight, ref y, ctx);
                return;
            case "svg":
                EmitSvg(node, style, availX, availRight, ref y, ctx);
                return;
            case "canvas":
                EmitCanvas(node, style, availX, availRight, ref y, ctx);
                return;
            case "video":
                EmitVideo(node, style, availX, availRight, ref y, ctx);
                return;
            case "audio":
                EmitAudio(node, style, availX, availRight, ref y, ctx);
                return;
        }

        // --- box model ---
        int marginLeft = style.MarginL, marginRight = style.MarginR;
        int marginTop = style.MarginT, marginBottom = style.MarginB;
        int bl = style.BorderLW, br = style.BorderRW, bt = style.BorderTW, bb = style.BorderBW;
        int pl = style.PadL, pr = style.PadR, pt = style.PadT, pb = style.PadB;

        bool absolute = style.Position == CssPos.Absolute;
        bool fixedPos = style.Position == CssPos.Fixed;
        bool outOfFlow = absolute || fixedPos;
        bool positioned = style.Position != CssPos.Static;

        // containing block this element is placed against (fixed uses the viewport)
        int cbL = ctx.CbL, cbT = ctx.CbT, cbR = ctx.CbR, cbB = ctx.CbB;
        bool cbBValid = ctx.CbBValid;
        if (fixedPos) { cbL = 0; cbT = 0; cbR = ctx.Vw; cbB = ctx.Vh; cbBValid = true; }

        int availW = Math.Max(60, availRight - availX);
        if (style.HasW && !style.HasH && style.AspectRatio > 0)
        {
            style.H = (int)(style.W / style.AspectRatio);
            style.HasH = true;
        }
        else if (style.HasH && !style.HasW && style.AspectRatio > 0)
        {
            style.W = (int)(style.H * style.AspectRatio);
            style.HasW = true;
        }
        int boxW;
        if (style.HasW)
        {
            int baseW = style.W;
            if (style.IsWPct)
            {
                int cbWidth = outOfFlow ? (cbR - cbL) : (availRight - availX - marginLeft - marginRight);
                if (cbWidth > 0)
                    baseW = (int)Math.Round(style.WPct * cbWidth / 100.0);
            }
            boxW = baseW + (style.BoxSizing == CssBox.Border ? 0 : pl + pr + bl + br);
        }
        else if (style.Display == CssDisplay.InlineBlock || style.Float != CssFloat.None)
        {
            int contentNat = MeasureContentWidth(node, style, ctx);
            boxW = contentNat + pl + pr + bl + br;
            if (boxW > availW - marginLeft - marginRight)
                boxW = availW - marginLeft - marginRight;
        }
        else
        {
            boxW = availW - marginLeft - marginRight;
        }

        // absolute/fixed with left+right and auto width: stretch to fit
        if (outOfFlow && !style.HasW
            && style.PosL != CssComputed.Auto && style.PosR != CssComputed.Auto)
        {
            int borders = (style.BoxSizing == CssBox.Border ? 0 : pl + pr + bl + br);
            boxW = (cbR - style.PosR) - (cbL + style.PosL) - borders;
        }

        int curMaxW = style.MaxW;
        if (style.IsMaxWPct)
        {
            int cbWidth = outOfFlow ? (cbR - cbL) : (availRight - availX - marginLeft - marginRight);
            if (cbWidth > 0) curMaxW = (int)Math.Round(style.MaxWPct * cbWidth / 100.0);
        }
        int curMinW = style.MinW;
        if (style.IsMinWPct)
        {
            int cbWidth = outOfFlow ? (cbR - cbL) : (availRight - availX - marginLeft - marginRight);
            if (cbWidth > 0) curMinW = (int)Math.Round(style.MinWPct * cbWidth / 100.0);
        }

        if (curMaxW < int.MaxValue && boxW > curMaxW) boxW = curMaxW;
        if (curMinW > 0 && boxW < curMinW) boxW = curMinW;
        if (outOfFlow) { if (boxW < 1) boxW = 1; }
        else { if (boxW < 40) boxW = Math.Min(40, availW); if (boxW < 20) boxW = 20; }

        int boxX, boxTop;
        if (outOfFlow)
        {
            if (style.PosL != CssComputed.Auto) boxX = cbL + style.PosL;
            else if (style.PosR != CssComputed.Auto) boxX = cbR - style.PosR - boxW;
            else boxX = cbL + marginLeft;
            // top-anchored here; bottom-only is corrected after measuring height
            boxTop = style.PosT != CssComputed.Auto ? cbT + style.PosT : cbT;
        }
        else
        {
            // relative offsets the rendering but keeps the flow position
            int dx = 0, dy = 0;
            if (style.Position == CssPos.Relative)
            {
                if (style.PosL != CssComputed.Auto) dx = style.PosL;
                else if (style.PosR != CssComputed.Auto) dx = -style.PosR;
                if (style.PosT != CssComputed.Auto) dy = style.PosT;
                else if (style.PosB != CssComputed.Auto) dy = -style.PosB;
            }

            int freeSpace = availW - boxW;
            if (style.Float == CssFloat.Right)
            {
                boxX = availRight - marginRight - boxW + dx;
            }
            else if (!ctx.IsMeasuring && freeSpace > 0 && style.MarginLAuto && style.MarginRAuto)
            {
                boxX = availX + freeSpace / 2 + dx;
            }
            else if (!ctx.IsMeasuring && freeSpace > 0 && style.MarginLAuto)
            {
                boxX = availX + (freeSpace - marginRight) + dx;
            }
            else if (!ctx.IsMeasuring && freeSpace > 0 && style.TextAlign == CssAlign.Center && (style.HasW || style.MaxW < int.MaxValue || style.Display == CssDisplay.InlineBlock))
            {
                boxX = availX + freeSpace / 2 + dx;
            }
            else
            {
                boxX = availX + marginLeft + dx;
            }
            boxTop = y + marginTop + dy;
        }

        int contentX = boxX + bl + pl;
        int contentRight = boxX + boxW - br - pr;
        if (contentRight - contentX < 40 && !outOfFlow) contentRight = contentX + 40;
        int contentTop = boxTop + bt + pt;

        bool hidden = style.Visibility != CssVis.Visible;

        // A positioned element's padding box becomes the containing block for
        // its absolutely positioned descendants. With an explicit height the
        // bottom edge is known; with auto height it is circular, so mark the
        // bound unreliable and let bottom-anchored children fall back to top.
        int savedCbL = ctx.CbL, savedCbT = ctx.CbT, savedCbR = ctx.CbR, savedCbB = ctx.CbB;
        bool savedCbBValid = ctx.CbBValid;
        if (positioned)
        {
            ctx.CbL = boxX + bl;
            ctx.CbT = boxTop + bt;
            ctx.CbR = boxX + boxW - br;
            if (style.HasH) { ctx.CbB = contentTop + style.H + pb; ctx.CbBValid = true; }
            else { ctx.CbB = int.MaxValue / 2; ctx.CbBValid = false; }
        }

        // remember where this subtree's rendered items start (for bottom-anchoring)
        int boxStart = ctx.Plan.Boxes.Count;
        int lineStart = ctx.Plan.Lines.Count;
        int widgetStart = ctx.Plan.Widgets.Count;
        int linkStart = ctx.Plan.Links.Count;

        int cy = contentTop;
        CssComputed containerStyle = style;

        // Table rows/cells and captions are handled as containers; lists/tables recursed above.
        if (style.Display == CssDisplay.Flex)
            LayoutFlex(node, style, containerStyle, contentX, contentRight, ref cy, ctx);
        else if (style.Display == CssDisplay.Grid)
            LayoutGrid(node, style, containerStyle, contentX, contentRight, ref cy, ctx);
        else
            LayoutContainer(node, style, containerStyle, contentX, contentRight, ref cy, ctx);

        int contentBottom = cy;
        int explicitH = style.H;
        bool hasDefiniteH = style.HasH;
        if (style.IsHPct)
        {
            if (outOfFlow && cbBValid)
            {
                int cbHeight = cbB - cbT;
                if (cbHeight > 0)
                {
                    explicitH = (int)Math.Round(style.HPct * cbHeight / 100.0);
                    if (style.BoxSizing == CssBox.Border)
                        explicitH = Math.Max(0, explicitH - pt - pb - bt - bb);
                    hasDefiniteH = true;
                }
            }
            else
            {
                hasDefiniteH = false;
            }
        }

        int curMaxH = style.MaxH;
        if (style.IsMaxHPct && outOfFlow && cbBValid)
        {
            int cbHeight = cbB - cbT;
            if (cbHeight > 0) curMaxH = (int)Math.Round(style.MaxHPct * cbHeight / 100.0);
        }
        int curMinH = style.MinH;
        if (style.IsMinHPct && outOfFlow && cbBValid)
        {
            int cbHeight = cbB - cbT;
            if (cbHeight > 0) curMinH = (int)Math.Round(style.MinHPct * cbHeight / 100.0);
        }

        if (hasDefiniteH && explicitH > (contentBottom - contentTop)) contentBottom = contentTop + explicitH;
        if (curMinH > 0 && contentBottom - contentTop < curMinH) contentBottom = contentTop + curMinH;
        if (curMaxH < int.MaxValue && contentBottom - contentTop > curMaxH) contentBottom = contentTop + curMaxH;

        int boxBottom = contentBottom + pb + bb;
        int boxHeight = boxBottom - boxTop;
        if (curMaxH < int.MaxValue && boxHeight > curMaxH)
        {
            boxHeight = curMaxH;
            boxBottom = boxTop + boxHeight;
        }
        if (boxHeight < 1) boxHeight = 1;

        if (style.OverflowX == CssOverflow.Hidden || style.OverflowY == CssOverflow.Hidden)
        {
            int clipL = boxX + bl;
            int clipT = boxTop + bt;
            int clipR = boxX + boxW - br;
            int clipB = boxBottom - bb;
            int clipW = Math.Max(0, clipR - clipL);
            int clipH = Math.Max(0, clipB - clipT);

            for (int i = boxStart; i < ctx.Plan.Boxes.Count; i++)
            {
                RenderBox b = ctx.Plan.Boxes[i];
                if (b.HasClip)
                {
                    int r = Math.Min(b.ClipX + b.ClipW, clipR);
                    int bot = Math.Min(b.ClipY + b.ClipH, clipB);
                    b.ClipX = Math.Max(b.ClipX, clipL);
                    b.ClipY = Math.Max(b.ClipY, clipT);
                    b.ClipW = Math.Max(0, r - b.ClipX);
                    b.ClipH = Math.Max(0, bot - b.ClipY);
                }
                else
                {
                    b.HasClip = true;
                    b.ClipX = clipL; b.ClipY = clipT;
                    b.ClipW = clipW; b.ClipH = clipH;
                }
            }
            for (int i = lineStart; i < ctx.Plan.Lines.Count; i++)
            {
                RenderLine l = ctx.Plan.Lines[i];
                if (l.HasClip)
                {
                    int r = Math.Min(l.ClipX + l.ClipW, clipR);
                    int bot = Math.Min(l.ClipY + l.ClipH, clipB);
                    l.ClipX = Math.Max(l.ClipX, clipL);
                    l.ClipY = Math.Max(l.ClipY, clipT);
                    l.ClipW = Math.Max(0, r - l.ClipX);
                    l.ClipH = Math.Max(0, bot - l.ClipY);
                }
                else
                {
                    l.HasClip = true;
                    l.ClipX = clipL; l.ClipY = clipT;
                    l.ClipW = clipW; l.ClipH = clipH;
                }
            }
            for (int i = widgetStart; i < ctx.Plan.Widgets.Count; i++)
            {
                RenderWidget w = ctx.Plan.Widgets[i];
                if (w.HasClip)
                {
                    int r = Math.Min(w.ClipX + w.ClipW, clipR);
                    int bot = Math.Min(w.ClipY + w.ClipH, clipB);
                    w.ClipX = Math.Max(w.ClipX, clipL);
                    w.ClipY = Math.Max(w.ClipY, clipT);
                    w.ClipW = Math.Max(0, r - w.ClipX);
                    w.ClipH = Math.Max(0, bot - w.ClipY);
                }
                else
                {
                    w.HasClip = true;
                    w.ClipX = clipL; w.ClipY = clipT;
                    w.ClipW = clipW; w.ClipH = clipH;
                }
            }
        }

        // bottom-anchored absolute/fixed (no top): shift the measured subtree
        // up/down into place once the real height is known. Only when the
        // containing block's bottom edge is trustworthy.
        if (outOfFlow && style.PosT == CssComputed.Auto && style.PosB != CssComputed.Auto && cbBValid)
        {
            int finalTop = cbB - style.PosB - boxHeight;
            int shift = finalTop - boxTop;
            if (shift != 0)
            {
                ShiftRange(ctx.Plan, boxStart, lineStart, widgetStart, linkStart, 0, shift);
                boxTop = finalTop;
                contentTop += shift;
            }
        }

        // restore the containing block for following siblings
        if (positioned)
        {
            ctx.CbL = savedCbL; ctx.CbT = savedCbT; ctx.CbR = savedCbR; ctx.CbB = savedCbB;
            ctx.CbBValid = savedCbBValid;
        }

        Image bgImg = null;
        if (!string.IsNullOrEmpty(style.BgImage))
        {
            string abs = WebUrl.Resolve(ctx.BaseHref, style.BgImage);
            if (abs != null && ctx.Images.ContainsKey(abs)) ctx.Images.TryGetValue(abs, out bgImg);
            else if (ctx.Images.ContainsKey(style.BgImage)) ctx.Images.TryGetValue(style.BgImage, out bgImg);
        }

        bool paintBox = style.HasBg || bgImg != null || bt + bb + bl + br > 0 || !string.IsNullOrEmpty(style.BoxShadow) || style.Position == CssPos.Sticky || !string.IsNullOrEmpty(style.Transform);
        if (paintBox)
        {
            RenderBox box = new RenderBox
            {
                X = boxX,
                Y = boxTop,
                W = boxW,
                H = boxHeight,
                HasBg = style.HasBg,
                Bg = ToColor(style.BgColor, style.Opacity),
                BgImage = bgImg,
                BgRepeat = style.BgRepeat,
                BorderRadius = style.BorderRadius,
                BorderT = bt, BorderB = bb, BorderL = bl, BorderR = br,
                BorderCT = ToColor(style.BorderTC, style.Opacity),
                BorderCB = ToColor(style.BorderBC, style.Opacity),
                BorderCL = ToColor(style.BorderLC, style.Opacity),
                BorderCR = ToColor(style.BorderRC, style.Opacity),
                StyleT = style.BorderTT, StyleB = style.BorderBB, StyleL = style.BorderLL, StyleR = style.BorderRR,
                Hidden = hidden,
                Node = node,
                BoxShadow = style.BoxShadow,
                IsSticky = style.Position == CssPos.Sticky,
                StickyTop = style.PosT != CssComputed.Auto ? style.PosT : 0,
                ParentBottom = ctx.CbBValid ? ctx.CbB : int.MaxValue,
                Transform = null,
                ZIndex = style.ZIndex,
            };
            if (boxStart >= 0 && boxStart <= ctx.Plan.Boxes.Count)
                ctx.Plan.Boxes.Insert(boxStart, box);
            else
                ctx.Plan.Boxes.Add(box);
        }

        if (!string.IsNullOrEmpty(style.Transform))
        {
            ParseTransformTranslate(style.Transform, boxW, boxHeight, ctx.Vw, ctx.Vh, out int tdx, out int tdy);
            if (tdx != 0 || tdy != 0)
            {
                ShiftRange(ctx.Plan, boxStart, lineStart, widgetStart, linkStart, tdx, tdy);
            }
        }

        // absolute/fixed are out of normal flow: leave the flow cursor alone.
        // relative shifts rendering but must not move the flow position.
        if (!outOfFlow)
        {
            int flowDy = 0;
            if (style.Position == CssPos.Relative)
            {
                if (style.PosT != CssComputed.Auto) flowDy = style.PosT;
                else if (style.PosB != CssComputed.Auto) flowDy = -style.PosB;
            }
            y = (boxBottom - flowDy) + marginBottom;
        }
    }

    /// <summary>Translate every rendered item a subtree produced (used to nudge a
    /// bottom-anchored absolute box into place after its height is known).</summary>
    private static void ShiftRange(RenderPlan plan, int boxStart, int lineStart,
        int widgetStart, int linkStart, int dx, int dy)
    {
        for (int i = boxStart; i < plan.Boxes.Count; i++)
        {
            plan.Boxes[i].X += dx; plan.Boxes[i].Y += dy;
            if (plan.Boxes[i].HasClip) { plan.Boxes[i].ClipX += dx; plan.Boxes[i].ClipY += dy; }
        }
        for (int i = lineStart; i < plan.Lines.Count; i++)
        {
            RenderLine l = plan.Lines[i];
            l.Y += dy; l.TextX += dx; l.BulletX += dx;
            if (l.HasClip) { l.ClipX += dx; l.ClipY += dy; }
            for (int j = 0; j < l.Spans.Count; j++) { l.Spans[j].X += dx; l.Spans[j].Y += dy; }
        }
        for (int i = widgetStart; i < plan.Widgets.Count; i++)
        {
            plan.Widgets[i].X += dx; plan.Widgets[i].Y += dy;
            if (plan.Widgets[i].HasClip) { plan.Widgets[i].ClipX += dx; plan.Widgets[i].ClipY += dy; }
        }
        for (int i = linkStart; i < plan.Links.Count; i++) { plan.Links[i].X += dx; plan.Links[i].Y += dy; }
    }

    private static void ParseTransformTranslate(string transform, int boxW, int boxH, int vw, int vh, out int dx, out int dy)
    {
        dx = 0;
        dy = 0;
        if (string.IsNullOrEmpty(transform)) return;

        string t = transform.Trim();
        int idx = 0;
        while (idx < t.Length)
        {
            int pOpen = t.IndexOf('(', idx);
            if (pOpen < 0) break;
            int pClose = t.IndexOf(')', pOpen);
            if (pClose < 0) break;

            int fnStart = pOpen - 1;
            while (fnStart >= idx && (char.IsLetterOrDigit(t[fnStart]) || t[fnStart] == '-')) fnStart--;
            string fn = t.Substring(fnStart + 1, pOpen - fnStart - 1).Trim().ToLowerInvariant();
            string args = t.Substring(pOpen + 1, pClose - pOpen - 1).Trim();

            if (fn == "translatex")
            {
                dx += ParseTranslateComponent(args, boxW, vw);
            }
            else if (fn == "translatey")
            {
                dy += ParseTranslateComponent(args, boxH, vh);
            }
            else if (fn == "translate" || fn == "translate3d")
            {
                string[] parts = args.Split(',');
                if (parts.Length > 0) dx += ParseTranslateComponent(parts[0], boxW, vw);
                if (parts.Length > 1) dy += ParseTranslateComponent(parts[1], boxH, vh);
            }

            idx = pClose + 1;
        }
    }

    private static int ParseTranslateComponent(string s, int boxSize, int viewSize)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        s = s.Trim();
        if (s.EndsWith("%"))
        {
            string num = s.Substring(0, s.Length - 1).Trim();
            if (double.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pct))
            {
                return (int)(boxSize * pct / 100.0);
            }
        }
        else if (s.EndsWith("vw", StringComparison.OrdinalIgnoreCase))
        {
            string num = s.Substring(0, s.Length - 2).Trim();
            if (double.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
            {
                return (int)(viewSize * v / 100.0);
            }
        }
        else if (s.EndsWith("vh", StringComparison.OrdinalIgnoreCase))
        {
            string num = s.Substring(0, s.Length - 2).Trim();
            if (double.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
            {
                return (int)(viewSize * v / 100.0);
            }
        }
        else if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            string num = s.Substring(0, s.Length - 2).Trim();
            if (double.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double px))
            {
                return (int)px;
            }
        }
        else
        {
            if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
            {
                return (int)v;
            }
        }
        return 0;
    }

    // ---- flexbox ---------------------------------------------------------

    private static CssAlignItems ResolveAlign(CssAlignItems self, CssAlignItems container)
    {
        if (self != CssAlignItems.Auto) return self;
        return container == CssAlignItems.Auto ? CssAlignItems.Stretch : container;
    }

    private static int MeasureContentWidth(DomNode node, CssComputed style, Ctx ctx)
    {
        if (node == null) return 40;
        RenderPlan saved = ctx.Plan;
        RenderPlan scratch = new RenderPlan();
        ctx.Plan = scratch;
        CssComputed m = style.Clone();
        m.MarginL = m.MarginR = m.MarginT = m.MarginB = 0;
        m.HasW = false; m.HasH = false;
        m.Display = CssDisplay.Block;
        int y = 0;
        LayoutContainer(node, m, m, 0, 10000, ref y, ctx);
        ctx.Plan = saved;
        int maxR = 0;
        for (int li = 0; li < scratch.Lines.Count; li++)
        {
            RenderLine line = scratch.Lines[li];
            if (line.IsImage) { if (line.TextX + line.ImageW > maxR) maxR = line.TextX + line.ImageW; continue; }
            for (int si = 0; si < line.Spans.Count; si++)
            {
                RenderSpan s = line.Spans[si];
                if (s.X + s.Width > maxR) maxR = s.X + s.Width;
            }
        }
        for (int wi = 0; wi < scratch.Widgets.Count; wi++)
        {
            RenderWidget rw = scratch.Widgets[wi];
            if (rw.X + rw.W > maxR) maxR = rw.X + rw.W;
        }
        for (int bi = 0; bi < scratch.Boxes.Count; bi++)
        {
            RenderBox rb = scratch.Boxes[bi];
            if (rb.X + rb.W > maxR) maxR = rb.X + rb.W;
        }
        if (maxR < 20) maxR = 20;
        return maxR;
    }

    // Measure a flex item's natural content-box width (no wrap).
    private static int MeasureNaturalWidth(FlexItem it, Ctx ctx)
    {
        int w;
        if (it.IsAnonymous)
        {
            w = 0;
            for (int i = 0; i < it.Runs.Count; i++)
            {
                Run r = it.Runs[i];
                if (r.Break) continue;
                int sz = r.Style != null ? r.Style.FontSize : 13;
                w += ctx.Measure(r.Text, sz);
                if (i + 1 < it.Runs.Count) w += 4;
            }
        }
        else
        {
            RenderPlan saved = ctx.Plan;
            bool savedMeasuring = ctx.IsMeasuring;
            ctx.IsMeasuring = true;
            RenderPlan scratch = new RenderPlan();
            ctx.Plan = scratch;
            CssComputed m = it.Style.Clone();
            m.MarginL = m.MarginR = m.MarginT = m.MarginB = 0;
            m.HasW = false; m.HasH = false;
            int y = 0;
            LayoutBlockNode(it.Node, m, 0, 10000, ref y, ctx, 0);
            ctx.Plan = saved;
            ctx.IsMeasuring = savedMeasuring;
            int contentX = m.BorderLW + m.PadL;
            int maxR = contentX;
            for (int li = 0; li < scratch.Lines.Count; li++)
            {
                RenderLine line = scratch.Lines[li];
                if (line.IsImage) { if (line.TextX + line.ImageW > maxR) maxR = line.TextX + line.ImageW; continue; }
                for (int si = 0; si < line.Spans.Count; si++)
                {
                    RenderSpan s = line.Spans[si];
                    if (s.X + s.Width > maxR) maxR = s.X + s.Width;
                }
            }
            for (int wi = 0; wi < scratch.Widgets.Count; wi++)
            {
                RenderWidget rw = scratch.Widgets[wi];
                if (rw.X + rw.W > maxR) maxR = rw.X + rw.W;
            }
            w = maxR - contentX;
        }
        if (w < 1) w = 1;
        CssComputed st = it.Style;
        if (st.MaxW < int.MaxValue && w > st.MaxW) w = st.MaxW;
        if (st.MinW > 0 && w < st.MinW) w = st.MinW;
        return w;
    }

    // Measure a flex item's border-box height when its content-box width is contentW.
    private static int MeasureBorderHeightAt(FlexItem it, int contentW, Ctx ctx)
    {
        CssComputed st = it.Style;
        if (it.IsAnonymous)
        {
            RenderPlan saved = ctx.Plan;
            RenderPlan sc = new RenderPlan();
            ctx.Plan = sc;
            int y = 0;
            FlushRuns(new List<Run>(it.Runs), st, 0, contentW + st.PadL + st.PadR, ref y, ctx, null, 0);
            ctx.Plan = saved;
            return y < 1 ? 1 : y;
        }
        RenderPlan saved2 = ctx.Plan;
        RenderPlan s2 = new RenderPlan();
        ctx.Plan = s2;
        CssComputed m = st.Clone();
        m.MarginL = m.MarginR = m.MarginT = m.MarginB = 0;
        m.HasW = true; m.HasH = false;
        int bw = contentW + m.PadL + m.PadR + m.BorderLW + m.BorderRW;
        m.W = m.BoxSizing == CssBox.Border ? bw : contentW;
        int y2 = 0;
        LayoutBlockNode(it.Node, m, 0, bw, ref y2, ctx, 0);
        ctx.Plan = saved2;
        return y2 < 1 ? 1 : y2;
    }

    // Lay one item out for real at a border-box top-left, with a fixed border-box size.
    private static void LayFlexItem(FlexItem it, int boxLeft, int boxTop, int boxW, int boxH, Ctx ctx)
    {
        CssComputed st = it.Style;
        if (it.IsAnonymous)
        {
            RenderPlan saved = ctx.Plan;
            // anonymous items lay out inline runs; keep them on the real plan
            int fy = boxTop;
            FlushRuns(new List<Run>(it.Runs), st, boxLeft, boxLeft + boxW, ref fy, ctx, null, 0);
            return;
        }
        CssComputed f = st.Clone();
        f.MarginL = f.MarginR = f.MarginT = f.MarginB = 0;
        f.HasW = true; f.HasH = true;
        if (f.BoxSizing == CssBox.Border) { f.W = boxW; f.H = boxH; }
        else
        {
            f.W = boxW - (f.PadL + f.PadR + f.BorderLW + f.BorderRW);
            f.H = boxH - (f.PadT + f.PadB + f.BorderTW + f.BorderBW);
        }
        if (f.W < 1) f.W = 1;
        if (f.H < 1) f.H = 1;
        int fy2 = boxTop;
        LayoutBlockNode(it.Node, f, boxLeft, boxLeft + boxW, ref fy2, ctx, 0);
    }

    private static void CollectFlexItems(DomNode container, CssComputed baseStyle, bool row,
        List<FlexItem> items, List<DomNode> outOfFlow, Ctx ctx)
    {
        List<Run> pending = null;
        DomNode child = container.FirstChild;
        while (child != null)
        {
            if (child.NodeType == DomNode.TextNode)
            {
                string collapsed = CollapseWs(child.Text);
                bool hasInk = false;
                for (int i = 0; i < collapsed.Length; i++) if (collapsed[i] != ' ') { hasInk = true; break; }
                if (hasInk)
                {
                    if (pending == null) pending = new List<Run>();
                    AddTextRuns(pending, collapsed, baseStyle, null, container);
                }
            }
            else if (child.IsElement)
            {
                CssComputed cs = ctx.Sheet.Compute(child, baseStyle, ctx.Vw, ctx.Vh);
                if (cs.Display != CssDisplay.None)
                {
                    if (cs.Display == CssDisplay.Contents)
                    {
                        CollectFlexItems(child, cs, row, items, outOfFlow, ctx);
                        child = child.Next;
                        continue;
                    }
                    if (cs.Position == CssPos.Absolute || cs.Position == CssPos.Fixed)
                    {
                        if (outOfFlow != null) outOfFlow.Add(child);
                    }
                    else
                    {
                        if (pending != null) { items.Add(new FlexItem { IsAnonymous = true, Runs = pending, Style = baseStyle.Clone() }); pending = null; }
                        items.Add(new FlexItem { Node = child, Style = cs });
                    }
                }
            }
            child = child.Next;
        }
        if (pending != null) items.Add(new FlexItem { IsAnonymous = true, Runs = pending, Style = baseStyle.Clone() });

        // sort by 'order' (stable)
        for (int i = 1; i < items.Count; i++)
        {
            FlexItem di = items[i]; int dj = i - 1;
            while (dj >= 0 && items[dj].Style.Order > di.Style.Order) { items[dj + 1] = items[dj]; dj--; }
            items[dj + 1] = di;
        }

        // pull per-item box-model parts
        for (int i = 0; i < items.Count; i++)
        {
            FlexItem it = items[i]; CssComputed s = it.Style;
            if (row)
            {
                it.MarginMain0 = s.MarginL; it.MarginMain1 = s.MarginR;
                it.MarginCross0 = s.MarginT; it.MarginCross1 = s.MarginB;
                it.PadMain = s.PadL + s.PadR; it.BorderMain = s.BorderLW + s.BorderRW;
                it.PadCross = s.PadT + s.PadB; it.BorderCross = s.BorderTW + s.BorderBW;
            }
            else
            {
                it.MarginMain0 = s.MarginT; it.MarginMain1 = s.MarginB;
                it.MarginCross0 = s.MarginL; it.MarginCross1 = s.MarginR;
                it.PadMain = s.PadT + s.PadB; it.BorderMain = s.BorderTW + s.BorderBW;
                it.PadCross = s.PadL + s.PadR; it.BorderCross = s.BorderLW + s.BorderRW;
            }
            it.Grow = s.FlexGrow; it.Shrink = s.FlexShrink; it.Order = s.Order;
            it.AlignSelf = s.AlignSelf;
            it.MinMain = row ? s.MinW : s.MinH;
            if (it.IsAnonymous && row) it.MinMain = it.BaseSize;
            it.MaxMain = row ? s.MaxW : s.MaxH;
        }
    }

    // Distribute free/overflow along the main axis for one line of items [lo,hi).
    private static void ResolveFlexLine(List<FlexItem> items, int lo, int hi, int containerMain, int gap)
    {
        // hypothetical outer main sizes
        long usedOuter = 0; float totalGrow = 0, weightedShrink = 0;
        for (int i = lo; i < hi; i++)
        {
            FlexItem it = items[i];
            int outer = it.BaseSize + it.PadMain + it.BorderMain + it.MarginMain0 + it.MarginMain1;
            usedOuter += outer;
            totalGrow += it.Grow;
            weightedShrink += it.Shrink * it.BaseSize;
        }
        long gaps = (long)gap * (hi - lo - 1);
        int free = (int)(containerMain - usedOuter - gaps);

        if (free > 0 && totalGrow > 0.001f)
        {
            for (int i = lo; i < hi; i++)
            {
                FlexItem it = items[i];
                if (it.Grow <= 0) continue;
                int add = (int)(free * (it.Grow / totalGrow));
                it.BaseSize += add;
            }
        }
        else if (free < 0 && weightedShrink > 0.001f)
        {
            int toRemove = -free;
            for (int i = lo; i < hi; i++)
            {
                FlexItem it = items[i];
                if (it.Shrink <= 0 || it.BaseSize <= 0) continue;
                int sub = (int)(toRemove * (it.Shrink * it.BaseSize / weightedShrink));
                it.BaseSize -= sub;
            }
        }

        // clamp to min/max and recompute leftover
        for (int i = lo; i < hi; i++)
        {
            FlexItem it = items[i];
            if (it.MaxMain < int.MaxValue && it.BaseSize > it.MaxMain) it.BaseSize = it.MaxMain;
            if (it.MinMain > 0 && it.BaseSize < it.MinMain) it.BaseSize = it.MinMain;
            if (it.BaseSize < 0) it.BaseSize = 0;
        }
    }

    private static void LayoutFlex(DomNode container, CssComputed cstyle, CssComputed baseStyle,
        int contentX, int contentRight, ref int y, Ctx ctx)
    {
        int flexStartY = y;
        bool row = cstyle.FlexDirection == CssFlexDir.Row || cstyle.FlexDirection == CssFlexDir.RowReverse;
        bool reverseMain = cstyle.FlexDirection == CssFlexDir.RowReverse || cstyle.FlexDirection == CssFlexDir.ColumnReverse;
        bool wrap = cstyle.FlexWrap == CssFlexWrap.Wrap || cstyle.FlexWrap == CssFlexWrap.WrapReverse;
        bool reverseCross = cstyle.FlexWrap == CssFlexWrap.WrapReverse;

        List<FlexItem> items = new List<FlexItem>();
        List<DomNode> outOfFlow = new List<DomNode>();
        CollectFlexItems(container, baseStyle, row, items, outOfFlow, ctx);

        if (items.Count == 0)
        {
            for (int i = 0; i < outOfFlow.Count; i++)
            {
                DomNode oof = outOfFlow[i];
                CssComputed cs = ctx.Sheet.Compute(oof, baseStyle, ctx.Vw, ctx.Vh);
                int oofY = flexStartY;
                LayoutBlockNode(oof, cs, contentX, contentRight, ref oofY, ctx, 0);
            }
            return;
        }

        CssAlignItems contAlign = cstyle.AlignItems == CssAlignItems.Auto ? CssAlignItems.Stretch : cstyle.AlignItems;
        int containerCross = contentRight - contentX;   // for row: width; used as line width
        int containerMainSize = row ? containerCross : 0;
        int mainGap = row ? cstyle.GapCol : cstyle.GapRow;
        int crossGap = row ? cstyle.GapRow : cstyle.GapCol;

        // ---- determine base main size per item ----
        if (row)
        {
            for (int i = 0; i < items.Count; i++)
            {
                FlexItem it = items[i]; CssComputed s = it.Style;
                if (s.HasW)
                    it.BaseSize = s.BoxSizing == CssBox.Border ? s.W - it.PadMain - it.BorderMain : s.W;
                else if (s.HasFlexBasis)
                    it.BaseSize = s.FlexBasis;
                else
                    it.BaseSize = MeasureNaturalWidth(it, ctx);
            }
        }
        else
        {
            // column: cross (width) first
            for (int i = 0; i < items.Count; i++)
            {
                FlexItem it = items[i]; CssComputed s = it.Style;
                CssAlignItems al = ResolveAlign(it.AlignSelf, contAlign);
                if (s.HasW)
                    it.CrossSize = s.BoxSizing == CssBox.Border ? s.W - it.PadCross - it.BorderCross : s.W;
                else if (al == CssAlignItems.Stretch && s.Display != CssDisplay.InlineBlock)
                    it.CrossSize = Math.Max(1, containerCross - it.PadCross - it.BorderCross);
                else
                    it.CrossSize = Math.Max(1, MeasureNaturalWidth(it, ctx));
                if (s.MaxW < int.MaxValue && it.CrossSize > s.MaxW) it.CrossSize = s.MaxW;
                if (s.MinW > 0 && it.CrossSize < s.MinW) it.CrossSize = s.MinW;
            }
            for (int i = 0; i < items.Count; i++)
            {
                FlexItem it = items[i]; CssComputed s = it.Style;
                if (s.HasH)
                    it.BaseSize = s.BoxSizing == CssBox.Border ? s.H - it.PadMain - it.BorderMain : s.H;
                else if (s.HasFlexBasis)
                    it.BaseSize = s.FlexBasis;
                else
                    it.BaseSize = MeasureBorderHeightAt(it, it.CrossSize, ctx) - it.PadMain - it.BorderMain;
            }
        }

        // ---- build lines (main-axis grouping) ----
        List<int> lineStart = new List<int>();
        List<int> lineEnd = new List<int>();
        if (!wrap)
        {
            lineStart.Add(0); lineEnd.Add(items.Count);
        }
        else
        {
            long runOuter = 0; int start = 0;
            for (int i = 0; i < items.Count; i++)
            {
                FlexItem it = items[i];
                int outer = it.BaseSize + it.PadMain + it.BorderMain + it.MarginMain0 + it.MarginMain1;
                long needed = runOuter + outer + (i > start ? mainGap : 0);
                if (i > start && needed > containerMainSize)
                {
                    lineStart.Add(start); lineEnd.Add(i);
                    start = i; runOuter = outer;
                }
                else runOuter = needed;
            }
            lineStart.Add(start); lineEnd.Add(items.Count);
        }

        // ---- resolve flexible lengths + position items line by line ----
        int mainCursor = 0;   // accumulated main offset across lines (for column) / cross lines stacked in y
        int totalCross = 0;   // sum of line cross sizes (+ gaps) — becomes the content height for column, or lines for row wrap

        for (int l = 0; l < lineStart.Count; l++)
        {
            int lo = lineStart[l], hi = lineEnd[l];

            if (row)
            {
                ResolveFlexLine(items, lo, hi, containerMainSize, mainGap);

                // measure each item's cross (height) at its resolved content width
                int lineCross = 0;
                for (int i = lo; i < hi; i++)
                {
                    FlexItem it = items[i];
                    int bw = it.BaseSize + it.PadMain + it.BorderMain;
                    if (it.Style.HasH)
                        it.CrossSize = it.Style.BoxSizing == CssBox.Border
                            ? it.Style.H - it.PadCross - it.BorderCross
                            : it.Style.H;
                    else
                        it.CrossSize = MeasureBorderHeightAt(it, it.BaseSize, ctx) - it.PadCross - it.BorderCross;
                    if (it.CrossSize < 1) it.CrossSize = 1;
                    int outerCross = it.CrossSize + it.PadCross + it.BorderCross + it.MarginCross0 + it.MarginCross1;
                    if (outerCross > lineCross) lineCross = outerCross;
                }
                // A single-line container with a definite cross (height) size: the line
                // fills it, so align-items:center/stretch center against the container,
                // not against the tallest item.
                if (lineStart.Count == 1 && cstyle.HasH && cstyle.H > lineCross) lineCross = cstyle.H;

                // leftover + justify-content
                long usedOuter = 0;
                for (int i = lo; i < hi; i++)
                {
                    FlexItem it = items[i];
                    usedOuter += it.BaseSize + it.PadMain + it.BorderMain + it.MarginMain0 + it.MarginMain1;
                }
                long gaps = (long)mainGap * (hi - lo - 1);
                int leftover = (int)(containerMainSize - usedOuter - gaps);
                int lead = 0, extraGap = 0; int n = hi - lo;
                if (!ctx.IsMeasuring)
                {
                    switch (cstyle.JustifyContent)
                    {
                        case CssJustify.FlexEnd: lead = leftover; break;
                        case CssJustify.Center: lead = leftover / 2; break;
                        case CssJustify.SpaceBetween: if (n > 1) extraGap = leftover / (n - 1); else lead = leftover / 2; break;
                        case CssJustify.SpaceAround: lead = leftover / (2 * n); extraGap = n > 0 ? leftover / n : 0; break;
                        case CssJustify.SpaceEvenly: lead = leftover / (n + 1); extraGap = leftover / (n + 1); break;
                        default: lead = 0; break;
                    }
                    if (leftover < 0) { lead = 0; extraGap = 0; }
                }

                // place items
                int cur = lead;
                int orderStart = lo, orderEnd = hi, orderStep = 1;
                if (reverseMain) { cur = leftover - lead; orderStart = hi - 1; orderEnd = -1; orderStep = -1; }
                for (int idx = orderStart; idx != orderEnd; idx += orderStep)
                {
                    FlexItem it = items[idx];
                    int bw = it.BaseSize + it.PadMain + it.BorderMain;
                    if (reverseMain) cur -= (it.MarginMain1 + bw + it.MarginMain0 + mainGap + extraGap);
                    int boxLeft = contentX + cur + it.MarginMain0;
                    if (!reverseMain) cur += it.MarginMain0 + bw + it.MarginMain1 + mainGap + extraGap;

                    // cross position within the line
                    int outerCross = it.CrossSize + it.PadCross + it.BorderCross;
                    int targetH = outerCross;
                    CssAlignItems al = ResolveAlign(it.AlignSelf, contAlign);
                    bool stretch = al == CssAlignItems.Stretch && !it.Style.HasH;
                    if (stretch) targetH = lineCross - it.MarginCross0 - it.MarginCross1;
                    if (targetH < 1) targetH = 1;
                    int crossTop;
                    switch (al)
                    {
                        case CssAlignItems.FlexEnd: crossTop = lineCross - it.MarginCross1 - targetH; break;
                        case CssAlignItems.Center: crossTop = (lineCross - targetH) / 2; break;
                        default: crossTop = it.MarginCross0; break;   // flex-start / stretch / baseline
                    }
                    int boxTop = y + mainCursor + crossTop;
                    int contentH = targetH - it.PadCross - it.BorderCross;
                    if (contentH < 1) contentH = 1;

                    LayFlexItem(it, boxLeft, boxTop, bw, targetH, ctx);
                }

                totalCross += lineCross;
                mainCursor += lineCross;
            }
            else
            {
                // column
                if (cstyle.HasH)
                    ResolveFlexLine(items, lo, hi, cstyle.H, mainGap);

                // measure natural main (height) for auto items already done; compute leftover if definite height
                long usedOuter = 0;
                for (int i = lo; i < hi; i++)
                {
                    FlexItem it = items[i];
                    usedOuter += it.BaseSize + it.PadMain + it.BorderMain + it.MarginMain0 + it.MarginMain1;
                }
                long gaps = (long)mainGap * (hi - lo - 1);
                int leftover = cstyle.HasH ? (int)(cstyle.H - usedOuter - gaps) : 0;
                int lead = 0, extraGap = 0; int n = hi - lo;
                if (cstyle.HasH)
                {
                    switch (cstyle.JustifyContent)
                    {
                        case CssJustify.FlexEnd: lead = leftover; break;
                        case CssJustify.Center: lead = leftover / 2; break;
                        case CssJustify.SpaceBetween: if (n > 1) extraGap = leftover / (n - 1); else lead = leftover / 2; break;
                        case CssJustify.SpaceAround: lead = leftover / (2 * n); extraGap = n > 0 ? leftover / n : 0; break;
                        case CssJustify.SpaceEvenly: lead = leftover / (n + 1); extraGap = leftover / (n + 1); break;
                        default: lead = 0; break;
                    }
                    if (leftover < 0) { lead = 0; extraGap = 0; }
                }

                int cur = lead;
                int orderStart = lo, orderEnd = hi, orderStep = 1;
                if (reverseMain) { cur = (cstyle.HasH ? leftover : 0) - lead; orderStart = hi - 1; orderEnd = -1; orderStep = -1; }
                for (int idx = orderStart; idx != orderEnd; idx += orderStep)
                {
                    FlexItem it = items[idx];
                    int bw = it.CrossSize + it.PadCross + it.BorderCross;   // width
                    int bh = it.BaseSize + it.PadMain + it.BorderMain;       // height
                    if (reverseMain) cur -= (it.MarginMain1 + bh + it.MarginMain0 + mainGap + extraGap);
                    int boxTop = y + cur + it.MarginMain0;
                    if (!reverseMain) cur += it.MarginMain0 + bh + it.MarginMain1 + mainGap + extraGap;

                    CssAlignItems al = ResolveAlign(it.AlignSelf, contAlign);
                    int boxLeft;
                    if (ctx.IsMeasuring)
                    {
                        boxLeft = contentX + it.MarginCross0;
                    }
                    else
                    {
                        switch (al)
                        {
                            case CssAlignItems.FlexEnd: boxLeft = contentRight - it.MarginCross1 - bw; break;
                            case CssAlignItems.Center: boxLeft = contentX + (containerCross - bw) / 2; break;
                            default:
                                if ((it.Style.MarginLAuto && it.Style.MarginRAuto) || (bw < containerCross && (cstyle.TextAlign == CssAlign.Center || it.Style.TextAlign == CssAlign.Center)))
                                    boxLeft = contentX + (containerCross - bw) / 2;
                                else if (it.Style.MarginLAuto)
                                    boxLeft = contentRight - it.MarginCross1 - bw;
                                else
                                    boxLeft = contentX + it.MarginCross0;
                                break;
                        }
                    }

                    LayFlexItem(it, boxLeft, boxTop, bw, bh, ctx);
                }

                totalCross += 0;   // column main axis already advanced via cur
                // for column, the content height is the consumed main length
                int consumed = lead;
                for (int i = lo; i < hi; i++)
                {
                    FlexItem it = items[i];
                    consumed += it.MarginMain0 + it.BaseSize + it.PadMain + it.BorderMain + it.MarginMain1 + mainGap + extraGap;
                }
                if (hi - lo > 0) consumed -= mainGap + extraGap;
                mainCursor += consumed;
            }
        }

        if (row) y += totalCross;
        else y += mainCursor;

        // Lay out out-of-flow children (position: absolute / fixed) independently after flex bounds are known
        if (outOfFlow.Count > 0)
        {
            int savedCbB = ctx.CbB;
            bool savedCbBValid = ctx.CbBValid;
            if (cstyle.Position != CssPos.Static)
            {
                ctx.CbB = y;
                ctx.CbBValid = true;
            }
            for (int i = 0; i < outOfFlow.Count; i++)
            {
                DomNode oof = outOfFlow[i];
                CssComputed cs = ctx.Sheet.Compute(oof, baseStyle, ctx.Vw, ctx.Vh);
                int oofY = flexStartY;
                LayoutBlockNode(oof, cs, contentX, contentRight, ref oofY, ctx, 0);
            }
            ctx.CbB = savedCbB;
            ctx.CbBValid = savedCbBValid;
        }
    }

    // ---- lists -----------------------------------------------------------

    private static void LayoutList(DomNode node, CssComputed style, int availX, int availRight,
        ref int y, Ctx ctx, int listIndent)
    {
        bool ordered = node.Tag == "ol";
        int counter = 1;
        string startAttr = node.GetAttr("start");
        if (ordered && startAttr != null)
        {
            int s;
            if (int.TryParse(startAttr.Trim(), out s)) counter = s;
        }

        int marginLeft = style.MarginL + listIndent;
        int contentX = availX + marginLeft;
        int contentRight = availRight - style.MarginR;
        if (contentRight - contentX < 60) contentRight = contentX + 60;

        y += style.MarginT;

        DomNode child = node.FirstElementChild();
        while (child != null)
        {
            if (child.IsElement && child.Tag == "li")
            {
                CssComputed liStyle = ctx.Sheet.Compute(child, style, ctx.Vw, ctx.Vh);
                if (liStyle.Display == CssDisplay.None) { child = child.Next; continue; }
                string marker = ListMarker(liStyle.ListStyleType, ordered, counter);
                LayoutListItem(child, liStyle, contentX, contentRight, ref y, ctx, 0, marker, 0);
                if (ordered) counter++;
            }
            else if (child.IsElement && (child.Tag == "ul" || child.Tag == "ol"))
            {
                CssComputed sub = ctx.Sheet.Compute(child, style, ctx.Vw, ctx.Vh);
                LayoutList(child, sub, contentX - 16, contentRight, ref y, ctx, 16);
            }
            child = child.Next;
        }
        y += style.MarginB;
    }

    private static void LayoutListItem(DomNode node, CssComputed style, int contentX, int contentRight,
        ref int y, Ctx ctx, int extraIndent, string marker, int markerNum)
    {
        int x = contentX + extraIndent + style.MarginL;
        int right = contentRight;
        y += style.MarginT;

        // bullet area
        int bulletSpace = marker != null && marker.Length > 0
            ? Math.Max(18, ctx.Measure(marker, style.FontSize > 0 ? style.FontSize : 13) + 8)
            : 0;

        List<Run> runs = new List<Run>();
        // ::before content
        AddPseudo(runs, node, style, "before", ctx);

        DomNode child = node.FirstChild;
        while (child != null)
        {
            if (child.NodeType == DomNode.TextNode)
            {
                AddTextRuns(runs, child.Text, style, null, node);
            }
            else if (child.IsElement)
            {
                CssComputed cs = ctx.Sheet.Compute(child, style, ctx.Vw, ctx.Vh);
                if (cs.Display == CssDisplay.None) { child = child.Next; continue; }
                if (child.Tag == "ul" || child.Tag == "ol")
                {
                    FlushRuns(runs, style, x + bulletSpace, right, ref y, ctx, marker, x);
                    marker = null; bulletSpace = 0;
                    LayoutList(child, cs, x, right, ref y, ctx, 16);
                }
                else if (child.Tag == "br")
                {
                    FlushRuns(runs, style, x + bulletSpace, right, ref y, ctx, marker, x);
                    marker = null; bulletSpace = 0;
                    runs.Add(new Run { Break = true });
                }
                else if (cs.Display == CssDisplay.Contents)
                {
                    DomNode cchild = child.FirstChild;
                    while (cchild != null)
                    {
                        if (cchild.NodeType == DomNode.TextNode)
                            AddTextRuns(runs, cchild.Text, style, null, child);
                        else if (cchild.IsElement)
                        {
                            CssComputed ccs = ctx.Sheet.Compute(cchild, style, ctx.Vw, ctx.Vh);
                            if (ccs.Display != CssDisplay.None)
                            {
                                if (IsBlockLevel(ccs))
                                {
                                    FlushRuns(runs, style, x + bulletSpace, right, ref y, ctx, marker, x);
                                    marker = null; bulletSpace = 0;
                                    LayoutBlockNode(cchild, ccs, x, right, ref y, ctx, 0);
                                }
                                else
                                    CollectInline(cchild, ccs, runs, ctx, cchild.Tag == "a" ? cchild.GetAttr("href") : null);
                            }
                        }
                        cchild = cchild.Next;
                    }
                }
                else if (IsBlockLevel(cs))
                {
                    FlushRuns(runs, style, x + bulletSpace, right, ref y, ctx, marker, x);
                    marker = null; bulletSpace = 0;
                    LayoutBlockNode(child, cs, x, right, ref y, ctx, 0);
                }
                else
                {
                    CollectInline(child, cs, runs, ctx, child.Tag == "a" ? child.GetAttr("href") : null);
                }
            }
            child = child.Next;
        }
        AddPseudo(runs, node, style, "after", ctx);
        FlushRuns(runs, style, x + bulletSpace, right, ref y, ctx, marker, x);
        y += style.MarginB;
    }

    // ---- tables (approximate: each row merged into one line) -------------

    private static void LayoutTable(DomNode node, CssComputed style, int availX, int availRight,
        ref int y, Ctx ctx)
    {
        y += style.MarginT;
        int x = availX + style.MarginL;
        int right = availRight - style.MarginR;

        DomNode row = node.FirstElementChild();
        while (row != null)
        {
            if (row.IsElement && (row.Tag == "tr"))
            {
                LayoutTableRow(row, x, right, ref y, ctx, style);
            }
            else if (row.IsElement && (row.Tag == "thead" || row.Tag == "tbody" || row.Tag == "tfoot"))
            {
                DomNode r = row.FirstElementChild();
                while (r != null)
                {
                    if (r.IsElement && r.Tag == "tr") LayoutTableRow(r, x, right, ref y, ctx, style);
                    r = r.NextElementSibling();
                }
            }
            row = row.NextElementSibling();
        }
        y += style.MarginB;
    }

    private static void LayoutTableRow(DomNode tr, int x, int right, ref int y, Ctx ctx, CssComputed tableStyle)
    {
        List<DomNode> cells = new List<DomNode>();
        DomNode cell = tr.FirstElementChild();
        while (cell != null)
        {
            if (cell.IsElement && (cell.Tag == "td" || cell.Tag == "th")) cells.Add(cell);
            cell = cell.NextElementSibling();
        }
        if (cells.Count == 0) return;

        int tableW = Math.Max(40, right - x);
        int[] colW = new int[cells.Count];
        int definedW = 0;
        int autoCount = 0;

        for (int i = 0; i < cells.Count; i++)
        {
            DomNode c = cells[i];
            CssComputed cs = ctx.Sheet.Compute(c, tableStyle, ctx.Vw, ctx.Vh);
            string wAttr = c.GetAttr("width");
            int cw = 0;
            if (!string.IsNullOrEmpty(wAttr))
            {
                if (wAttr.EndsWith("%"))
                {
                    int pct = ParseInt(wAttr.TrimEnd('%'), 0);
                    if (pct > 0) cw = tableW * pct / 100;
                }
                else
                {
                    cw = ParseInt(wAttr, 0);
                }
            }
            if (cw == 0 && cs.HasW && cs.W > 0) cw = cs.W;

            if (cw > 0)
            {
                colW[i] = cw;
                definedW += cw;
            }
            else
            {
                autoCount++;
            }
        }

        int remainingW = Math.Max(0, tableW - definedW);
        if (autoCount > 0)
        {
            int autoEach = remainingW / autoCount;
            for (int i = 0; i < cells.Count; i++)
            {
                if (colW[i] == 0) colW[i] = Math.Max(20, autoEach);
            }
        }

        int startY = y;
        int maxY = startY;
        int curX = x;

        for (int i = 0; i < cells.Count; i++)
        {
            DomNode c = cells[i];
            int cellW = colW[i];
            int cellX = curX;
            int cellRight = curX + cellW;
            curX += cellW;

            CssComputed cs = ctx.Sheet.Compute(c, tableStyle, ctx.Vw, ctx.Vh);
            int cellY = startY;

            bool hasBlockChild = false;
            DomNode ch = c.FirstChild;
            while (ch != null)
            {
                if (ch.IsElement)
                {
                    CssComputed childStyle = ctx.Sheet.Compute(ch, cs, ctx.Vw, ctx.Vh);
                    if (IsBlockLevel(childStyle)) { hasBlockChild = true; break; }
                }
                ch = ch.Next;
            }

            int contentX = cellX + cs.PadL + cs.BorderLW;
            int contentRight = cellRight - cs.PadR - cs.BorderRW;
            if (contentRight - contentX < 20) contentRight = contentX + 20;

            if (hasBlockChild)
            {
                LayoutContainer(c, cs, cs, contentX, contentRight, ref cellY, ctx);
            }
            else
            {
                List<Run> runs = new List<Run>();
                DomNode ch2 = c.FirstChild;
                while (ch2 != null)
                {
                    if (ch2.NodeType == DomNode.TextNode) AddTextRuns(runs, ch2.Text, cs, null, c);
                    else if (ch2.IsElement)
                    {
                        CssComputed gs = ctx.Sheet.Compute(ch2, cs, ctx.Vw, ctx.Vh);
                        if (gs.Display != CssDisplay.None)
                        {
                            if (ch2.Tag == "br") runs.Add(new Run { Break = true });
                            else CollectInline(ch2, gs, runs, ctx, ch2.Tag == "a" ? ch2.GetAttr("href") : null);
                        }
                    }
                    ch2 = ch2.Next;
                }
                FlushRuns(runs, cs, contentX, contentRight, ref cellY, ctx, null, 0);
            }

            if (cellY > maxY) maxY = cellY;
        }

        y = maxY > startY ? maxY : startY + 20;
    }

    // ---- images / rules --------------------------------------------------

    private static void EmitImage(DomNode node, CssComputed style, int contentX, int contentRight, ref int y, Ctx ctx)
    {
        y += style.MarginT;
        string src = node.GetAttr("src");
        string abs = src != null ? WebUrl.Resolve(ctx.BaseHref, src) : null;
        Image img = null;
        if (abs != null && ctx.Images.ContainsKey(abs)) ctx.Images.TryGetValue(abs, out img);
        else if (src != null && ctx.Images.ContainsKey(src)) ctx.Images.TryGetValue(src, out img);

        int attrW = ParseInt(node.GetAttr("width"), 0);
        int attrH = ParseInt(node.GetAttr("height"), 0);
        int cssW = style.HasW && style.W > 0 ? style.W : 0;
        int cssH = style.HasH && style.H > 0 ? style.H : 0;

        int maxW = Math.Max(20, contentRight - contentX);
        int w, h;
        if (img != null)
        {
            int natW = img.Width > 0 ? img.Width : 64;
            int natH = img.Height > 0 ? img.Height : 64;
            w = cssW > 0 ? cssW : (attrW > 0 ? attrW : natW);
            h = cssH > 0 ? cssH : (attrH > 0 ? attrH : natH);
            if (w > maxW) { h = Math.Max(1, h * maxW / Math.Max(1, w)); w = maxW; }
            int maxH = 420;
            if (h > maxH) { w = Math.Max(1, w * maxH / Math.Max(1, h)); h = maxH; }
        }
        else
        {
            w = cssW > 0 ? Math.Min(cssW, maxW) : (attrW > 0 ? Math.Min(attrW, maxW) : 48);
            h = cssH > 0 ? cssH : (attrH > 0 ? attrH : 48);
        }
        if (w < 1) w = 1;
        if (h < 1) h = 1;

        int imgX = contentX;
        if (contentRight - contentX > w)
        {
            if (style.TextAlign == CssAlign.Center || (style.MarginLAuto && style.MarginRAuto))
                imgX = contentX + (contentRight - contentX - w) / 2;
            else if (style.TextAlign == CssAlign.Right || style.MarginLAuto)
                imgX = contentRight - w;
        }

        RenderLine line = new RenderLine
        {
            Y = y,
            Height = h,
            IsImage = true,
            ImageW = w,
            ImageH = h,
            ImageAlt = node.GetAttr("alt") ?? "",
            Image = img,
            TextX = imgX,
            Hidden = style.Visibility != CssVis.Visible,
            Node = node,
        };
        ctx.Plan.Lines.Add(line);
        y += h + style.MarginB;
    }

    private static void EmitSvg(DomNode node, CssComputed style, int contentX, int contentRight, ref int y, Ctx ctx)
    {
        y += style.MarginT;
        int sw = style.HasW && style.W > 0 ? style.W : ParseInt(node.GetAttr("width"), 0);
        int sh = style.HasH && style.H > 0 ? style.H : ParseInt(node.GetAttr("height"), 0);
        if (sw <= 0 || sh <= 0)
        {
            Svg.ParseViewBox(node.GetAttr("viewBox"), out _, out _, out float vbw, out float vbh);
            if (sw <= 0) sw = vbw > 0 ? (int)Math.Ceiling(vbw) : 24;
            if (sh <= 0) sh = vbh > 0 ? (int)Math.Ceiling(vbh) : 24;
        }
        if (sw > 560) sw = 560;
        if (sh > 560) sh = 560;
        if (sw < 1) sw = 1;
        if (sh < 1) sh = 1;

        uint currentColor = style.HasColor ? style.Color : 0xFFFFFFFF;
        string key = "__svg_node_" + node.GetHashCode() + "_" + sw + "_" + sh;
        Image img;
        if (!ctx.Images.TryGetValue(key, out img))
        {
            img = Svg.FromDom(node, sw, sh, currentColor);
            ctx.Images[key] = img;
        }

        int imgX = contentX;
        if (contentRight - contentX > sw)
        {
            if (style.TextAlign == CssAlign.Center || (style.MarginLAuto && style.MarginRAuto))
                imgX = contentX + (contentRight - contentX - sw) / 2;
            else if (style.TextAlign == CssAlign.Right || style.MarginLAuto)
                imgX = contentRight - sw;
        }

        RenderLine line = new RenderLine
        {
            Y = y,
            Height = sh,
            IsImage = true,
            ImageW = sw,
            ImageH = sh,
            ImageAlt = node.GetAttr("aria-label") ?? "",
            Image = img,
            TextX = imgX,
            Node = node,
            Hidden = style.Visibility != CssVis.Visible,
        };
        ctx.Plan.Lines.Add(line);
        y += sh + style.MarginB;
    }

    private static void EmitCanvas(DomNode node, CssComputed style, int contentX, int contentRight, ref int y, Ctx ctx)
    {
        y += style.MarginT;
        int cw = style.HasW && style.W > 0 ? style.W : ParseInt(node.GetAttr("width"), 300);
        int ch = style.HasH && style.H > 0 ? style.H : ParseInt(node.GetAttr("height"), 150);
        if (cw < 1) cw = 1; if (ch < 1) ch = 1;
        if (node.CanvasSurface == null)
        {
            node.CanvasSurface = new Cosmos.Kernel.System.Graphics.Bitmap(cw, ch, Cosmos.Kernel.System.Graphics.ColorDepth.ColorDepth32);
        }
        string key = "__canvas_node_" + node.GetHashCode() + "_" + cw + "_" + ch;
        ctx.Images[key] = node.CanvasSurface;

        int imgX = contentX;
        if (contentRight - contentX > cw)
        {
            if (style.TextAlign == CssAlign.Center || (style.MarginLAuto && style.MarginRAuto))
                imgX = contentX + (contentRight - contentX - cw) / 2;
            else if (style.TextAlign == CssAlign.Right || style.MarginLAuto)
                imgX = contentRight - cw;
        }

        RenderLine line = new RenderLine
        {
            Y = y,
            Height = ch,
            IsImage = true,
            ImageW = cw,
            ImageH = ch,
            ImageAlt = node.GetAttr("aria-label") ?? "canvas",
            Image = node.CanvasSurface,
            TextX = imgX,
            Node = node,
            Hidden = style.Visibility != CssVis.Visible,
        };
        ctx.Plan.Lines.Add(line);
        y += ch + style.MarginB;
    }


    private static void EmitVideo(DomNode node, CssComputed style, int contentX, int contentRight, ref int y, Ctx ctx)
    {
        y += style.MarginT;
        int vw = style.HasW && style.W > 0 ? style.W : ParseInt(node.GetAttr("width"), 320);
        int vh = style.HasH && style.H > 0 ? style.H : ParseInt(node.GetAttr("height"), 180);
        if (vw < 40) vw = 40; if (vh < 30) vh = 30;

        int vx = contentX + style.MarginL;
        if (contentRight - contentX > vw)
        {
            if (style.TextAlign == CssAlign.Center || (style.MarginLAuto && style.MarginRAuto))
                vx = contentX + (contentRight - contentX - vw) / 2;
            else if (style.TextAlign == CssAlign.Right || style.MarginLAuto)
                vx = contentRight - vw;
        }

        string videoSrc = node.GetAttr("src");
        if (string.IsNullOrEmpty(videoSrc))
        {
            for (DomNode ch = node.FirstChild; ch != null; ch = ch.Next)
            {
                if (ch.Tag == "source")
                {
                    videoSrc = ch.GetAttr("src");
                    if (!string.IsNullOrEmpty(videoSrc)) break;
                }
            }
        }
        string mediaLink = !string.IsNullOrEmpty(videoSrc) ? ("media:" + videoSrc) : null;

        // Main video player screen box
        ctx.Plan.Boxes.Add(new RenderBox
        {
            X = vx,
            Y = y,
            W = vw,
            H = vh,
            HasBg = true,
            Bg = Color.FromArgb(16, 18, 24),
            BorderRadius = 6,
            BorderT = 1, BorderB = 1, BorderL = 1, BorderR = 1,
            BorderCT = Color.FromArgb(60, 60, 60),
            BorderCB = Color.FromArgb(60, 60, 60),
            BorderCL = Color.FromArgb(60, 60, 60),
            BorderCR = Color.FromArgb(60, 60, 60),
            StyleT = CssBorder.Solid, StyleB = CssBorder.Solid, StyleL = CssBorder.Solid, StyleR = CssBorder.Solid,
            Node = node,
            Hidden = style.Visibility != CssVis.Visible
        });

        // Bottom control strip
        int barH = Math.Min(28, vh / 4);
        int barY = y + vh - barH;
        ctx.Plan.Boxes.Add(new RenderBox
        {
            X = vx,
            Y = barY,
            W = vw,
            H = barH,
            HasBg = true,
            Bg = Color.FromArgb(28, 30, 38),
            BorderRadius = 0,
            BorderT = 1,
            BorderCT = Color.FromArgb(50, 52, 60),
            StyleT = CssBorder.Solid,
            Node = node,
            Hidden = style.Visibility != CssVis.Visible
        });

        // Central Play button
        int lineCenterY = y + (vh - barH) / 2 - 8;
        RenderLine centerLine = new RenderLine { Y = lineCenterY, Height = 18, TextX = vx + vw / 2 - 32 };
        centerLine.Spans.Add(new RenderSpan { Text = "[ Play Video ]", Color = Color.White, Href = mediaLink, Bold = true });
        ctx.Plan.Lines.Add(centerLine);

        // Control bar text (Play icon + time + WMP badge)
        RenderLine barLine = new RenderLine { Y = barY + 5, Height = 14, TextX = vx + 8 };
        barLine.Spans.Add(new RenderSpan { Text = "0:00 / 0:00", Color = Color.FromArgb(200, 200, 200), Href = mediaLink });
        barLine.Spans.Add(new RenderSpan { Text = "  [Open in Media Player]", Color = Color.FromArgb(0, 160, 255), Href = mediaLink, Bold = true });
        ctx.Plan.Lines.Add(barLine);

        y += vh + style.MarginB;
    }

    private static void EmitAudio(DomNode node, CssComputed style, int contentX, int contentRight, ref int y, Ctx ctx)
    {
        y += style.MarginT;
        int aw = style.HasW && style.W > 0 ? style.W : 300;
        int ah = 32;
        int ax = contentX + style.MarginL;

        string audioSrc = node.GetAttr("src");
        if (string.IsNullOrEmpty(audioSrc))
        {
            for (DomNode ch = node.FirstChild; ch != null; ch = ch.Next)
            {
                if (ch.Tag == "source")
                {
                    audioSrc = ch.GetAttr("src");
                    if (!string.IsNullOrEmpty(audioSrc)) break;
                }
            }
        }
        string mediaLink = !string.IsNullOrEmpty(audioSrc) ? ("media:" + audioSrc) : null;

        ctx.Plan.Boxes.Add(new RenderBox
        {
            X = ax,
            Y = y,
            W = aw,
            H = ah,
            HasBg = true,
            Bg = Color.FromArgb(32, 34, 42),
            BorderRadius = 4,
            BorderT = 1, BorderB = 1, BorderL = 1, BorderR = 1,
            BorderCT = Color.FromArgb(70, 70, 70),
            BorderCB = Color.FromArgb(70, 70, 70),
            BorderCL = Color.FromArgb(70, 70, 70),
            BorderCR = Color.FromArgb(70, 70, 70),
            StyleT = CssBorder.Solid, StyleB = CssBorder.Solid, StyleL = CssBorder.Solid, StyleR = CssBorder.Solid,
            Node = node,
            Hidden = style.Visibility != CssVis.Visible
        });

        RenderLine barLine = new RenderLine { Y = y + 7, Height = 16, TextX = ax + 10 };
        barLine.Spans.Add(new RenderSpan { Text = "Audio Player", Color = Color.White, Href = mediaLink, Bold = true });
        barLine.Spans.Add(new RenderSpan { Text = "   [Play in Media Player]", Color = Color.FromArgb(0, 180, 255), Href = mediaLink });
        ctx.Plan.Lines.Add(barLine);

        y += ah + style.MarginB;
    }

    private static void EmitIframe(DomNode node, CssComputed style, int contentX, int contentRight, ref int y, Ctx ctx)
    {
        y += style.MarginT;
        int iw = style.HasW && style.W > 0 ? style.W : ParseInt(node.GetAttr("width"), 300);
        int ih = style.HasH && style.H > 0 ? style.H : ParseInt(node.GetAttr("height"), 150);
        if (iw < 1) iw = 1; if (ih < 1) ih = 1;
        int by = y;
        ctx.Plan.Boxes.Add(new RenderBox
        {
            X = contentX + style.MarginL,
            Y = by,
            W = iw,
            H = ih,
            HasBg = true,
            Bg = Color.White,
            BorderT = 1, BorderB = 1, BorderL = 1, BorderR = 1,
            BorderCT = Color.FromArgb(180, 180, 180),
            BorderCB = Color.FromArgb(180, 180, 180),
            BorderCL = Color.FromArgb(180, 180, 180),
            BorderCR = Color.FromArgb(180, 180, 180),
            StyleT = CssBorder.Solid, StyleB = CssBorder.Solid, StyleL = CssBorder.Solid, StyleR = CssBorder.Solid,
            Node = node,
            Hidden = style.Visibility != CssVis.Visible,
        });
        string srcdoc = node.GetAttr("srcdoc");
        if (!string.IsNullOrEmpty(srcdoc))
        {
            DomNode subDoc = HtmlDom.Parse(srcdoc);
            if (subDoc != null)
            {
                int subY = by + 2;
                LayoutContainer(subDoc, style, style, contentX + style.MarginL + 2, contentX + style.MarginL + iw - 2, ref subY, ctx);
            }
        }
        y += ih + style.MarginB;
    }

    private static void EmitRule(CssComputed style, int x, int right, ref int y, Ctx ctx)
    {
        y += style.MarginT;
        RenderLine rule = new RenderLine
        {
            Y = y + 4,
            Height = Math.Max(2, style.BorderTW > 0 ? style.BorderTW : 2),
            IsRule = true,
            TextX = x,
            RuleColor = style.HasBg ? ToColor(style.BgColor, 1.0f) : Color.FromArgb(187, 187, 187),
            Hidden = style.Visibility != CssVis.Visible,
        };
        ctx.Plan.Lines.Add(rule);
        y = rule.Y + rule.Height + 4 + style.MarginB;
    }

    private struct GridTrack
    {
        public int Width;
    }

    private static List<GridTrack> ParseGridTracks(string def, int availW, int gap)
    {
        List<GridTrack> tracks = new List<GridTrack>();
        if (string.IsNullOrEmpty(def)) return tracks;

        string s = def.Trim();
        int repIdx = s.IndexOf("repeat(", StringComparison.OrdinalIgnoreCase);
        while (repIdx >= 0)
        {
            int closeParen = s.IndexOf(')', repIdx);
            if (closeParen > repIdx)
            {
                string inner = s.Substring(repIdx + 7, closeParen - repIdx - 7);
                int comma = inner.IndexOf(',');
                if (comma > 0)
                {
                    string countStr = inner.Substring(0, comma).Trim();
                    string unitStr = inner.Substring(comma + 1).Trim();
                    int count = 1;
                    if (int.TryParse(countStr, out int pc)) count = Math.Max(1, Math.Min(12, pc));
                    StringBuilder repSb = new StringBuilder();
                    for (int k = 0; k < count; k++)
                    {
                        if (k > 0) repSb.Append(' ');
                        repSb.Append(unitStr);
                    }
                    s = s.Substring(0, repIdx) + repSb.ToString() + s.Substring(closeParen + 1);
                }
            }
            repIdx = s.IndexOf("repeat(", StringComparison.OrdinalIgnoreCase);
        }

        string[] tokens = s.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return tracks;

        int totalGap = Math.Max(0, (tokens.Length - 1) * gap);
        int remainingW = Math.Max(0, availW - totalGap);
        float totalFr = 0;
        int fixedSum = 0;
        float[] frs = new float[tokens.Length];
        int[] fixedW = new int[tokens.Length];

        for (int i = 0; i < tokens.Length; i++)
        {
            string t = tokens[i].Trim();
            if (t.EndsWith("fr", StringComparison.OrdinalIgnoreCase))
            {
                string num = t.Substring(0, t.Length - 2).Trim();
                float frVal = 1f;
                if (float.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pf)) frVal = pf;
                frs[i] = frVal;
                totalFr += frVal;
            }
            else if (t.EndsWith("%"))
            {
                string num = t.Substring(0, t.Length - 1).Trim();
                if (float.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pct))
                {
                    int w = (int)(availW * (pct / 100f));
                    fixedW[i] = w;
                    fixedSum += w;
                }
            }
            else if (t.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                string num = t.Substring(0, t.Length - 2).Trim();
                if (int.TryParse(num, out int px))
                {
                    fixedW[i] = px;
                    fixedSum += px;
                }
            }
            else if (t == "auto")
            {
                frs[i] = 1f;
                totalFr += 1f;
            }
            else if (int.TryParse(t, out int px))
            {
                fixedW[i] = px;
                fixedSum += px;
            }
        }

        int frSpace = Math.Max(0, remainingW - fixedSum);

        for (int i = 0; i < tokens.Length; i++)
        {
            int w = fixedW[i];
            if (frs[i] > 0 && totalFr > 0)
            {
                w = (int)(frSpace * (frs[i] / totalFr));
            }
            if (w < 1) w = 1;
            tracks.Add(new GridTrack { Width = w });
        }

        return tracks;
    }

    private static void LayoutGrid(DomNode container, CssComputed style, CssComputed containerStyle,
        int contentX, int contentRight, ref int y, Ctx ctx)
    {
        List<DomNode> items = new List<DomNode>();
        DomNode ch = container.FirstChild;
        while (ch != null)
        {
            if (ch.IsElement)
            {
                CssComputed cs = ctx.Sheet.Compute(ch, style, ctx.Vw, ctx.Vh);
                if (cs.Display != CssDisplay.None) items.Add(ch);
            }
            ch = ch.Next;
        }

        if (items.Count == 0) return;

        int availW = Math.Max(1, contentRight - contentX);
        int colGap = style.GapCol;
        int rowGap = style.GapRow;

        List<GridTrack> colTracks = ParseGridTracks(style.GridTemplateColumns, availW, colGap);
        if (colTracks.Count == 0)
        {
            colTracks.Add(new GridTrack { Width = availW });
        }

        int numCols = colTracks.Count;
        int curCol = 0;
        int rowTop = y;
        int maxRowH = 0;

        for (int i = 0; i < items.Count; i++)
        {
            DomNode item = items[i];
            CssComputed cs = ctx.Sheet.Compute(item, style, ctx.Vw, ctx.Vh);
            if (style.JustifyContent == CssJustify.Center && cs.TextAlign == CssAlign.Left)
            {
                cs.TextAlign = CssAlign.Center;
            }

            int cellX = contentX;
            for (int c = 0; c < curCol; c++) cellX += colTracks[c].Width + colGap;
            int cellW = colTracks[curCol].Width;

            int itemY = rowTop;
            LayoutBlockNode(item, cs, cellX, cellX + cellW, ref itemY, ctx, 0);
            int itemH = itemY - rowTop;
            if (itemH > maxRowH) maxRowH = itemH;

            curCol++;
            if (curCol >= numCols)
            {
                curCol = 0;
                rowTop += maxRowH + rowGap;
                maxRowH = 0;
            }
        }

        if (curCol > 0)
        {
            rowTop += maxRowH;
        }
        else if (rowTop > y)
        {
            rowTop -= rowGap;
        }

        y = rowTop;
    }

    private static void AddImageRun(DomNode child, CssComputed cs, List<Run> runs, Ctx ctx, string href)
    {
        int iw = cs.HasW && cs.W > 0 ? cs.W : ParseInt(child.GetAttr("width"), 0);
        int ih = cs.HasH && cs.H > 0 ? cs.H : ParseInt(child.GetAttr("height"), 0);
        runs.Add(new Run
        {
            IsImage = true,
            ImageSrc = child.GetAttr("src"),
            ImageAlt = child.GetAttr("alt"),
            ImageW = iw,
            ImageH = ih,
            Style = cs,
            Href = href,
            Node = child,
        });
    }

    private static void AddSvgRun(DomNode child, CssComputed cs, CssComputed parentStyle, List<Run> runs, Ctx ctx, string href)
    {
        int sw = cs.HasW && cs.W > 0 ? cs.W : ParseInt(child.GetAttr("width"), 0);
        int sh = cs.HasH && cs.H > 0 ? cs.H : ParseInt(child.GetAttr("height"), 0);
        if (sw <= 0 || sh <= 0)
        {
            Svg.ParseViewBox(child.GetAttr("viewBox"), out _, out _, out float vbw, out float vbh);
            if (sw <= 0) sw = vbw > 0 ? (int)Math.Ceiling(vbw) : 24;
            if (sh <= 0) sh = vbh > 0 ? (int)Math.Ceiling(vbh) : 24;
        }
        if (sw > 560) sw = 560;
        if (sh > 560) sh = 560;
        if (sw < 1) sw = 1;
        if (sh < 1) sh = 1;

        uint currentColor = cs.HasColor ? cs.Color : (parentStyle != null && parentStyle.HasColor ? parentStyle.Color : 0xFFFFFFFF);
        string key = "__svg_node_" + child.GetHashCode() + "_" + sw + "_" + sh;
        if (!ctx.Images.ContainsKey(key))
        {
            Image svgImg = Svg.FromDom(child, sw, sh, currentColor);
            ctx.Images[key] = svgImg;
        }

        runs.Add(new Run
        {
            IsImage = true,
            ImageSrc = key,
            ImageAlt = child.GetAttr("aria-label") ?? "",
            ImageW = sw,
            ImageH = sh,
            Style = cs,
            Href = href,
            Node = child,
        });
    }

    private static void AddCanvasRun(DomNode child, CssComputed cs, List<Run> runs, Ctx ctx, string href)
    {
        int cw = cs.HasW && cs.W > 0 ? cs.W : ParseInt(child.GetAttr("width"), 300);
        int ch = cs.HasH && cs.H > 0 ? cs.H : ParseInt(child.GetAttr("height"), 150);
        if (cw < 1) cw = 1; if (ch < 1) ch = 1;
        if (child.CanvasSurface == null)
        {
            child.CanvasSurface = new Cosmos.Kernel.System.Graphics.Bitmap(cw, ch, Cosmos.Kernel.System.Graphics.ColorDepth.ColorDepth32);
        }
        string key = "__canvas_node_" + child.GetHashCode() + "_" + cw + "_" + ch;
        ctx.Images[key] = child.CanvasSurface;

        runs.Add(new Run
        {
            IsImage = true,
            ImageSrc = key,
            ImageAlt = child.GetAttr("aria-label") ?? "canvas",
            ImageW = cw,
            ImageH = ch,
            Style = cs,
            Href = href,
            Node = child,
        });
    }

    // ---- inline collection -----------------------------------------------

    private static void CollectInline(DomNode node, CssComputed style, List<Run> runs, Ctx ctx, string href)
    {
        // ::before content
        AddPseudo(runs, node, style, "before", ctx);

        DomNode child = node.FirstChild;
        while (child != null)
        {
            if (child.NodeType == DomNode.TextNode)
            {
                AddTextRuns(runs, child.Text, style, href, node);
            }
            else if (child.IsElement)
            {
                string tag = child.Tag;
                CssComputed cs = ctx.Sheet.Compute(child, style, ctx.Vw, ctx.Vh);
                if (cs.Display == CssDisplay.None) { child = child.Next; continue; }

                if ((cs.PadL > 0 || cs.MarginL > 0) && runs.Count > 0 && !runs[runs.Count - 1].Break)
                {
                    runs.Add(new Run { Text = " ", Style = cs, Href = href, Node = child });
                }

                if (!cs.HasBg && style.HasBg)
                {
                    cs.HasBg = true;
                    cs.BgColor = style.BgColor;
                    cs.PadL = Math.Max(cs.PadL, style.PadL);
                    cs.PadR = Math.Max(cs.PadR, style.PadR);
                    cs.PadT = Math.Max(cs.PadT, style.PadT);
                    cs.PadB = Math.Max(cs.PadB, style.PadB);
                    cs.BorderRadius = Math.Max(cs.BorderRadius, style.BorderRadius);
                }

                if (tag == "br") { runs.Add(new Run { Break = true }); }
                else if (IsWidgetTag(tag))
                {
                    byte kind = WidgetKindOf(child);
                    if (kind != WidgetKind.NotAWidget)
                    {
                        int ww, wh;
                        MeasureWidget(child, kind, cs, ctx, out ww, out wh);
                        runs.Add(new Run
                        {
                            IsWidget = true,
                            WidgetKind = kind,
                            WidgetW = ww,
                            WidgetH = wh,
                            Style = cs,
                            Node = child,
                        });
                    }
                }
                else if (tag == "img")
                {
                    AddImageRun(child, cs, runs, ctx, href);
                }
                else if (tag == "svg")
                {
                    AddSvgRun(child, cs, style, runs, ctx, href);
                }
                else if (tag == "canvas")
                {
                    AddCanvasRun(child, cs, runs, ctx, href);
                }
                else if (tag == "a")
                {
                    CollectInline(child, cs, runs, ctx, child.GetAttr("href"));
                }
                else if (cs.Display == CssDisplay.Contents)
                {
                    CollectInline(child, cs, runs, ctx, href);
                }
                else if (IsBlockLevel(cs))
                {
                    // Block inside inline context: contribute its text inline (flatten).
                    CollectInline(child, cs, runs, ctx, href);
                }
                else
                {
                    CollectInline(child, cs, runs, ctx, href);
                }
            }
            child = child.Next;
        }

        AddPseudo(runs, node, style, "after", ctx);
    }

    private static void AddPseudo(List<Run> runs, DomNode el, CssComputed elStyle, string pseudo, Ctx ctx)
    {
        string content;
        CssComputed ps = ctx.Sheet.ComputePseudo(el, elStyle, pseudo, ctx.Vw, ctx.Vh, out content);
        if (ps == null || content == null) return;
        AddTextRuns(runs, content, ps, null, el);
    }

    private static void AddTextRuns(List<Run> runs, string text, CssComputed style, string href, DomNode node)
    {
        if (string.IsNullOrEmpty(text)) return;
        CssWhite ws = style.WhiteSpace;
        if (ws == CssWhite.Pre || ws == CssWhite.PreWrap)
        {
            // preserve whitespace; split on newlines into break markers
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) runs.Add(new Run { Break = true });
                if (lines[i].Length > 0) runs.Add(new Run { Text = lines[i], Style = style, Href = href, Node = node });
            }
        }
        else if (ws == CssWhite.PreLine)
        {
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) runs.Add(new Run { Break = true });
                string collapsed = CollapseWs(lines[i]);
                if (collapsed.Length > 0) runs.Add(new Run { Text = collapsed, Style = style, Href = href, Node = node });
            }
        }
        else
        {
            // Normal / nowrap: collapse whitespace runs to single spaces.
            string collapsed = CollapseWs(text);
            if (collapsed.Length > 0) runs.Add(new Run { Text = collapsed, Style = style, Href = href, Node = node });
        }
    }

    // ---- flush + wrap a run list into lines ------------------------------

    private static void FlushRuns(List<Run> runs, CssComputed baseStyle, int contentX, int contentRight,
        ref int y, Ctx ctx, string bullet, int bulletX)
    {
        if (runs.Count == 0)
        {
            if (bullet != null)
            {
                // empty list item: still reserve a line for the bullet
                RenderLine empty = new RenderLine { Y = y, Height = LineHeightOf(baseStyle), TextX = contentX, Bullet = bullet, BulletX = bulletX };
                ctx.Plan.Lines.Add(empty);
                y += empty.Height;
            }
            return;
        }

        // tokenize
        List<Tok> toks = new List<Tok>();
        for (int i = 0; i < runs.Count; i++)
        {
            Run r = runs[i];
            if (r.Break) { toks.Add(new Tok { Break = true, Style = baseStyle }); continue; }
            if (r.IsImage)
            {
                toks.Add(new Tok { IsImage = true, ImageSrc = r.ImageSrc, ImageAlt = r.ImageAlt, ImageW = r.ImageW, ImageH = r.ImageH, Style = r.Style, Href = r.Href, Node = r.Node });
                continue;
            }
            if (r.IsWidget)
            {
                toks.Add(new Tok { IsWidget = true, WidgetKind = r.WidgetKind, WidgetW = r.WidgetW, WidgetH = r.WidgetH, Style = r.Style, Href = r.Href, Node = r.Node });
                continue;
            }
            TokenizeText(toks, r.Text, r.Style, r.Href, r.Node);
        }
        runs.Clear();

        int align = (int)baseStyle.TextAlign;
        bool nowrap = baseStyle.WhiteSpace == CssWhite.Nowrap;

        int curX = contentX + (bullet != null ? Math.Max(18, ctx.Measure(bullet, baseStyle.FontSize) + 8) : 0);
        int lineTop = y;
        List<RenderSpan> spans = new List<RenderSpan>();
        int lineH = LineHeightOf(baseStyle);
        bool hasContent = false;
        bool firstLine = true;
        string curBullet = bullet;
        int curBulletX = bulletX;
        int lineWidStart = ctx.Plan.Widgets.Count;

        for (int t = 0; t < toks.Count; t++)
        {
            Tok tok = toks[t];

            if (tok.Break)
            {
                EmitLine(ctx, spans, lineTop, lineH, contentX, curBullet, curBulletX, align, contentRight - contentX, lineWidStart);
                lineTop += lineH;
                spans = new List<RenderSpan>();
                curX = contentX;
                hasContent = false;
                firstLine = false;
                curBullet = null;
                lineWidStart = ctx.Plan.Widgets.Count;
                continue;
            }

            if (tok.IsImage)
            {
                string abs = tok.ImageSrc != null ? WebUrl.Resolve(ctx.BaseHref, tok.ImageSrc) : null;
                Image img = null;
                if (tok.ImageSrc != null && ctx.Images.ContainsKey(tok.ImageSrc)) ctx.Images.TryGetValue(tok.ImageSrc, out img);
                else if (abs != null && ctx.Images.ContainsKey(abs)) ctx.Images.TryGetValue(abs, out img);

                int natW = img != null && img.Width > 0 ? img.Width : 0;
                int natH = img != null && img.Height > 0 ? img.Height : 0;
                int iw = tok.ImageW, ih = tok.ImageH;
                if (iw <= 0 && tok.Style != null && tok.Style.HasW) iw = tok.Style.W;
                if (ih <= 0 && tok.Style != null && tok.Style.HasH) ih = tok.Style.H;

                if (iw > 0 && ih > 0) { }
                else if (iw > 0) { ih = natH > 0 && natW > 0 ? natH * iw / natW : iw; }
                else if (ih > 0) { iw = natW > 0 && natH > 0 ? natW * ih / natH : ih; }
                else if (natW > 0 && natH > 0) { iw = natW; ih = natH; }
                else { iw = 32; ih = 32; }

                int maxW = Math.Max(20, contentRight - contentX);
                if (iw > maxW) { ih = Math.Max(1, ih * maxW / Math.Max(1, iw)); iw = maxW; }
                if (iw < 1) iw = 1;
                if (ih < 1) ih = 1;

                if (!nowrap && hasContent && curX + iw > contentRight)
                {
                    EmitLine(ctx, spans, lineTop, lineH, contentX, curBullet, curBulletX, align, contentRight - contentX, lineWidStart);
                    lineTop += lineH;
                    spans = new List<RenderSpan>();
                    curX = contentX;
                    hasContent = false;
                    firstLine = false;
                    curBullet = null;
                    lineH = LineHeightOf(baseStyle);
                    lineWidStart = ctx.Plan.Widgets.Count;
                }

                if (ih > lineH) lineH = ih;

                spans.Add(new RenderSpan
                {
                    IsImage = true,
                    Image = img,
                    ImageAlt = tok.ImageAlt ?? "",
                    X = curX,
                    Y = lineTop,
                    YOff = Math.Max(0, (lineH - ih) / 2),
                    Width = iw,
                    Height = ih,
                    Href = tok.Href,
                    Node = tok.Node,
                    Hidden = tok.Style != null && tok.Style.Visibility != CssVis.Visible,
                });

                curX += iw + 4;
                hasContent = true;
                continue;
            }

            if (tok.IsWidget)
            {
                // form controls flow inline (inline-block): wrap like a word,
                // grow the line when the control is taller than the text.
                int ww = tok.WidgetW, wh = tok.WidgetH;
                if (!nowrap && hasContent && curX + ww > contentRight)
                {
                    EmitLine(ctx, spans, lineTop, lineH, contentX, curBullet, curBulletX, align, contentRight - contentX, lineWidStart);
                    lineTop += lineH;
                    spans = new List<RenderSpan>();
                    curX = contentX;
                    hasContent = false;
                    firstLine = false;
                    curBullet = null;
                    lineH = LineHeightOf(baseStyle);
                    lineWidStart = ctx.Plan.Widgets.Count;
                }
                if (wh > lineH) lineH = wh;
                EmitWidget(ctx, tok, curX, lineTop, lineH);
                curX += ww + 4;
                hasContent = true;
                continue;
            }

            if (tok.Space)
            {
                if (!hasContent) continue;
                curX += ctx.Measure(" ", tok.Style.FontSize) + tok.Style.LetterSpacing;
                continue;
            }

            CssComputed st = tok.Style;
            int size = st.FontSize > 0 ? st.FontSize : baseStyle.FontSize;
            int w = ctx.Measure(tok.Text, size) + Math.Max(0, tok.Text.Length - 1) * st.LetterSpacing + (st.FontWeight >= 600 ? 1 : 0);
            if (st.PadL > 0 || st.PadR > 0) w += st.PadL + st.PadR;
            int lh = LineHeightOf(st);
            if (st.PadT > 0 || st.PadB > 0) lh += st.PadT + st.PadB;
            if (lh > lineH) lineH = lh;

            if (!nowrap && hasContent && curX + w > contentRight)
            {
                EmitLine(ctx, spans, lineTop, lineH, contentX, curBullet, curBulletX, align, contentRight - contentX, lineWidStart);
                lineTop += lineH;
                spans = new List<RenderSpan>();
                curX = contentX;
                hasContent = false;
                firstLine = false;
                curBullet = null;
                lineH = lh;
                lineWidStart = ctx.Plan.Widgets.Count;
            }

            // hard-break a single word wider than the line
            bool allowBreakWord = st.WordBreak == "break-all" || (st.WordBreak == "break-word" && w > contentRight - curX) || tok.Text.Length > 25;
            if (!hasContent && w > contentRight - curX && tok.Text.Length > 1 && !nowrap && allowBreakWord && (contentRight - curX >= 20))
            {
                int cut = tok.Text.Length;
                while (cut > 1)
                {
                    int cw = ctx.Measure(tok.Text.Substring(0, cut), size);
                    if (cw + curX <= contentRight) break;
                    cut--;
                }
                string head = tok.Text.Substring(0, cut);
                string tail = tok.Text.Substring(cut);
                AddSpan(ctx, spans, head, st, tok.Href, curX, lineTop, lineH, tok.Node);
                EmitLine(ctx, spans, lineTop, lineH, contentX, curBullet, curBulletX, align, contentRight - contentX, lineWidStart);
                lineTop += lineH;
                spans = new List<RenderSpan>();
                curX = contentX;
                hasContent = false;
                firstLine = false;
                curBullet = null;
                lineH = lh;
                lineWidStart = ctx.Plan.Widgets.Count;
                Tok rem = new Tok { Text = tail, Style = st, Href = tok.Href, Node = tok.Node };
                toks[t] = rem;
                t--;
                continue;
            }

            AddSpan(ctx, spans, tok.Text, st, tok.Href, curX, lineTop, lineH, tok.Node);
            curX += w;
            hasContent = true;
        }

        EmitLine(ctx, spans, lineTop, lineH, contentX, curBullet, curBulletX, align, contentRight - contentX, lineWidStart);
        y = lineTop + lineH;
    }

    private static void TokenizeText(List<Tok> toks, string text, CssComputed style, string href, DomNode node)
    {
        if (text == null) return;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == ' ')
            {
                int start = i;
                while (i < text.Length && text[i] == ' ') i++;
                // cross-run whitespace collapse: skip if the previous token is already a space
                if (toks.Count > 0 && toks[toks.Count - 1].Space) continue;
                toks.Add(new Tok { Text = " ", Space = true, Style = style, Href = href, Node = node });
            }
            else
            {
                int start = i;
                while (i < text.Length && text[i] != ' ') i++;
                toks.Add(new Tok { Text = text.Substring(start, i - start), Style = style, Href = href, Node = node });
            }
        }
    }

    private static void AddSpan(Ctx ctx, List<RenderSpan> spans, string text, CssComputed st, string href, int x, int y, int height, DomNode node)
    {
        int size = st.FontSize > 0 ? st.FontSize : 13;
        int padL = st.PadL, padR = st.PadR;
        int padT = st.PadT, padB = st.PadB;
        int w = ctx.Measure(text, size) + Math.Max(0, text.Length - 1) * st.LetterSpacing + (st.FontWeight >= 600 ? 1 : 0);
        int totalW = w + padL + padR;
        int yOff = 0;
        if (st.VerticalAlign == CssVAlign.Super) yOff = -Math.Max(2, size / 3);
        else if (st.VerticalAlign == CssVAlign.Sub) yOff = Math.Max(2, size / 3);

        uint col = st.HasColor ? st.Color : 0xFF000000;
        spans.Add(new RenderSpan
        {
            Text = text,
            Bold = st.FontWeight >= 600,
            Italic = st.Italic,
            Underline = st.Underline,
            LineThrough = st.LineThrough,
            Overline = st.Overline,
            Href = href,
            Color = ToColor(col, st.Opacity),
            Bg = ToColor(st.BgColor, 1.0f),
            HasBg = st.HasBg,
            Hidden = st.Visibility != CssVis.Visible,
            Size = size,
            X = x,
            Y = y,
            YOff = yOff,
            Width = totalW,
            Height = height,
            PadL = padL,
            PadR = padR,
            PadT = padT,
            PadB = padB,
            BorderRadius = st.BorderRadius,
            Node = node,
        });
    }

    private static int EmitInlineImage(Tok tok, int x, int y, int availW, int align, Ctx ctx)
    {
        string abs = tok.ImageSrc != null ? WebUrl.Resolve(ctx.BaseHref, tok.ImageSrc) : null;
        Image img = null;
        if (abs != null && ctx.Images.ContainsKey(abs)) ctx.Images.TryGetValue(abs, out img);
        else if (tok.ImageSrc != null && ctx.Images.ContainsKey(tok.ImageSrc)) ctx.Images.TryGetValue(tok.ImageSrc, out img);
        int natW = img != null && img.Width > 0 ? img.Width : 0;
        int natH = img != null && img.Height > 0 ? img.Height : 0;
        int w, h;
        if (tok.ImageW > 0 && tok.ImageH > 0) { w = tok.ImageW; h = tok.ImageH; }
        else if (tok.ImageW > 0) { w = tok.ImageW; h = natH > 0 && natW > 0 ? natH * w / natW : tok.ImageW; }
        else if (tok.ImageH > 0) { h = tok.ImageH; w = natW > 0 && natH > 0 ? natW * h / natH : tok.ImageH; }
        else { w = natW > 0 ? natW : 48; h = natH > 0 ? natH : 48; }
        // Clamp to the available content width and a sane max height, preserving aspect ratio.
        int maxW = availW > 40 ? Math.Min(availW, 560) : 560;
        int maxH = 400;
        if (w > maxW) { h = h * maxW / Math.Max(1, w); w = maxW; }
        if (h > maxH) { w = w * maxH / Math.Max(1, h); h = maxH; }
        if (w < 16) w = 16;
        if (h < 16) h = 16;

        int imgX = x;
        if (!ctx.IsMeasuring && availW > w)
        {
            if (align == (int)CssAlign.Center || (tok.Style != null && tok.Style.TextAlign == CssAlign.Center))
                imgX = x + (availW - w) / 2;
            else if (align == (int)CssAlign.Right || (tok.Style != null && tok.Style.TextAlign == CssAlign.Right))
                imgX = x + (availW - w);
        }

        RenderLine line = new RenderLine
        {
            Y = y,
            Height = h,
            IsImage = true,
            ImageW = w,
            ImageH = h,
            ImageAlt = tok.ImageAlt ?? "",
            Image = img,
            TextX = imgX,
            Node = tok.Node,
        };
        ctx.Plan.Lines.Add(line);
        if (tok.Href != null)
        {
            ctx.Plan.Links.Add(new RenderPlan.LinkRect { Href = tok.Href, X = imgX, Y = y, W = w, H = h });
        }
        return h;
    }

    // ---- form-control widgets --------------------------------------------

    private static bool IsWidgetTag(string tag)
    {
        return tag == "input" || tag == "button" || tag == "select" || tag == "textarea";
    }

    /// <summary>Map an element to a widget kind (NotAWidget = don't render,
    /// e.g. hidden inputs, which still take part in form submission).</summary>
    private static byte WidgetKindOf(DomNode node)
    {
        if (node == null) return WidgetKind.NotAWidget;
        if (node.GetAttr("hidden") != null) return WidgetKind.NotAWidget;
        string tag = node.Tag;
        if (tag == "input")
        {
            string t = node.GetAttr("type");
            if (t == null || t.Length == 0) t = "text";
            t = t.ToLowerInvariant();
            if (t == "hidden") return WidgetKind.NotAWidget;
            if (t == "password") return WidgetKind.Password;
            if (t == "checkbox") return WidgetKind.Checkbox;
            if (t == "radio") return WidgetKind.Radio;
            if (t == "submit" || t == "button" || t == "reset" || t == "image" || t == "file")
                return WidgetKind.Button;
            return WidgetKind.Text;   // text, search, email, url, tel, number, ...
        }
        if (tag == "button") return WidgetKind.Button;
        if (tag == "textarea") return WidgetKind.Textarea;
        if (tag == "select") return WidgetKind.Select;
        return WidgetKind.NotAWidget;
    }

    /// <summary>Natural size of a control from its attributes and style.</summary>
    private static void MeasureWidget(DomNode node, byte kind, CssComputed st, Ctx ctx,
        out int w, out int h)
    {
        int size = st.FontSize > 0 ? st.FontSize : 13;
        int lh = LineHeightOf(st);
        int padY = Math.Max(size + 8, lh + 4);

        switch (kind)
        {
            case WidgetKind.Text:
            case WidgetKind.Password:
            {
                int cols = ParseInt(node.GetAttr("size"), 20);
                if (cols < 1) cols = 1;
                if (cols > 200) cols = 200;
                w = Math.Max(70, ctx.Measure(MakeFill(cols), size) + 10);
                h = padY;
                break;
            }
            case WidgetKind.Checkbox:
            case WidgetKind.Radio:
                w = 13; h = 13;
                break;
            case WidgetKind.Button:
            {
                string label = ButtonLabel(node);
                w = Math.Max(44, ctx.Measure(label, size) + 22);
                h = padY;
                break;
            }
            case WidgetKind.Textarea:
            {
                int cols = ParseInt(node.GetAttr("cols"), 20);
                if (cols < 1) cols = 1;
                if (cols > 200) cols = 200;
                int rows = ParseInt(node.GetAttr("rows"), 2);
                if (rows < 1) rows = 1;
                if (rows > 50) rows = 50;
                w = Math.Max(90, ctx.Measure(MakeFill(cols), size) + 10);
                h = Math.Max(size + 10, (lh + 2) * rows + 8);
                break;
            }
            case WidgetKind.Select:
            {
                string label = SelectedOptionLabel(node);
                w = Math.Max(90, ctx.Measure(label, size) + 34);
                h = padY;
                break;
            }
            default:
                w = 0; h = 0;
                break;
        }

        if (st != null)
        {
            if (st.HasW && st.W > 0) w = st.W;
            if (st.HasH && st.H > 0) h = st.H;
            if (st.MaxW < int.MaxValue && w > st.MaxW) w = st.MaxW;
            if (st.MinW > 0 && w < st.MinW) w = st.MinW;
            if (st.MaxH < int.MaxValue && h > st.MaxH) h = st.MaxH;
            if (st.MinH > 0 && h < st.MinH) h = st.MinH;
        }
    }

    private static string MakeFill(int n)
    {
        char[] buf = new char[n];
        for (int i = 0; i < n; i++) buf[i] = 'n';
        return new string(buf);
    }

    /// <summary>A button's visible label: its value attr, else its text.</summary>
    public static string ButtonLabel(DomNode node)
    {
        if (node.Tag == "input")
        {
            string v = node.GetAttr("value");
            if (v != null) return v;
            string t = node.GetAttr("type");
            if (t != null && t.Length > 0)
            {
                t = t.ToLowerInvariant();
                if (t == "submit") return "Submit";
                if (t == "reset") return "Reset";
                if (t == "button") return "Button";
            }
            return "Button";
        }
        string text = node.TextContent;
        if (string.IsNullOrEmpty(text)) return "Button";
        return CollapseWs(text).Trim();
    }

    /// <summary>The label of a select's current option (value attr, else text).</summary>
    public static string SelectedOptionLabel(DomNode node)
    {
        DomNode opt = SelectedOption(node);
        return opt != null ? OptionLabel(opt) : "";
    }

    /// <summary>The select's selected option element (selected attr, else first,
    /// else none).</summary>
    public static DomNode SelectedOption(DomNode select)
    {
        DomNode first = null;
        DomNode opt = select.FirstChild;
        while (opt != null)
        {
            if (opt.IsElement && opt.Tag == "option")
            {
                if (first == null) first = opt;
                if (opt.GetAttr("selected") != null) return opt;
            }
            opt = opt.Next;
        }
        return first;
    }

    public static string OptionLabel(DomNode opt)
    {
        string v = opt.GetAttr("value");
        if (v != null) return v;
        string t = opt.TextContent;
        return t != null ? CollapseWs(t).Trim() : "";
    }

    /// <summary>Emit a form control as a positioned widget on the plan.</summary>
    private static void EmitWidget(Ctx ctx, Tok tok, int x, int lineTop, int lineH)
    {
        DomNode node = tok.Node;
        if (node == null) return;
        byte kind = tok.WidgetKind;
        CssComputed st = tok.Style;
        int size = st.FontSize > 0 ? st.FontSize : 13;
        int w = tok.WidgetW, h = tok.WidgetH;

        string text;
        switch (kind)
        {
            case WidgetKind.Text:
            case WidgetKind.Password:
                text = node.GetAttr("value");
                if (text == null) text = "";
                break;
            case WidgetKind.Textarea:
                text = node.GetAttr("value");
                if (text == null) text = node.TextContent;
                if (text == null) text = "";
                break;
            case WidgetKind.Button:
                text = ButtonLabel(node);
                break;
            case WidgetKind.Select:
                text = SelectedOptionLabel(node);
                break;
            default:
                text = "";
                break;
        }

        RenderWidget rw = new RenderWidget();
        rw.Node = node;
        rw.Kind = kind;
        rw.W = w;
        rw.H = h;
        rw.X = x;
        rw.Y = lineTop + Math.Max(0, (lineH - h) / 2);
        rw.Text = text;
        rw.Checked = node.GetAttr("checked") != null;
        rw.Disabled = node.GetAttr("disabled") != null;
        rw.FontSize = size;
        rw.Hidden = st.Visibility != CssVis.Visible;

        int pad = (kind == WidgetKind.Checkbox || kind == WidgetKind.Radio) ? 0 : 5;
        rw.TextX = x + pad;
        rw.TextY = rw.Y + Math.Max(1, (h - size) / 2);
        rw.TextW = ctx.Measure(text, size);
        ctx.Plan.Widgets.Add(rw);
    }

    /// <summary>Block-level widget (display:block control): its own line.</summary>
    private static void EmitBlockWidget(DomNode node, CssComputed style, int x, ref int y, Ctx ctx)
    {
        byte kind = WidgetKindOf(node);
        if (kind == WidgetKind.NotAWidget) return;
        int w, h;
        MeasureWidget(node, kind, style, ctx, out w, out h);
        Tok tok = new Tok { IsWidget = true, WidgetKind = kind, WidgetW = w, WidgetH = h, Style = style, Node = node };
        EmitWidget(ctx, tok, x, y, Math.Max(h, LineHeightOf(style)));
        y += h + 4;
    }

    private static void EmitLine(Ctx ctx, List<RenderSpan> spans, int top, int height, int textX,
        string bullet, int bulletX, int align, int availWidth, int widgetStart = -1)
    {
        // horizontal alignment
        if (!ctx.IsMeasuring && (align == (int)CssAlign.Center || align == (int)CssAlign.Right))
        {
            int minX = int.MaxValue, maxX = int.MinValue;
            for (int i = 0; i < spans.Count; i++)
            {
                if (spans[i].X < minX) minX = spans[i].X;
                if (spans[i].X + spans[i].Width > maxX) maxX = spans[i].X + spans[i].Width;
            }
            if (widgetStart >= 0)
            {
                for (int i = widgetStart; i < ctx.Plan.Widgets.Count; i++)
                {
                    RenderWidget w = ctx.Plan.Widgets[i];
                    if (w.X < minX) minX = w.X;
                    if (w.X + w.W > maxX) maxX = w.X + w.W;
                }
            }
            if (maxX > minX)
            {
                int contentW = maxX - minX;
                int delta = align == (int)CssAlign.Center ? (availWidth - contentW) / 2 : (availWidth - contentW);
                if (delta > 0)
                {
                    for (int i = 0; i < spans.Count; i++) spans[i].X += delta;
                    if (widgetStart >= 0)
                    {
                        for (int i = widgetStart; i < ctx.Plan.Widgets.Count; i++)
                        {
                            ctx.Plan.Widgets[i].X += delta;
                            ctx.Plan.Widgets[i].TextX += delta;
                        }
                    }
                }
            }
        }

        RenderLine line = new RenderLine
        {
            Y = top,
            Height = height,
            TextX = textX,
            Bullet = bullet,
            BulletX = bulletX,
        };
        line.Spans = spans;
        ctx.Plan.Lines.Add(line);

        for (int i = 0; i < spans.Count; i++)
        {
            RenderSpan s = spans[i];
            if (s.Href != null && (s.IsImage || (s.Text != null && s.Text.Trim().Length > 0)))
            {
                ctx.Plan.Links.Add(new RenderPlan.LinkRect { Href = s.Href, X = s.X, Y = s.Y + s.YOff, W = s.Width, H = s.Height });
            }
        }
    }

    // ---- small helpers ---------------------------------------------------

    private static int LineHeightOf(CssComputed st)
    {
        int size = st.FontSize > 0 ? st.FontSize : 13;
        if (st.LineHeight > 0.01f) return Math.Max(size, (int)(st.LineHeight * size));
        return size + 6;
    }

    private static string CollapseWs(string s)
    {
        // Collapse each whitespace run to a single space; preserve leading/trailing
        // single spaces (the wrapper drops them at line starts). This keeps the
        // inter-word space between adjacent inline elements.
        StringBuilder sb = new StringBuilder(s.Length);
        bool lastSpace = false;
        bool any = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
            {
                if (!lastSpace) { sb.Append(' '); lastSpace = true; }
            }
            else { sb.Append(c); lastSpace = false; any = true; }
        }
        if (!any && sb.Length == 0 && s.Length > 0) return " "; // pure-whitespace node -> one space
        return sb.ToString();
    }

    private static int ParseInt(string s, int fallback)
    {
        if (string.IsNullOrEmpty(s)) return fallback;
        int result = 0; bool any = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c >= '0' && c <= '9') { result = result * 10 + (c - '0'); any = true; }
            else break;
        }
        return any ? result : fallback;
    }

    private static string ListMarker(CssListStyle type, bool ordered, int number)
    {
        if (ordered)
        {
            switch (type)
            {
                case CssListStyle.LowerAlpha: return Alpha(number, 'a') + ".";
                case CssListStyle.UpperAlpha: return Alpha(number, 'A') + ".";
                case CssListStyle.LowerRoman: return Roman(number).ToLowerInvariant() + ".";
                case CssListStyle.UpperRoman: return Roman(number) + ".";
                default: return number + ".";
            }
        }
        switch (type)
        {
            case CssListStyle.None: return null;
            case CssListStyle.Circle: return "\u25CB";
            case CssListStyle.Square: return "\u25A0";
            default: return "\u2022";
        }
    }

    private static string Alpha(int n, char baseCh)
    {
        if (n < 1) n = 1;
        n = ((n - 1) % 26);
        return ((char)(baseCh + n)).ToString();
    }

    private static string Roman(int n)
    {
        if (n < 1) n = 1;
        if (n > 3999) n = 3999;
        int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        string[] symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            while (n >= values[i]) { sb.Append(symbols[i]); n -= values[i]; }
        }
        return sb.ToString();
    }

    // Convert a 0xAARRGGBB uint to a drawing Color, flattening alpha onto white
    // and applying the extra paint opacity.
    private static Color ToColor(uint c, float opacity)
    {
        int a = (int)((c >> 24) & 0xFF);
        int r = (int)((c >> 16) & 0xFF);
        int g = (int)((c >> 8) & 0xFF);
        int b = (int)(c & 0xFF);
        // flatten src alpha onto white
        if (a < 255)
        {
            r = (r * a + 255 * (255 - a)) / 255;
            g = (g * a + 255 * (255 - a)) / 255;
            b = (b * a + 255 * (255 - a)) / 255;
        }
        if (opacity < 0.999f)
        {
            if (opacity < 0) opacity = 0;
            r = (int)(r * opacity + 255 * (1 - opacity));
            g = (int)(g * opacity + 255 * (1 - opacity));
            b = (int)(b * opacity + 255 * (1 - opacity));
        }
        if (r < 0) r = 0; else if (r > 255) r = 255;
        if (g < 0) g = 0; else if (g > 255) g = 255;
        if (b < 0) b = 0; else if (b > 255) b = 255;
        return Color.FromArgb(r, g, b);
    }
}
