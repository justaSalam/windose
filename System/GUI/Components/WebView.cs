using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Input;
using Cosmos.Kernel.System.Graphics;

/// <summary>
/// Renders a parsed web page (a <see cref="RenderPlan"/>) with scrolling, block
/// backgrounds/borders, images, styled text and clickable links. Networking and
/// parsing happen outside; this only lays out (on resize) and draws.
/// </summary>
public class WebView : Component
{
    public override bool HandlesMouseWheel => true;

    public int fontSize = 16;
    public int scrollbarSize = 16;

    /// <summary>Raised with the raw href when the user clicks a link.</summary>
    public Action<string> LinkActivated;

    private HtmlPage document;
    private RenderPlan plan = new RenderPlan();
    private string baseHref = "";
    private int scrollY;
    private int layoutWidth = -1;
    private Dictionary<string, Image> images = new Dictionary<string, Image>();

    // Script engine: one interpreter + DOM per loaded page.
    private JsInterpreter js;
    private JsDom dom;
    private bool layoutDirty;

    public string DocumentTitle => document != null ? document.Title : "";
    public string BaseHref => baseHref;

    public WebView(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
        Margin = new Thickness(0);
    }

    /// <summary>Swap in a freshly fetched/parsed page and reset scroll.</summary>
    public void SetDocument(HtmlPage page, string baseHref)
    {
        SetDocument(page, baseHref, null);
    }

    public void SetDocument(HtmlPage page, string baseHref, Dictionary<string, Image> pageImages)
    {
        this.document = page;
        this.baseHref = baseHref ?? (page != null ? page.BaseHref : "");
        this.images = pageImages != null ? pageImages : new Dictionary<string, Image>();
        scrollY = 0;

        StartEngine();
        RebuildLayout();
        MarkDirty();
    }

    /// <summary>Create a fresh interpreter + DOM for the page and run its scripts
    /// in document order, then fire DOMContentLoaded and load.</summary>
    private void StartEngine()
    {
        js = null;
        dom = null;
        if (document == null || document.Root == null) return;
        if (document.Scripts == null || document.Scripts.Count == 0) return; // static page: no engine needed

        js = new JsInterpreter();
        js.InitGlobal();
        dom = new JsDom(js, document.Root);
        dom.OnDomChanged = OnDomMutated;
        dom.Install();

        RunScripts();
        dom.FireReadyEvents();

        // scripts may have mutated the tree; relayout once at the end
        if (layoutDirty)
        {
            layoutDirty = false;
            RebuildLayout();
        }
    }

    private void RunScripts()
    {
        if (js == null || dom == null) return;
        for (int i = 0; i < document.Scripts.Count; i++)
        {
            HtmlScript s = document.Scripts[i];
            if (string.IsNullOrEmpty(s.Code)) continue; // external script failed to fetch
            try
            {
                JsProgram prog = JsParser.ParseProgram(s.Code);
                js.RunProgram(prog);
            }
            catch (JsThrown) { /* a syntax/runtime error in one script must not stop the rest */ }
            catch (Exception) { }
        }
    }

    private void OnDomMutated()
    {
        layoutDirty = true;
        MarkDirty();
    }

    /// <summary>Dispatch a DOM click event at a content-space point. Returns true
    /// if the default was prevented (host should not navigate).</summary>
    private bool DispatchClickAt(int contentX, int contentY)
    {
        if (dom == null) return false;
        DomNode node = plan.HitTestNode(contentX, contentY);
        if (node == null) return false;
        return !dom.Dispatch(node, "click"); // Dispatch returns !defaultPrevented
    }

    /// <summary>Show a plain error/blank page without touching the network.</summary>
    public void SetMessage(string title, string message)
    {
        string html = "";
        if (!string.IsNullOrEmpty(title)) html += "<h2>" + title + "</h2>";
        if (!string.IsNullOrEmpty(message)) html += "<p>" + message + "</p>";
        SetDocument(HtmlPageBuilder.Parse(html), "");
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        RebuildLayout();
    }

    private void RebuildLayout()
    {
        layoutWidth = Width;
        if (document == null) { plan = new RenderPlan(); return; }
        plan = HtmlLayout.Build(document.Root, document.CssText, Width, Height, fontSize,
            (s, sz) => MeasureTextWidth(s, sz), images, baseHref);
        scrollY = Math.Max(0, Math.Min(scrollY, MaxScroll()));
    }

