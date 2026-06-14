using System.Drawing;

public class MenuPopup : Component
{
    private static readonly List<MenuPopup> openMenus = new List<MenuPopup>();

    public StackPanel items;
    public int itemWidth;
    public int itemHeight;

    public MenuPopup(int width, int height) : base(0, 0, width, height)
    {
        zLayer = DrawLayer.Popup;
        clampSize = false;
        Visible = false;
        itemWidth = width;
        itemHeight = 24;

        items = new StackPanel(Palette.ControlFace, 2, 2, width - 4, height - 4)
        {
            useBackground = false,
            useBorders = false,
            clampSize = false,
            orientation = StackOrientation.Vertical,
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(2),
            Padding = new Thickness(0),
        };

        AddChild(items);
    }

    public MenuItem AddItem(string text, Action click = null)
    {
        MenuItem item = new MenuItem(0, 0, itemWidth - 4, itemHeight)
        {
            text = text,
            click = click,
            clampSize = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0),
        };

        items.AddStackChild(item);
        ResizeToContent();
        return item;
    }

    public MenuItem AddSeparator()
    {
        MenuItem item = new MenuItem(0, 0, itemWidth - 4, 8)
        {
            drawSeparator = true,
            clampSize = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0),
        };

        items.AddStackChild(item);
        ResizeToContent();
        return item;
    }

    public void ShowAt(int x, int y)
    {
        X = x;
        Y = y;
        Visible = true;
        if (!openMenus.Contains(this))
            openMenus.Add(this);

        MarkDirty();
    }

    public void Hide()
    {
        Visible = false;
        openMenus.Remove(this);
        MarkDirty();
    }

    public static void HideAll()
    {
        for (int i = openMenus.Count - 1; i >= 0; i--)
            openMenus[i].Hide();

        openMenus.Clear();
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        items.Resize(Math.Max(1, width - 4), Math.Max(1, height - 4));
        items.ResolveStackLayout();
    }

    public override void Draw()
    {
        DrawLocal();
        DrawToScreen();
    }

    public override void DrawLocal()
    {
        DrawRaisedRectangle(0, 0, Width, Height);

        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            child.DrawLocal();
            buffer.DrawImageAlpha(child.GetBuffer(), child.X, child.Y);
            child.MarkCleaned();
        }
    }

    private void ResizeToContent()
    {
        int contentHeight = 4;

        for (int i = 0; i < items.children.Count; i++)
        {
            Component child = items.children[i];
            if (!child.Visible) continue;

            contentHeight += child.Height + child.Margin.top + child.Margin.bottom + items.spacing;
        }

        if (items.children.Count > 0)
            contentHeight -= items.spacing;

        Resize(itemWidth, Math.Max(itemHeight, contentHeight));
    }

    public override string GetName() => "MenuPopup";
}
