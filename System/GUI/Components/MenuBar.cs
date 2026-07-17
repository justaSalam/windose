public class MenuBar : Component
{
    private readonly StackPanel items;

    public MenuBar(int x, int y, int width, int height = 22) : base(x, y, width, height)
    {
        clampSize = false;

        items = new StackPanel(Palette.ControlFace, 2, 1, width - 4, height - 2)
        {
            useBackground = false,
            useBorders = false,
            clampSize = false,
            orientation = StackOrientation.Horizontal,
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            spacing = 2,
        };

        AddChild(items);
    }

    public MenuItem AddMenu(string text, Action click = null)
    {
        int width = Math.Max(40, MeasureStringWidth(text, 16) + 16);
        MenuItem item = new MenuItem(0, 0, width, Height - 2)
        {
            text = text,
            click = click,
            useRightClick = false,
            fontSize = 16,
            clampSize = false,
            Margin = new Thickness(0),
        };

        items.AddStackChild(item);
        MarkDirty();
        return item;
    }

    public MenuPage AddMenuPage(string text, int width = 180)
    {
        MenuItem item = AddMenu(text);
        MenuPage page = new MenuPage(width);

        item.submenu = page;
        item.openSubmenuBelow = true;
        item.drawSubmenuArrow = false;
        item.closeOtherMenusOnOpen = true;

        return page;
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        items.Resize(Math.Max(1, width - 4), Math.Max(1, height - 2));
        items.ResolveStackLayout();
    }

    public override void Draw()
    {
        base.Draw();
    }

    public override void DrawLocal()
    {
        DrawFilledRectangle(Palette.ControlFace, 0, 0, Width, Height);
        DrawLine(Palette.ControlWhite, 0, 0, Width - 1, 0);
        DrawLine(Palette.ControlShadow, 0, Height - 1, Width - 1, Height - 1);

        DrawChildren();
    }

    protected void DrawChildren()
    {
        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            DrawChild(child);
        }
    }

    public override string GetName() => "MenuBar";
}
