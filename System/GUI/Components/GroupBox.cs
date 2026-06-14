using System.Drawing;

public class GroupBox : Component
{
    public int fontSize = 14;
    public Color textColor = Palette.ControlBlack;
    public Color backgroundColor = Palette.ControlFace;
    public bool useBackground = false;
    public StackOrientation orientation = StackOrientation.Vertical;

    private readonly StackPanel contentPanel;

    public GroupBox(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;

        contentPanel = new StackPanel(backgroundColor, 8, fontSize + 8, width - 16, height - fontSize - 16)
        {
            useBackground = false,
            useBorders = false,
            clampSize = false,
            orientation = orientation,
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0),
            Padding = new Thickness(4),
        };

        AddChild(contentPanel);
    }

    public void AddGroupChild(Component child)
    {
        contentPanel.orientation = orientation;
        contentPanel.AddStackChild(child);
        MarkDirty();
    }

    public void RemoveGroupChild(Component child)
    {
        contentPanel.RemoveStackChild(child);
        MarkDirty();
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        ResolveGroupLayout();
    }

    public override void Draw()
    {
        base.Draw();
    }

    public override void DrawLocal()
    {
        if (useBackground)
            DrawFilledRectangle(backgroundColor, 0, 0, Width, Height);

        DrawGroupBorder();

        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            child.DrawLocal();
            DrawChild(child);
            child.MarkCleaned();
        }
    }

    private void ResolveGroupLayout()
    {
        contentPanel.orientation = orientation;
        contentPanel.X = 8;
        contentPanel.Y = fontSize + 8;
        contentPanel.Resize(Math.Max(1, Width - 16), Math.Max(1, Height - fontSize - 16));
        contentPanel.ResolveStackLayout();
    }

    private void DrawGroupBorder()
    {
        int borderY = Math.Max(1, fontSize / 2);
        int labelX = 8;
        int labelPadding = 4;
        int textWidth = text == "" ? 0 : MeasureStringWidth(text, fontSize);
        int gapStart = text == "" ? labelX : Math.Max(0, labelX - labelPadding);
        int gapEnd = text == "" ? labelX : Math.Min(Width - 1, labelX + textWidth + labelPadding);

        DrawEtchedLine(0, borderY, gapStart, borderY);
        DrawEtchedLine(gapEnd, borderY, Width - 1, borderY);
        DrawEtchedLine(0, borderY, 0, Height - 1);
        DrawEtchedLine(0, Height - 1, Width - 1, Height - 1);
        DrawEtchedLine(Width - 1, borderY, Width - 1, Height - 1);

        if (text != "")
            DrawString(text, textColor, labelX, 0, fontSize);
    }

    private void DrawEtchedLine(int x1, int y1, int x2, int y2)
    {
        DrawLine(Palette.ControlShadow, x1, y1, x2, y2);
        DrawLine(Palette.ControlWhite, x1 + 1, y1 + 1, x2 + 1, y2 + 1);
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        return base.HandleInput(mouseX, mouseY, mouse);
    }

    public override string GetName() => "GroupBox";
}
