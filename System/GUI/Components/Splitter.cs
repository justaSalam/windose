public class Splitter : Component
{
    public LayoutOrientation orientation = LayoutOrientation.Vertical;
    public Action<int> moved;

    private bool dragging;
    private int dragStart;
    private int originalPosition;

    public Splitter(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
    }

    public override void Draw()
    {
        base.Draw();
    }

    public override void DrawLocal()
    {
        DrawFilledRectangle(Palette.ControlFace, 0, 0, Width, Height);

        if (orientation == LayoutOrientation.Vertical)
        {
            int x = Width / 2;
            DrawLine(Palette.ControlShadow, x, 0, x, Height - 1);
            DrawLine(Palette.ControlWhite, x + 1, 0, x + 1, Height - 1);
        }
        else
        {
            int y = Height / 2;
            DrawLine(Palette.ControlShadow, 0, y, Width - 1, y);
            DrawLine(Palette.ControlWhite, 0, y + 1, Width - 1, y + 1);
        }
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        switch (mouse.left)
        {
            case MouseEvents.Press:
                if (!IsInsideAbsolute(mouseX, mouseY)) return false;

                dragging = true;
                dragStart = orientation == LayoutOrientation.Vertical ? mouseX : mouseY;
                originalPosition = orientation == LayoutOrientation.Vertical ? X : Y;
                return true;

            case MouseEvents.Hold:
                if (!dragging) return IsInsideAbsolute(mouseX, mouseY);

                int current = orientation == LayoutOrientation.Vertical ? mouseX : mouseY;
                int delta = current - dragStart;
                int newPosition = originalPosition + delta;

                if (orientation == LayoutOrientation.Vertical)
                    X = newPosition;
                else
                    Y = newPosition;

                moved?.Invoke(newPosition);
                MarkDirty();
                return true;

            case MouseEvents.Release:
            case MouseEvents.None:
                dragging = false;
                return IsInsideAbsolute(mouseX, mouseY);
        }

        return IsInsideAbsolute(mouseX, mouseY);
    }

    public override string GetComponentName() => "Splitter";
}
