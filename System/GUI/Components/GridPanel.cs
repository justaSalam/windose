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

    public T AddGridChild<T>(T child) where T : Component
    {
        AddChild((Component)child);
        return child;
    }

    public override Component AddChild(Component child)
    {
        Component added = base.AddChild(child);
        ResolveGridLayout();
        return added;
    }

    public override void RemoveChild(Component child)
    {
        base.RemoveChild(child);
        ResolveGridLayout();
    }

    protected override void OnChildVisibilityChanged(Component child) => ResolveGridLayout();

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        ResolveGridLayout();
    }

    public void ResolveGridLayout()
    {
        int availableWidth = Math.Max(1, Width - Padding.left - Padding.right);
        int cellGap = Math.Max(0, spacing);
        int preferredCellWidth = Math.Max(1, cellWidth);
        int columns = (int)Math.Max(1, (availableWidth + (long)cellGap) / ((long)preferredCellWidth + cellGap));
        int actualCellWidth = Math.Max(1, (availableWidth - cellGap * (columns - 1)) / columns);
        if (actualCellWidth > 1 && actualCellWidth % 2 != 0)
            actualCellWidth--;
        int actualCellHeight = Math.Max(1, cellHeight);
        int visibleIndex = 0;

        for (int i = 0; i < children.Count; i++)
        {
            Component child = children[i];
            if (!child.Visible) continue;

            int column = visibleIndex % columns;
            int row = visibleIndex / columns;

            int cellX = Padding.left + column * (actualCellWidth + cellGap);
            int cellY = Padding.top + row * (actualCellHeight + cellGap);
            int childWidth = Math.Max(1, actualCellWidth - child.Margin.left - child.Margin.right);
            int childHeight = Math.Max(1, actualCellHeight - child.Margin.top - child.Margin.bottom);
            int childX = cellX + child.Margin.left;
            int childY = cellY + child.Margin.top;
            child.SetBounds(childX, childY, childWidth, childHeight);
            child.MarkDirty();
            visibleIndex++;
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
