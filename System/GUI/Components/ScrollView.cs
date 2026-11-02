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
        contentWidth = Math.Max(width, GetViewportWidth());
        contentHeight = Math.Max(height, GetViewportHeight());

        child.clampSize = false;
        child.X = 0;
        child.Y = 0;
        child.Resize(contentWidth, contentHeight);

        AddChild(child);
        RefreshContentSize();
        ClampScroll();
        ApplyContentOffset();
        MarkDirty();
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);

        if (content != null)
        {
            RefreshContentSize();
            ClampScroll();
            ApplyContentOffset();
        }
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
            int vScrollbarEnd = Height - (showHorizontalScrollbar ? scrollbarSize : 0);
            int downArrowY = Math.Max(scrollbarSize, vScrollbarEnd - scrollbarSize);

            DrawFilledRectangle(Palette.ControlFace, x, 0, scrollbarSize, vScrollbarEnd);
            DrawRaisedRectangle(x, 0, scrollbarSize, scrollbarSize);
            DrawRaisedRectangle(x, downArrowY, scrollbarSize, scrollbarSize);
            DrawArrowUp(x, 0);
            DrawArrowDown(x, downArrowY);

            Rectangle thumb = GetVerticalThumb();
            DrawRaisedRectangle(thumb.X, thumb.Y, thumb.Width, thumb.Height);
        }

        if (showHorizontalScrollbar)
        {
            int y = Height - scrollbarSize;
            int hScrollbarEnd = Width - (showVerticalScrollbar ? scrollbarSize : 0);
            int rightArrowX = Math.Max(scrollbarSize, hScrollbarEnd - scrollbarSize);

            DrawFilledRectangle(Palette.ControlFace, 0, y, hScrollbarEnd, scrollbarSize);
            DrawRaisedRectangle(0, y, scrollbarSize, scrollbarSize);
            DrawRaisedRectangle(rightArrowX, y, scrollbarSize, scrollbarSize);
            DrawArrowLeft(0, y);
            DrawArrowRight(rightArrowX, y);

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
            ScrollBy(0, -(int)Mouse.scroll * wheelStep);
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
                int vScrollbarEnd = Height - (showHorizontalScrollbar ? scrollbarSize : 0);
                int downArrowY = Math.Max(scrollbarSize, vScrollbarEnd - scrollbarSize);

                if (localY < scrollbarSize)
                {
                    ScrollBy(0, -wheelStep);
                }
                else if (localY >= downArrowY && localY < vScrollbarEnd)
                {
                    ScrollBy(0, wheelStep);
                }
                else if (localY >= scrollbarSize && localY < downArrowY)
                {
                    Rectangle thumb = GetVerticalThumb();
                    if (localY < thumb.Y)
                        ScrollBy(0, -GetViewportHeight());
                    else if (localY >= thumb.Bottom)
                        ScrollBy(0, GetViewportHeight());
                }

                return true;
            }

            if (showHorizontalScrollbar && localY >= Height - scrollbarSize)
            {
                int hScrollbarEnd = Width - (showVerticalScrollbar ? scrollbarSize : 0);
                int rightArrowX = Math.Max(scrollbarSize, hScrollbarEnd - scrollbarSize);

                if (localX < scrollbarSize)
                {
                    ScrollBy(-wheelStep, 0);
                }
                else if (localX >= rightArrowX && localX < hScrollbarEnd)
                {
                    ScrollBy(wheelStep, 0);
                }
                else if (localX >= scrollbarSize && localX < rightArrowX)
                {
                    Rectangle thumb = GetHorizontalThumb();
                    if (localX < thumb.X)
                        ScrollBy(-GetViewportWidth(), 0);
                    else if (localX >= thumb.Right)
                        ScrollBy(GetViewportWidth(), 0);
                }

                return true;
            }
        }

        if (mouse.left == MouseEvents.Hold)
        {
            if (draggingVerticalThumb)
            {
                int trackHeight = GetVerticalTrackHeight();
                int viewportHeight = GetViewportHeight();
                int thumbHeight = Math.Max(12, viewportHeight * trackHeight / Math.Max(viewportHeight, contentHeight));
                thumbHeight = Math.Min(trackHeight, thumbHeight);
                int travel = Math.Max(1, trackHeight - thumbHeight);
                int maxScroll = GetMaxScrollY();
                int delta = localY - dragStartMouse;
                int scrollDelta = travel <= 0 ? 0 : delta * maxScroll / travel;
                ScrollTo(scrollX, dragStartScroll + scrollDelta);
                return true;
            }

            if (draggingHorizontalThumb)
            {
                int trackWidth = GetHorizontalTrackWidth();
                int viewportWidth = GetViewportWidth();
                int thumbWidth = Math.Max(12, viewportWidth * trackWidth / Math.Max(viewportWidth, contentWidth));
                thumbWidth = Math.Min(trackWidth, thumbWidth);
                int travel = Math.Max(1, trackWidth - thumbWidth);
                int maxScroll = GetMaxScrollX();
                int delta = localX - dragStartMouse;
                int scrollDelta = travel <= 0 ? 0 : delta * maxScroll / travel;
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

    public void ScrollIntoView(Rectangle childBounds)
    {
        int viewportW = GetViewportWidth();
        int viewportH = GetViewportHeight();

        if (childBounds.Y < scrollY)
        {
            ScrollTo(scrollX, childBounds.Y);
        }
        else if (childBounds.Bottom > scrollY + viewportH)
        {
            ScrollTo(scrollX, childBounds.Bottom - viewportH);
        }

        if (childBounds.X < scrollX)
        {
            ScrollTo(childBounds.X, scrollY);
        }
        else if (childBounds.Right > scrollX + viewportW)
        {
            ScrollTo(childBounds.Right - viewportW, scrollY);
        }
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

    public void RefreshContentSize()
    {
        if (content == null)
            return;

        int viewportWidth = GetViewportWidth();
        int viewportHeight = GetViewportHeight();

        int targetWidth = viewportWidth;
        int targetHeight = viewportHeight;

        if (content is FileListView fileListView)
        {
            if (showHorizontalScrollbar && fileListView.viewMode == ListViewMode.Details)
            {
                int detailsWidth = 0;
                if (fileListView.headerWidths != null)
                {
                    for (int i = 0; i < fileListView.headerWidths.Length; i++)
                        detailsWidth += fileListView.headerWidths[i];
                }
                targetWidth = Math.Max(viewportWidth, detailsWidth);
            }
            else
            {
                targetWidth = viewportWidth;
            }

            if (fileListView.Width != targetWidth)
            {
                fileListView.Resize(targetWidth, fileListView.Height);
            }

            targetHeight = Math.Max(viewportHeight, fileListView.GetContentHeight());
        }
        else if (content is ListView listView)
        {
            if (showHorizontalScrollbar && listView.columns.Count > 0)
            {
                int colWidth = 0;
                for (int i = 0; i < listView.columns.Count; i++)
                    colWidth += listView.columns[i].Width;
                targetWidth = Math.Max(viewportWidth, colWidth);
            }
            else
            {
                targetWidth = viewportWidth;
            }

            targetHeight = Math.Max(viewportHeight, listView.GetContentHeight());
        }
        else if (content is DataGrid dataGrid)
        {
            if (showHorizontalScrollbar)
            {
                targetWidth = Math.Max(viewportWidth, dataGrid.GetContentWidth());
            }
            else
            {
                targetWidth = viewportWidth;
            }

            targetHeight = Math.Max(viewportHeight, dataGrid.GetContentHeight());
        }
        else if (content is TreeView treeView)
        {
            targetWidth = showHorizontalScrollbar ? Math.Max(viewportWidth, contentWidth) : viewportWidth;
            targetHeight = Math.Max(viewportHeight, treeView.GetContentHeight());
        }
        else
        {
            int childMaxX = 0;
            int childMaxY = 0;
            if (content.children != null && content.children.Count > 0)
            {
                for (int i = 0; i < content.children.Count; i++)
                {
                    Component c = content.children[i];
                    if (c != null && c.Visible)
                    {
                        if (c.X + c.Width > childMaxX) childMaxX = c.X + c.Width;
                        if (c.Y + c.Height > childMaxY) childMaxY = c.Y + c.Height;
                    }
                }
            }

            targetWidth = showHorizontalScrollbar ? Math.Max(viewportWidth, Math.Max(contentWidth, childMaxX)) : viewportWidth;
            targetHeight = Math.Max(viewportHeight, Math.Max(contentHeight, childMaxY));
        }

        if (targetWidth != contentWidth || targetHeight != contentHeight || content.Width != targetWidth || content.Height != targetHeight)
        {
            contentWidth = targetWidth;
            contentHeight = targetHeight;
            content.Resize(contentWidth, contentHeight);
            ClampScroll();
            ApplyContentOffset();
        }
    }

    public int GetViewportWidth()
    {
        return Math.Max(1, Width - 4 - (showVerticalScrollbar ? scrollbarSize : 0));
    }

    public int GetViewportHeight()
    {
        return Math.Max(1, Height - 4 - (showHorizontalScrollbar ? scrollbarSize : 0));
    }

    public int GetMaxScrollX()
    {
        return Math.Max(0, contentWidth - GetViewportWidth());
    }

    public int GetMaxScrollY()
    {
        return Math.Max(0, contentHeight - GetViewportHeight());
    }

    public int GetVerticalTrackHeight()
    {
        int vScrollbarEnd = Height - (showHorizontalScrollbar ? scrollbarSize : 0);
        return Math.Max(1, vScrollbarEnd - scrollbarSize * 2);
    }

    public int GetHorizontalTrackWidth()
    {
        int hScrollbarEnd = Width - (showVerticalScrollbar ? scrollbarSize : 0);
        return Math.Max(1, hScrollbarEnd - scrollbarSize * 2);
    }

    private Rectangle GetVerticalThumb()
    {
        int trackY = scrollbarSize;
        int trackHeight = GetVerticalTrackHeight();
        int viewportHeight = GetViewportHeight();
        int maxScroll = GetMaxScrollY();
        if (maxScroll <= 0 || trackHeight <= 0)
        {
            return new Rectangle(Width - scrollbarSize, trackY, scrollbarSize, trackHeight);
        }

        int thumbHeight = Math.Max(12, viewportHeight * trackHeight / Math.Max(viewportHeight, contentHeight));
        thumbHeight = Math.Min(trackHeight, thumbHeight);
        int travel = Math.Max(0, trackHeight - thumbHeight);
        int thumbY = trackY + (travel == 0 ? 0 : scrollY * travel / maxScroll);

        return new Rectangle(Width - scrollbarSize, thumbY, scrollbarSize, thumbHeight);
    }

    private Rectangle GetHorizontalThumb()
    {
        int trackX = scrollbarSize;
        int trackWidth = GetHorizontalTrackWidth();
        int viewportWidth = GetViewportWidth();
        int maxScroll = GetMaxScrollX();
        if (maxScroll <= 0 || trackWidth <= 0)
        {
            return new Rectangle(trackX, Height - scrollbarSize, trackWidth, scrollbarSize);
        }

        int thumbWidth = Math.Max(12, viewportWidth * trackWidth / Math.Max(viewportWidth, contentWidth));
        thumbWidth = Math.Min(trackWidth, thumbWidth);
        int travel = Math.Max(0, trackWidth - thumbWidth);
        int thumbX = trackX + (travel == 0 ? 0 : scrollX * travel / maxScroll);

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

    public override string GetComponentName() => "ScrollView";
}
