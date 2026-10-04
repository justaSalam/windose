using System.Drawing;

public class DockPanel : Component
{
    public bool useBackground = false;
    public Color backgroundColor = Palette.ControlFace;

    private readonly List<Component> scratch = new List<Component>();

    public DockPanel(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
    }

    public Component AddDockChild(Component child, Dock dockStyle)
    {
        child.dock = dockStyle;     // must be set before AddChild so the anchor methods skip it
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
        // Snapshot under the same lock AddChild uses
        scratch.Clear();
        lock (children)
        {
            scratch.AddRange(children);
        }

        // Console.WriteLine($"Dock layout: {GetComponentName()} children={scratch.Count} size={Width}x{Height}");

        int left = Padding.left;
        int top = Padding.top;
        int right = Width - Padding.right;
        int bottom = Height - Padding.bottom;

        // Pass 1: edge docks, in child order
        for (int i = 0; i < scratch.Count; i++)
        {
            Component c = scratch[i];
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

        // Pass 2: fills share what's left (stacked vertically)
        int fills = 0;
        for (int i = 0; i < scratch.Count; i++)
            if (scratch[i].Visible && scratch[i].dock == Dock.Fill) fills++;

        for (int i = 0; i < scratch.Count; i++)
        {
            Component c = scratch[i];
            if (!c.Visible || c.dock != Dock.Fill) continue;

            Thickness m = c.Margin;
            int slice = Math.Max(0, bottom - top) / fills;
            Place(c, left + m.left, top + m.top,
                  right - left - m.left - m.right,
                  slice - m.top - m.bottom);
            top += slice;
            fills--;
        }

        scratch.Clear();
        MarkDirty();
    }

    private static void Place(Component c, int x, int y, int w, int h)  => c.SetBounds(x, y, Math.Max(1, w), Math.Max(1, h));

    public override void RemoveChild(Component child)
    {
        base.RemoveChild(child);
        ResolveDockLayout();
    }

    protected override void OnChildVisibilityChanged(Component child) => ResolveDockLayout();

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
