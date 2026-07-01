public class StatusBar : Component
{
    private readonly StackPanel panels;

    public StatusBar(int x, int y, int width, int height = 20) : base(x, y, width, height)
    {
        clampSize = false;

        panels = new StackPanel(Palette.ControlFace, 2, 2, width - 4, height - 4)
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

        AddChild(panels);
    }

    public Panel AddPanel(string text, int width = 120)
    {
        Panel panel = new Panel(Palette.ControlFace, 0, 0, width, Height - 4)
        {
            useBackground = true,
            useBorders = false,
            text = text,
            fontSize = 14,
            textColor = Palette.ControlBlack,
            clampSize = false,
            Margin = new Thickness(0),
        };

        panels.AddStackChild(panel);
        MarkDirty();
        return panel;
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        panels.Resize(Math.Max(1, width - 4), Math.Max(1, height - 4));
        panels.ResolveStackLayout();
    }

    public override void Draw()
    {
        base.Draw();
    }

    public override void DrawLocal()
    {
        DrawFilledRectangle(Palette.ControlFace, 0, 0, Width, Height);
        DrawLine(Palette.ControlWhite, 0, 0, Width - 1, 0);

        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            DrawChild(child);
        }

        for (int i = 0; i < panels.children.Count; i++)
        {
            Component child = panels.children[i];
            DrawSunkenRectangle(child.X + panels.X, child.Y + panels.Y, child.Width, child.Height);
        }
    }

    public override string GetName() => "StatusBar";
}
