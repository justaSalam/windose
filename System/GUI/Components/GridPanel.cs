using System.Drawing;

public class GridPanel : Component
{
    public int cellWidth = 80;
    public int cellHeight = 72;
    public int spacing = 8;
    public bool useBackground = false;
    public Color backgroundColor = Palette.ControlWhite;

    public GridPanel(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
    }

    public void AddGridChild(Component child)
    {
        AddChild(child);
        ResolveGridLayout();
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        ResolveGridLayout();
    }

    public void ResolveGridLayout()
    {
        int availableWidth = Math.Max(1, Width - Padding.left - Padding.right);
        int columns = Math.Max(1, (availableWidth + spacing) / Math.Max(1, cellWidth + spacing));

        for (int i = 0; i < children.Count; i++)
        {
            Component child = children[i];
            if (!child.Visible) continue;

            int column = i % columns;
            int row = i / columns;

            child.X = Padding.left + column * (cellWidth + spacing);
            child.Y = Padding.top + row * (cellHeight + spacing);
            child.Resize(cellWidth, cellHeight);
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

    public override string GetComponentName() => "GridPanel";

    public override bool IsOpaqueForCopy() => useBackground;
}
