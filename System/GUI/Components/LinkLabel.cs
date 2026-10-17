using Cosmos.Kernel.System.Input;
using System.Drawing;

/// <summary>
/// A label that renders as a clickable hyperlink. Raises the <see cref="Clicked"/>
/// action when the user presses it. Supports hover highlighting and an underline.
/// </summary>
public class LinkLabel : Component
{
    public new Action leftClickAction;

    public Color linkColor = Color.FromArgb(0, 0, 255);
    public Color hoverColor = Color.FromArgb(0, 0, 128);
    public Color visitedColor = Color.FromArgb(128, 0, 128);
    public bool underline = true;

    private bool visited;
    private bool hovered;

    public bool Visited
    {
        get => visited;
        set { visited = value; MarkDirty(); }
    }

    public LinkLabel(string text, int x, int y, int width, int height) : base(x, y, width, height)
    {
        this.text = text;
        capturesInput = false;
        TabStop = true;
        horizontalAlignment = HorizontalAlignment.Left;
        verticalAlignment = VerticalAlignment.Center;
    }

    public override void DrawLocal()
    {
        if (string.IsNullOrEmpty(text)) return;

        Color color = visited ? visitedColor : (hovered ? hoverColor : linkColor);
        Rectangle content = GetContentBounds();

        int y = content.Y;
        if (verticalAlignment == VerticalAlignment.Center) y = content.Y + (content.Height - 16) / 2;
        else if (verticalAlignment == VerticalAlignment.Bottom) y = content.Bottom - 16;

        DrawString(text, color, content.X, y);

        if (underline)
        {
            int textWidth = MeasureStringWidth(text, 16);
            DrawLine(color, content.X, y + 16, textWidth, 1);
        }
    }

    public override void Update()
    {
        base.Update();
        bool isInside = IsInsideAbsolute(MouseManager.X, MouseManager.Y);
        if (isInside && !hovered)
        {
            hovered = true;
            MarkDirty();
        }
        else if (!isInside && hovered)
        {
            hovered = false;
            MarkDirty();
        }
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInputEnabled()) return false;
        if (IsInsideAbsolute(mouseX, mouseY) && mouse.left == MouseEvents.Release)
        {
            visited = true;
            MarkDirty();
            leftClickAction?.Invoke();
            return true;
        }
        return base.HandleInput(mouseX, mouseY, mouse);
    }

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        if (!IsInputEnabled()) return;
        if (keyEvent.Key == Key.Enter || keyEvent.Key == Key.Spacebar)
        {
            visited = true;
            MarkDirty();
            leftClickAction?.Invoke();
        }
    }

    public override string GetComponentName() => "LinkLabel";
}
