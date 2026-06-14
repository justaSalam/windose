public class Toolbar : Component
{
    private readonly StackPanel items;

    public Toolbar(int x, int y, int width, int height = 28) : base(x, y, width, height)
    {
        clampSize = false;

        items = new StackPanel(Palette.ControlFace, 3, 3, width - 6, height - 6)
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

    public Button AddButton(string text, Action click = null, int width = 64)
    {
        Button button = new Button(0, 0, width, Height - 6)
        {
            text = text,
            fontSize = 16,
            useBorders = true,
            clampSize = false,
            Margin = new Thickness(0),
            leftMouseRelease = click,
        };

        items.AddStackChild(button);
        MarkDirty();
        return button;
    }

    public Separator AddSeparator(int width = 8)
    {
        Separator separator = new Separator(0, 0, width, Height - 8)
        {
            orientation = LayoutOrientation.Vertical,
            clampSize = false,
            Margin = new Thickness(0),
        };

        items.AddStackChild(separator);
        MarkDirty();
        return separator;
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        items.Resize(Math.Max(1, width - 6), Math.Max(1, height - 6));
        items.ResolveStackLayout();
    }

    public override void Draw()
    {
        base.Draw();
    }

    public override void DrawLocal()
    {
        DrawRaisedRectangle(0, 0, Width, Height);

        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            child.DrawLocal();
            DrawChild(child);
            child.MarkCleaned();
        }
    }

    public override string GetName() => "Toolbar";
}
