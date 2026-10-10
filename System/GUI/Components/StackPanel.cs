using System.Drawing;

public class StackPanel : Panel
{
    public StackOrientation orientation = StackOrientation.Vertical;
    public int spacing = 4;

    public StackPanel(Color color, int x, int y, int width, int height) : base(color, x, y, width, height)
    {
    }

    public StackPanel(Color color1, Color color2, int x, int y, int width, int height) : base(color1, color2, x, y, width, height)
    {
    }

    public T AddStackChild<T>(T child) where T : Component
    {
        AddChild((Component)child);
        return child;
    }

    public void RemoveStackChild(Component child)
    {
        RemoveChild(child);
    }

    public override Component AddChild(Component child)
    {
        Component added = base.AddChild(child);
        ResolveStackLayout();
        return added;
    }

    public override void RemoveChild(Component child)
    {
        base.RemoveChild(child);
        ResolveStackLayout();
    }

    protected override void OnChildVisibilityChanged(Component child) => ResolveStackLayout();

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        ResolveStackLayout();
    }

    public void ResolveStackLayout()
    {
        int cursorX = Padding.left;
        int cursorY = Padding.top;
        int availableWidth = Math.Max(0, Width - Padding.left - Padding.right);
        int availableHeight = Math.Max(0, Height - Padding.top - Padding.bottom);

        if (orientation == StackOrientation.Vertical)
        {
            for (int i = 0; i < children.Count; i++)
            {
                Component child = children[i];
                if (!child.Visible) continue;
                child.PrepareLayout();

                int childWidth = child.horizontalAlignment == HorizontalAlignment.Stretch
                    ? Math.Max(1, availableWidth - child.Margin.left - child.Margin.right)
                    : child.Width;
                int childX = child.horizontalAlignment switch
                {
                    HorizontalAlignment.Center => Padding.left + child.Margin.left + (availableWidth - child.Margin.left - child.Margin.right - childWidth) / 2,
                    HorizontalAlignment.Right => Width - Padding.right - child.Margin.right - childWidth,
                    _ => cursorX + child.Margin.left,
                };
                childWidth = Math.Min(childWidth, Math.Max(1, Width - Padding.right - child.Margin.right - childX));

                int childY = cursorY + child.Margin.top;
                child.SetBounds(childX, childY, childWidth, child.Height);
                cursorY = child.Y + child.Height + child.Margin.bottom + spacing;

                child.MarkDirty();
            }
        }
        else
        {
            int leftCursor = cursorX;
            int rightCursor = Width - Padding.right;

            for (int i = 0; i < children.Count; i++)
            {
                Component child = children[i];
                if (!child.Visible) continue;
                child.PrepareLayout();

                int childHeight = child.verticalAlignment == VerticalAlignment.Stretch
                    ? Math.Max(1, availableHeight - child.Margin.top - child.Margin.bottom)
                    : child.Height;
                int childY = child.verticalAlignment switch
                {
                    VerticalAlignment.Center => Padding.top + child.Margin.top + (availableHeight - child.Margin.top - child.Margin.bottom - childHeight) / 2,
                    VerticalAlignment.Bottom => Height - Padding.bottom - child.Margin.bottom - childHeight,
                    _ => cursorY + child.Margin.top,
                };

                int childX;
                if (child.horizontalAlignment == HorizontalAlignment.Right)
                {
                    rightCursor -= child.Margin.right + child.Width;
                    childX = rightCursor;
                    rightCursor -= child.Margin.left + spacing;
                }
                else
                {
                    childX = leftCursor + child.Margin.left;
                    leftCursor = childX + child.Width + child.Margin.right + spacing;
                }
                childX = Math.Max(Padding.left + child.Margin.left, Math.Min(childX, Width - Padding.right - child.Margin.right - child.Width));

                child.SetBounds(childX, childY, child.Width, childHeight);
                child.MarkDirty();
            }
        }

        MarkDirty();
    }
}
