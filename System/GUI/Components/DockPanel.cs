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
    }

    public void AddDockChild(Component child, Dock dock)
    {
        dockChildren.Add(child);
        docks.Add(dock);
        AddChild(child);
        ResolveDockLayout();
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
                    child.X = left;
                    child.Y = top;
                    child.Resize(Math.Max(1, right - left), Math.Max(1, bottom - top));
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

            child.DrawLocal();
            DrawChild(child);
            child.MarkCleaned();
        }
    }

    public override string GetName() => "DockPanel";

    public override bool IsOpaqueForCopy() => useBackground;
}
