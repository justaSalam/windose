using System.Drawing;

public enum Dock { None, Top, Bottom, Left, Right, Fill }

public class DockPanel : Component
{
    public bool useBackground = false;
    public Color backgroundColor = Palette.ControlFace;
    public int gridDots = 0;                       // designer aid, 0 = off

    public DockPanel(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
    }

    public Component AddDockChild(Component child, Dock dockStyle)
    {
        child.dock = dockStyle;
        return AddChild(child);
    }

    public T AddDockChild<T>(T child, Dock dockStyle) where T : Component
    {
        child.dock = dockStyle;
        AddChild((Component)child);
        return child;
    }

    public override Component AddChild(Component child)
    {
        Component added = base.AddChild(child);
        ResolveDockLayout();
        return added;
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        ResolveDockLayout();
    }

    public override void RemoveChild(Component child)
    {
        base.RemoveChild(child);
        ResolveDockLayout();
    }

    protected override void OnChildVisibilityChanged(Component child) => ResolveDockLayout();

    public void ResolveDockLayout()
    {
        List<Component> list;
        lock (children) { list = new List<Component>(children); }

        int left = Padding.left, top = Padding.top;
        int right = Width - Padding.right, bottom = Height - Padding.bottom;

        // Pass 1: edges, in child order
        foreach (Component c in list)
        {
            if (!c.Visible || c.dock == Dock.None || c.dock == Dock.Fill) continue;

            Thickness m = c.Margin;
            int availW = Math.Max(0, right - left);
            int availH = Math.Max(0, bottom - top);

            switch (c.dock)
            {
                case Dock.Top:
                    {
                        int h = Math.Min(c.Height, Math.Max(0, availH - m.top - m.bottom));
                        Place(c, left + m.left, top + m.top, availW - m.left - m.right, h);
                        top += h + m.top + m.bottom;
                        break;
                    }
                case Dock.Bottom:
                    {
                        int h = Math.Min(c.Height, Math.Max(0, availH - m.top - m.bottom));
                        Place(c, left + m.left, bottom - m.bottom - h, availW - m.left - m.right, h);
                        bottom -= h + m.top + m.bottom;
                        break;
                    }
                case Dock.Left:
                    {
                        int w = Math.Min(c.Width, Math.Max(0, availW - m.left - m.right));
                        Place(c, left + m.left, top + m.top, w, availH - m.top - m.bottom);
                        left += w + m.left + m.right;
                        break;
                    }
                case Dock.Right:
                    {
                        int w = Math.Min(c.Width, Math.Max(0, availW - m.left - m.right));
                        Place(c, right - m.right - w, top + m.top, w, availH - m.top - m.bottom);
                        right -= w + m.left + m.right;
                        break;
                    }
            }
        }

        // Pass 2: fills share what is left (stacked vertically)
        int fills = 0;
        foreach (Component c in list)
            if (c.Visible && c.dock == Dock.Fill) fills++;

        foreach (Component c in list)
        {
            if (!c.Visible || c.dock != Dock.Fill || fills == 0) continue;

            Thickness m = c.Margin;
            int slice = Math.Max(0, bottom - top) / fills;
            Place(c, left + m.left, top + m.top,
                  right - left - m.left - m.right, slice - m.top - m.bottom);
            top += slice;
            fills--;
        }

        MarkDirty();
    }

    private static void Place(Component c, int x, int y, int w, int h)
        => c.SetBounds(x, y, Math.Max(2, w), Math.Max(1, h));

    public override void DrawLocal()
    {
        if (useBackground)
            DrawFilledRectangle(backgroundColor, 0, 0, Width, Height);

        if (gridDots > 1)
            for (int gy = gridDots; gy < Height; gy += gridDots)
                for (int gx = gridDots; gx < Width; gx += gridDots)
                    DrawFilledRectangle(Color.Gray, gx, gy, 1, 1);

        foreach (Component child in children)
            if (child.Visible) DrawChild(child);
    }

    public override string GetComponentName() => "DockPanel";
    public override bool IsOpaqueForCopy() => useBackground;
}