    public override void DrawLocal()
    {
        if (layoutWidth != Width || layoutDirty)
        {
            layoutDirty = false;
            RebuildLayout();
        }

        DrawFilledRectangle(plan.PageBg, 0, 0, Width, Height);

        int viewTop = 2;
        int viewHeight = Height - 4;
        int viewportWidth = Math.Max(1, Width - scrollbarSize);

        // Block backgrounds/borders painted behind the text.
        for (int i = 0; i < plan.Boxes.Count; i++)
        {
            RenderBox box = plan.Boxes[i];
            if (box.Hidden) continue;
            int by = box.Y - scrollY + viewTop;
            if (by + box.H < viewTop || by > viewTop + viewHeight) continue;
            DrawBox(box, by, viewportWidth);
        }

        // Clip manually by culling lines outside the viewport.
        for (int i = 0; i < plan.Lines.Count; i++)
        {
            RenderLine line = plan.Lines[i];
            int ly = line.Y - scrollY + viewTop;
            if (ly + line.Height < viewTop || ly > viewTop + viewHeight) continue;
            DrawLineContent(line, ly, viewportWidth);
        }

        DrawScrollbar();
    }

    private void DrawBox(RenderBox box, int by, int viewportWidth)
    {
        if (box.HasBg)
            DrawFilledRectangle(box.Bg, box.X, by, box.W, box.H);

        if (box.BorderT > 0) DrawBorderSide(box.StyleT, box.BorderCT, box.X, by, box.W, box.BorderT, 0);
        if (box.BorderB > 0) DrawBorderSide(box.StyleB, box.BorderCB, box.X, by + box.H - box.BorderB, box.W, box.BorderB, 2);
        if (box.BorderL > 0) DrawBorderSide(box.StyleL, box.BorderCL, box.X, by, box.BorderL, box.H, 3);
        if (box.BorderR > 0) DrawBorderSide(box.StyleR, box.BorderCR, box.X + box.W - box.BorderR, by, box.BorderR, box.H, 1);
    }

    // side: 0=top,1=right,2=bottom,3=left
    private void DrawBorderSide(CssBorder style, Color color, int x, int y, int w, int h, int side)
    {
        if (w <= 0 || h <= 0) return;
        switch (style)
        {
            case CssBorder.None:
                return;
            case CssBorder.Dotted:
                DrawDotted(color, x, y, w, h, side);
                return;
            case CssBorder.Dashed:
                DrawDashed(color, x, y, w, h, side);
                return;
            case CssBorder.Double:
                // approximate as a thicker solid line
                DrawFilledRectangle(color, x, y, w, h);
                return;
            default:
                DrawFilledRectangle(color, x, y, w, h);
                return;
        }
    }

    private void DrawDotted(Color c, int x, int y, int w, int h, int side)
    {
        if (side == 0 || side == 2)
        {
            for (int i = 0; i < w; i += 3) DrawFilledRectangle(c, x + i, y, 1, h);
        }
        else
        {
            for (int i = 0; i < h; i += 3) DrawFilledRectangle(c, x, y + i, w, 1);
        }
    }

    private void DrawDashed(Color c, int x, int y, int w, int h, int side)
    {
        if (side == 0 || side == 2)
        {
            for (int i = 0; i < w; i += 6) DrawFilledRectangle(c, x + i, y, Math.Min(4, w - i), h);
        }
        else
        {
            for (int i = 0; i < h; i += 6) DrawFilledRectangle(c, x, y + i, w, Math.Min(4, h - i));
        }
    }

