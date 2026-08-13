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

        for (int i = 0; i < children.Count; i++)
        {
            Component child = children[i];
            if (!child.Visible) continue;

            child.PrepareLayout();

            if (orientation == StackOrientation.Vertical)
            {
                child.X = cursorX + child.Margin.left;

                if (child.horizontalAlignment == HorizontalAlignment.Stretch)
                {
                    child.Resize(availableWidth - child.Margin.left - child.Margin.right, child.Height);
                }
                else if (child.horizontalAlignment == HorizontalAlignment.Center)
                {
                    child.X = Padding.left + (availableWidth - child.Width) / 2;
                }
                else if (child.horizontalAlignment == HorizontalAlignment.Right)
                {
                    child.X = Width - Padding.right - child.Margin.right - child.Width;
                }

                child.Y = cursorY + child.Margin.top;
                cursorY = child.Y + child.Height + child.Margin.bottom + spacing;
            }
            else
            {
                child.Y = cursorY + child.Margin.top;

                if (child.verticalAlignment == VerticalAlignment.Stretch)
                {
                    child.Resize(child.Width, availableHeight - child.Margin.top - child.Margin.bottom);
                }
                else if (child.verticalAlignment == VerticalAlignment.Center)
                {
                    child.Y = Padding.top + (availableHeight - child.Height) / 2;
                }
                else if (child.verticalAlignment == VerticalAlignment.Bottom)
                {
                    child.Y = Height - Padding.bottom - child.Margin.bottom - child.Height;
                }

                child.X = cursorX + child.Margin.left;
                cursorX = child.X + child.Width + child.Margin.right + spacing;
            }

            child.MarkDirty();
        }

        MarkDirty();
    }
}
