using System.Drawing;

public class ScrollView : Component
{
    public override bool HandlesMouseWheel => true;
    public Component content;

    public int scrollX;
    public int scrollY;
    public int contentWidth;
    public int contentHeight;
    public int wheelStep = 24;
    public int scrollbarSize = 16;
    public bool showHorizontalScrollbar = true;
    public bool showVerticalScrollbar = true;
    public bool useBackground = true;
    public Color backgroundColor = Palette.ControlWhite;

    private bool draggingVerticalThumb;
    private bool draggingHorizontalThumb;
    private int dragStartMouse;
    private int dragStartScroll;

    public ScrollView(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
    }

    public void SetContent(Component child, int width, int height)
    {
        content = child;
        contentWidth = Math.Max(width, Width);
        contentHeight = Math.Max(height, Height);

        child.clampSize = false;
        child.X = 0;
        child.Y = 0;
        child.Resize(contentWidth, contentHeight);

        AddChild(child);
        ClampScroll();
        ApplyContentOffset();
        MarkDirty();
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);

        if (content != null)
        {
            int newContentWidth = Math.Max(contentWidth, GetViewportWidth());
            int newContentHeight = Math.Max(contentHeight, GetViewportHeight());

            if (newContentWidth != contentWidth || newContentHeight != contentHeight)
            {
                contentWidth = newContentWidth;
                contentHeight = newContentHeight;
                content.Resize(contentWidth, contentHeight);
            }

            ClampScroll();
            ApplyContentOffset();
        }
    }

    public override void Draw()
    {
        base.Draw();
    }

    public override void DrawLocal()
    {
        RefreshContentSize();

        if (useBackground)
            DrawFilledRectangle(backgroundColor, 0, 0, Width, Height);

        DrawSunkenRectangle(0, 0, Width, Height);

        if (content != null)
        {
            DrawChild(content, new Rectangle(2, 2, GetViewportWidth(), GetViewportHeight()));
        }

        DrawScrollbars();
    }

    public override void DrawDirtyLocal(Rectangle dirtyRect)
    {
        DrawLocal();
    }

    protected override void MarkChildDirty()
    {
        base.MarkChildDirty();

        // Changes inside the scrolled content redraw this clipped viewport.
        WindowManager.Invalidate(this);
    }

    private void DrawScrollbars()
    {
        int viewportWidth = GetViewportWidth();
        int viewportHeight = GetViewportHeight();

        if (showVerticalScrollbar)
        {
            int x = Width - scrollbarSize;
            DrawFilledRectangle(Palette.ControlFace, x, 0, scrollbarSize, Height);
            DrawRaisedRectangle(x, 0, scrollbarSize, scrollbarSize);
            DrawRaisedRectangle(x, Height - scrollbarSize, scrollbarSize, scrollbarSize);
            DrawArrowUp(x, 0);
            DrawArrowDown(x, Height - scrollbarSize);

            Rectangle thumb = GetVerticalThumb();
            DrawRaisedRectangle(thumb.X, thumb.Y, thumb.Width, thumb.Height);
        }

        if (showHorizontalScrollbar)
        {
            int y = Height - scrollbarSize;
            DrawFilledRectangle(Palette.ControlFace, 0, y, Width, scrollbarSize);
            DrawRaisedRectangle(0, y, scrollbarSize, scrollbarSize);
            DrawRaisedRectangle(Width - scrollbarSize, y, scrollbarSize, scrollbarSize);
            DrawArrowLeft(0, y);
            DrawArrowRight(Width - scrollbarSize, y);

            Rectangle thumb = GetHorizontalThumb();
            DrawRaisedRectangle(thumb.X, thumb.Y, thumb.Width, thumb.Height);
        }

        if (showVerticalScrollbar && showHorizontalScrollbar)
            DrawFilledRectangle(Palette.ControlFace, Width - scrollbarSize, Height - scrollbarSize, scrollbarSize, scrollbarSize);
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInsideAbsolute(mouseX, mouseY))
            return false;

        int localX = mouseX - AbsoluteX;
        int localY = mouseY - AbsoluteY;

        if (Mouse.scroll != 0)
        {
            ScrollBy(0, (int)Mouse.scroll * wheelStep);
            return true;
        }

        if (mouse.left == MouseEvents.Press)
        {
            if (showVerticalScrollbar && GetVerticalThumb().Contains(localX, localY))
            {
                draggingVerticalThumb = true;
                dragStartMouse = localY;
                dragStartScroll = scrollY;
                return true;
            }

            if (showHorizontalScrollbar && GetHorizontalThumb().Contains(localX, localY))
            {
                draggingHorizontalThumb = true;
                dragStartMouse = localX;
                dragStartScroll = scrollX;
                return true;
            }

            if (showVerticalScrollbar && localX >= Width - scrollbarSize)
            {
                if (localY < scrollbarSize) ScrollBy(0, -wheelStep);
                else if (localY > Height - scrollbarSize) ScrollBy(0, wheelStep);
                else if (localY < GetVerticalThumb().Y) ScrollBy(0, -GetViewportHeight());
                else ScrollBy(0, GetViewportHeight());

                return true;
            }

            if (showHorizontalScrollbar && localY >= Height - scrollbarSize)
            {
                if (localX < scrollbarSize) ScrollBy(-wheelStep, 0);
                else if (localX > Width - scrollbarSize) ScrollBy(wheelStep, 0);
                else if (localX < GetHorizontalThumb().X) ScrollBy(-GetViewportWidth(), 0);
                else ScrollBy(GetViewportWidth(), 0);

                return true;
            }
        }

        if (mouse.left == MouseEvents.Hold)
        {
            if (draggingVerticalThumb)
            {
                int trackHeight = GetVerticalTrackHeight();
                int maxScroll = GetMaxScrollY();
                int delta = localY - dragStartMouse;
                int scrollDelta = trackHeight <= 0 ? 0 : delta * maxScroll / trackHeight;
                ScrollTo(scrollX, dragStartScroll + scrollDelta);
                return true;
            }

            if (draggingHorizontalThumb)
            {
                int trackWidth = GetHorizontalTrackWidth();
                int maxScroll = GetMaxScrollX();
                int delta = localX - dragStartMouse;
                int scrollDelta = trackWidth <= 0 ? 0 : delta * maxScroll / trackWidth;
                ScrollTo(dragStartScroll + scrollDelta, scrollY);
                return true;
            }
        }

        if (mouse.left == MouseEvents.Release || mouse.left == MouseEvents.None)
        {
            draggingVerticalThumb = false;
            draggingHorizontalThumb = false;
        }

        if (content != null && localX >= 2 && localY >= 2 && localX < 2 + GetViewportWidth() && localY < 2 + GetViewportHeight())
        {
            bool handled = content.HandleInput(mouseX, mouseY, mouse);
            RefreshContentSize();
            return handled;
        }

        return true;
    }

    public void ScrollBy(int x, int y)
    {
        ScrollTo(scrollX + x, scrollY + y);
    }

    public void ScrollTo(int x, int y)
    {
        scrollX = x;
        scrollY = y;
        ClampScroll();
        ApplyContentOffset();
        MarkDirty();
    }

    public void RefreshContent(bool resetScroll = false)
    {
        RefreshContentSize();

        if (resetScroll)
        {
            scrollX = 0;
            scrollY = 0;
        }

        ClampScroll();
        ApplyContentOffset();

        if (content != null)
            content.MarkDirty(false);

        ForceDirty();
    }

    private void ClampScroll()
    {
        scrollX = Math.Max(0, Math.Min(scrollX, GetMaxScrollX()));
        scrollY = Math.Max(0, Math.Min(scrollY, GetMaxScrollY()));
    }

    private void ApplyContentOffset()
    {
        if (content == null) return;

        content.X = 2 - scrollX;
        content.Y = 2 - scrollY;
    }

    private void RefreshContentSize()
    {
        if (content == null)
            return;

        if (content is TreeView treeView)
        {
            int newHeight = Math.Max(GetViewportHeight(), treeView.GetContentHeight());

            if (newHeight != contentHeight)
            {
                contentHeight = newHeight;
                content.Resize(contentWidth, contentHeight);
                ClampScroll();
                ApplyContentOffset();
            }
        }
        else if (content is ListView listView)
        {
            int newHeight = Math.Max(GetViewportHeight(), listView.GetContentHeight());

            if (newHeight != contentHeight)
            {
                contentHeight = newHeight;
                content.Resize(contentWidth, contentHeight);
                ClampScroll();
                ApplyContentOffset();
            }
        }
    }

    private int GetViewportWidth()
    {
        return Math.Max(1, Width - 4 - (showVerticalScrollbar ? scrollbarSize : 0));
    }

    private int GetViewportHeight()
    {
        return Math.Max(1, Height - 4 - (showHorizontalScrollbar ? scrollbarSize : 0));
    }

    private int GetMaxScrollX()
    {
        return Math.Max(0, contentWidth - GetViewportWidth());
    }

    private int GetMaxScrollY()
    {
        return Math.Max(0, contentHeight - GetViewportHeight());
    }

    private int GetVerticalTrackHeight()
    {
        return Math.Max(1, Height - scrollbarSize * 2);
    }

    private int GetHorizontalTrackWidth()
    {
        return Math.Max(1, Width - scrollbarSize * 2);
    }

    private Rectangle GetVerticalThumb()
    {
        int trackY = scrollbarSize;
        int trackHeight = GetVerticalTrackHeight();
        int viewportHeight = GetViewportHeight();
        int thumbHeight = Math.Max(12, viewportHeight * trackHeight / Math.Max(viewportHeight, contentHeight));
        int travel = Math.Max(1, trackHeight - thumbHeight);
        int thumbY = trackY + (GetMaxScrollY() == 0 ? 0 : scrollY * travel / GetMaxScrollY());

        return new Rectangle(Width - scrollbarSize, thumbY, scrollbarSize, thumbHeight);
    }

    private Rectangle GetHorizontalThumb()
    {
        int trackX = scrollbarSize;
        int trackWidth = GetHorizontalTrackWidth();
        int viewportWidth = GetViewportWidth();
        int thumbWidth = Math.Max(12, viewportWidth * trackWidth / Math.Max(viewportWidth, contentWidth));
        int travel = Math.Max(1, trackWidth - thumbWidth);
        int thumbX = trackX + (GetMaxScrollX() == 0 ? 0 : scrollX * travel / GetMaxScrollX());

        return new Rectangle(thumbX, Height - scrollbarSize, thumbWidth, scrollbarSize);
    }

    private void DrawArrowUp(int x, int y)
    {
        int cx = x + scrollbarSize / 2;
        int cy = y + scrollbarSize / 2 - 2;
        DrawLine(Palette.ControlBlack, cx, cy, cx - 4, cy + 4);
        DrawLine(Palette.ControlBlack, cx, cy, cx + 4, cy + 4);
    }

    private void DrawArrowDown(int x, int y)
    {
        int cx = x + scrollbarSize / 2;
        int cy = y + scrollbarSize / 2 + 2;
        DrawLine(Palette.ControlBlack, cx, cy, cx - 4, cy - 4);
        DrawLine(Palette.ControlBlack, cx, cy, cx + 4, cy - 4);
    }

    private void DrawArrowLeft(int x, int y)
    {
        int cx = x + scrollbarSize / 2 - 2;
        int cy = y + scrollbarSize / 2;
        DrawLine(Palette.ControlBlack, cx, cy, cx + 4, cy - 4);
        DrawLine(Palette.ControlBlack, cx, cy, cx + 4, cy + 4);
    }

    private void DrawArrowRight(int x, int y)
    {
        int cx = x + scrollbarSize / 2 + 2;
        int cy = y + scrollbarSize / 2;
        DrawLine(Palette.ControlBlack, cx, cy, cx - 4, cy - 4);
        DrawLine(Palette.ControlBlack, cx, cy, cx - 4, cy + 4);
    }

    public override bool IsOpaqueForCopy() => useBackground;

    public override string GetName() => "ScrollView";
}