    private void DrawLineContent(RenderLine line, int ly, int viewportWidth)
    {
        if (line.Hidden) return;

        if (line.IsRule)
        {
            DrawFilledRectangle(line.RuleColor, Math.Max(2, line.TextX), ly + line.Height / 2, viewportWidth - 4, Math.Max(1, line.Height));
            return;
        }

        if (line.IsImage)
        {
            if (line.Image != null)
            {
                DrawImageStretch(line.Image, new Rectangle(line.TextX, ly, line.ImageW, line.ImageH));
            }
            else
            {
                DrawSunkenRectangle(line.TextX, ly, line.ImageW, line.ImageH);
                DrawFilledRectangle(Color.FromArgb(235, 238, 242), line.TextX + 1, ly + 1, line.ImageW - 2, line.ImageH - 2);
                if (!string.IsNullOrEmpty(line.ImageAlt))
                {
                    int tw = MeasureTextWidth(line.ImageAlt, 14);
                    DrawString(line.ImageAlt, Color.FromArgb(90, 90, 100), line.TextX + (line.ImageW - tw) / 2, ly + line.ImageH / 2 - 8, 14);
                }
            }
            return;
        }

        if (line.Bullet != null)
            DrawString(line.Bullet, Palette.ControlBlack, line.BulletX + 2, ly + 2, fontSize);

        for (int i = 0; i < line.Spans.Count; i++)
        {
            RenderSpan s = line.Spans[i];
            if (s.Hidden || string.IsNullOrEmpty(s.Text)) continue;

            int size = s.Size > 0 ? s.Size : fontSize;
            int ty = ly + 2 + s.YOff;
            if (s.HasBg)
                DrawFilledRectangle(s.Bg, s.X, ty, s.Width, size + 2);

            DrawString(s.Text, s.Color, s.X, ty, size);
            if (s.Bold) DrawString(s.Text, s.Color, s.X + 1, ty, size); // faux bold
            if (s.Underline) DrawLine(s.Color, s.X, ty + size, s.X + s.Width, ty + size);
            if (s.Overline) DrawLine(s.Color, s.X, ty, s.X + s.Width, ty);
            if (s.LineThrough) DrawLine(s.Color, s.X, ty + size / 2, s.X + s.Width, ty + size / 2);
        }
    }

    private void DrawScrollbar()
    {
        int x = Width - scrollbarSize;
        if (MaxScroll() <= 0)
        {
            DrawFilledRectangle(Palette.ControlFace, x, 0, scrollbarSize, Height);
            return;
        }

        DrawFilledRectangle(Palette.ControlFace, x, 0, scrollbarSize, Height);
        DrawRaisedRectangle(x, 0, scrollbarSize, scrollbarSize);
        DrawRaisedRectangle(x, Height - scrollbarSize, scrollbarSize, scrollbarSize);
        DrawString("^", Palette.ControlBlack, x + 4, 1, 14);
        DrawString("v", Palette.ControlBlack, x + 4, Height - scrollbarSize + 1, 14);

        Rectangle thumb = GetThumb();
        DrawRaisedRectangle(thumb.X, thumb.Y, thumb.Width, thumb.Height);
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInsideAbsolute(mouseX, mouseY)) return false;

        if (Mouse.scroll != 0)
        {
            ScrollBy(-(int)Mouse.scroll * 48);
            return true;
        }

        if (mouse.left == MouseEvents.Press)
        {
            int localX = mouseX - AbsoluteX;
            int localY = mouseY - AbsoluteY;

            if (localX >= Width - scrollbarSize)
            {
                if (localY < scrollbarSize) ScrollBy(-48);
                else if (localY >= Height - scrollbarSize) ScrollBy(48);
                else if (localY < GetThumb().Y) ScrollBy(-ViewportHeight());
                else if (localY > GetThumb().Bottom) ScrollBy(ViewportHeight());
                return true;
            }

            // DOM click event dispatch (content coords)
            int contentX = localX;
            int contentY = localY - 2 + scrollY;

            // A script listener may mutate the tree; relayout before hit-testing
            // so the plan matches what's on screen.
            if (layoutDirty) { layoutDirty = false; RebuildLayout(); }

            bool defaultPrevented = DispatchClickAt(contentX, contentY);

            // link hit-test (content coords) — skipped if a script prevented default
            if (!defaultPrevented)
            {
                string href = plan.HitTestLink(contentX, contentY);
                if (href != null && LinkActivated != null)
                {
                    LinkActivated(href);
                    return true;
                }
            }
        }

        return true;
    }

    private void ScrollBy(int amount)
    {
        int next = Math.Max(0, Math.Min(MaxScroll(), scrollY + amount));
        if (next == scrollY) return;
        scrollY = next;
        MarkDirty();
    }

    private Rectangle GetThumb()
    {
        int trackTop = scrollbarSize;
        int trackHeight = Math.Max(1, Height - scrollbarSize * 2);
        int totalHeight = Math.Max(ViewportHeight(), plan.ContentHeight);
        int thumbHeight = Math.Max(16, trackHeight * ViewportHeight() / Math.Max(1, totalHeight));
        int travel = Math.Max(0, trackHeight - thumbHeight);
        int thumbY = trackTop + (MaxScroll() == 0 ? 0 : scrollY * travel / MaxScroll());
        return new Rectangle(Width - scrollbarSize, thumbY, scrollbarSize, thumbHeight);
    }

    private int ViewportHeight() => Math.Max(1, Height - 4);
    private int MaxScroll() => Math.Max(0, plan.ContentHeight - ViewportHeight());

    public override string GetComponentName() => "WebView";
}
