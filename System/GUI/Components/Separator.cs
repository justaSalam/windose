public class Separator : Component
{
    public LayoutOrientation orientation = LayoutOrientation.Vertical;

    public Separator(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
    }

    public override void Draw()
    {
        base.Draw();
    }

    public override void DrawLocal()
    {
        if (orientation == LayoutOrientation.Vertical)
        {
            int x = Width / 2;
            DrawLine(Palette.ControlShadow, x, 2, x, Height - 3);
            DrawLine(Palette.ControlWhite, x + 1, 2, x + 1, Height - 3);
        }
        else
        {
            int y = Height / 2;
            DrawLine(Palette.ControlShadow, 2, y, Width - 3, y);
            DrawLine(Palette.ControlWhite, 2, y + 1, Width - 3, y + 1);
        }
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        return IsInsideAbsolute(mouseX, mouseY);
    }

    public override string GetComponentName() => "Separator";
}
