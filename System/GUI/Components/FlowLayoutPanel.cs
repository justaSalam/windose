using System.Drawing;

/// <summary>
/// Layout container that arranges its children in a flow, wrapping to the next line
/// when they exceed the container's width. Useful for toolbars, tag clouds, and forms
/// where controls should pack naturally rather than being fixed in a stack.
/// </summary>
public class FlowLayoutPanel : Panel
{
    public int spacing = 4;
    public HorizontalAlignment lineAlignment = HorizontalAlignment.Left;


    public FlowLayoutPanel(Color color, int x, int y, int width, int height) : base(color, x, y, width, height)
    {
        clampSize = false;
        color1 = color;
        useBackground = true;
    }

    public T AddFlowChild<T>(T child) where T : Component
    {
        AddChild((Component)child);
        return child;
    }

    public void RemoveFlowChild(Component child)
    {
        RemoveChild(child);
    }

    public override Component AddChild(Component child)
    {
        Component added = base.AddChild(child);
        ResolveFlowLayout();
        return added;
    }

    public override void RemoveChild(Component child)
    {
        base.RemoveChild(child);
        ResolveFlowLayout();
    }

    protected override void OnChildVisibilityChanged(Component child) => ResolveFlowLayout();

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        ResolveFlowLayout();
    }

    public void ResolveFlowLayout()
    {
        int cursorX = Padding.left;
        int cursorY = Padding.top;
        int maxX = Width - Padding.right;
        int maxY = Height - Padding.bottom;
        int lineHeight = 0;

        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            child.PrepareLayout();

            int childWidth = child.horizontalAlignment == HorizontalAlignment.Stretch
                ? Math.Max(1, maxX - cursorX - Padding.left - Padding.right)
                : child.Width;
            int childHeight = child.verticalAlignment == VerticalAlignment.Stretch
                ? Math.Max(1, maxY - cursorY - Padding.top - Padding.bottom)
                : child.Height;

            // Wrap to the next line if the child no longer fits horizontally.
            if (cursorX + child.Margin.left + childWidth > maxX && cursorX > Padding.left)
            {
                cursorX = Padding.left;
                cursorY += lineHeight + spacing;
                lineHeight = 0;
            }

            int childX = cursorX + child.Margin.left;
            int childY = cursorY + child.Margin.top;

            switch (lineAlignment)
            {
                case HorizontalAlignment.Center:
                    childX = Padding.left + (maxX - Padding.left - Padding.right - childWidth) / 2;
                    break;
                case HorizontalAlignment.Right:
                    childX = maxX - Padding.right - childWidth;
                    break;
            }

            childX = Math.Max(Padding.left + child.Margin.left, Math.Min(childX, maxX - Padding.right - child.Margin.right - childWidth));
            child.SetBounds(childX, childY, childWidth, childHeight);

            cursorX = childX + childWidth + child.Margin.right + spacing;
            lineHeight = Math.Max(lineHeight, childHeight + child.Margin.top + child.Margin.bottom);

            child.MarkDirty();
        }

        MarkDirty();
    }
}
