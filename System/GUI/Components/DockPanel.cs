using System.Drawing;

public class DockPanel : Component
{
    public bool useBackground = false;
    public Color backgroundColor = Palette.ControlFace;

    private readonly List<Component> dockChildren = new List<Component>();
    private readonly List<Dock> docks = new List<Dock>();

    public DockPanel(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
        Margin = new Thickness(28, 2, 2, 2);  //Default Panel spanning the window leaving space for the title bar 
    }

    public Component AddDockChild(Component child, Dock dock)
    {
        dockChildren.Add(child);
        docks.Add(dock);
        AddChild(child);
        ResolveDockLayout();

        return child;
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        ResolveDockLayout();
    }

    public void ResolveDockLayout()
    {
        int left = Padding.left;
        int top = Padding.top;
        int right = Width - Padding.right;
        int bottom = Height - Padding.bottom;

        // Pass 1: Count how many visible components are set to Fill
        int fillCount = 0;
        for (int i = 0; i < dockChildren.Count; i++)
        {
            if (dockChildren[i].Visible && docks[i] == Dock.Fill)
            {
                fillCount++;
            }
        }

        // Pass 2: Layout the components
        for (int i = 0; i < dockChildren.Count; i++)
        {
            Component child = dockChildren[i];
            if (!child.Visible) continue;

            switch (docks[i])
            {
                case Dock.Top:
                    child.X = left;
                    child.Y = top;
                    child.Resize(Math.Max(1, right - left), child.Height);
                    top += child.Height + child.Margin.bottom;
                    break;

                case Dock.Bottom:
                    child.X = left;
                    child.Y = bottom - child.Height;
                    child.Resize(Math.Max(1, right - left), child.Height);
                    bottom -= child.Height + child.Margin.top;
                    break;

                case Dock.Left:
                    child.X = left;
                    child.Y = top;
                    child.Resize(child.Width, Math.Max(1, bottom - top));
                    left += child.Width + child.Margin.right;
                    break;

                case Dock.Right:
                    child.X = right - child.Width;
                    child.Y = top;
                    child.Resize(child.Width, Math.Max(1, bottom - top));
                    right -= child.Width + child.Margin.left;
                    break;

                case Dock.Fill:
                    // Calculate total height left for all filling elements
                    int totalRemainingHeight = Math.Max(1, bottom - top);

                    // Divide the remaining height by the number of remaining fill components
                    int currentFillHeight = totalRemainingHeight / fillCount;

                    child.X = left;
                    child.Y = top;
                    child.Resize(Math.Max(1, right - left), currentFillHeight);

                    // Advance the top boundary down for the next Fill component
                    top += currentFillHeight;

                    // Decrement the count since this one is allocated
                    fillCount--;
                    break;
            }

            child.MarkDirty();
        }

        MarkDirty();
    }

    public override void Draw()
    {
        base.Draw();
    }

    public override void DrawLocal()
    {
        if (useBackground)
            DrawFilledRectangle(backgroundColor, 0, 0, Width, Height);

        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            DrawChild(child);
        }
    }

    public override string GetComponentName() => "DockPanel";

    public override bool IsOpaqueForCopy() => useBackground;
}
