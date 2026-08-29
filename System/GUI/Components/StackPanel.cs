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

    public void AddStackChild(Component child)
    {
        AddChild(child);
        ResolveStackLayout();
    }

    public void RemoveStackChild(Component child)
    {
        RemoveChild(child);
        ResolveStackLayout();
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        ResolveStackLayout();
    }

    public void ResolveStackLayout()
    {
        int cursorX = Padding.left;
        int cursorY = Padding.top;
        int availableWidth = Width - Padding.left - Padding.right;
        int availableHeight = Height - Padding.top - Padding.bottom;

        if (orientation == StackOrientation.Vertical)
        {
            for (int i = 0; i < children.Count; i++)
            {
                Component child = children[i];
                if (!child.Visible) continue;
                child.PrepareLayout();

                child.X = child.horizontalAlignment switch
                {
                    HorizontalAlignment.Center => Padding.left + (availableWidth - child.Width) / 2,
                    HorizontalAlignment.Right => Width - Padding.right - child.Margin.right - child.Width,
                    _ => cursorX + child.Margin.left,
                };
                if (child.horizontalAlignment == HorizontalAlignment.Stretch)
                    child.Resize(availableWidth - child.Margin.left - child.Margin.right, child.Height);

                child.Y = cursorY + child.Margin.top;
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

                child.Y = child.verticalAlignment switch
                {
                    VerticalAlignment.Center => Padding.top + (availableHeight - child.Height) / 2,
                    VerticalAlignment.Bottom => Height - Padding.bottom - child.Margin.bottom - child.Height,
                    _ => cursorY + child.Margin.top,
                };
                if (child.verticalAlignment == VerticalAlignment.Stretch)
                    child.Resize(child.Width, availableHeight - child.Margin.top - child.Margin.bottom);

                if (child.horizontalAlignment == HorizontalAlignment.Right)
                {
                    rightCursor -= child.Margin.right + child.Width;
                    child.X = rightCursor;
                    rightCursor -= child.Margin.left + spacing;
                }
                else
                {
                    child.X = leftCursor + child.Margin.left;
                    leftCursor = child.X + child.Width + child.Margin.right + spacing;
                }

                child.MarkDirty();
            }
        }

        MarkDirty();
    }
}
