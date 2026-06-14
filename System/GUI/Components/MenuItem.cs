using System.Drawing;

public class MenuItem : Component
{
    public Action click;
    public int fontSize = 16;
    public Color textColor = Palette.ControlBlack;
    public Color disabledTextColor = Palette.ControlShadow;
    public bool enabled = true;
    public bool drawSeparator;
    public bool closeParentOnClick = true;
    public MenuPopup submenu;

    private bool isPressed;

    public MenuItem(int x, int y, int width, int height) : base(x, y, width, height)
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

        if (drawSeparator)
        {
            int y = Height / 2;
            DrawLine(Palette.ControlShadow, 2, y, Width - 3, y);
            DrawLine(Palette.ControlWhite, 2, y + 1, Width - 3, y + 1);
            return;
        }

        bool hasSubmenu = submenu != null;
        bool highlighted = enabled && (IsInsideAbsolute(Cosmos.Kernel.System.Mouse.MouseManager.X, Cosmos.Kernel.System.Mouse.MouseManager.Y) || (hasSubmenu && submenu.Visible));
        int textX = isPressed ? 9 : 8;
        int textY = Math.Max(0, (Height - MeasureStringHeight(fontSize)) / 2);

        if (highlighted)
        {
            DrawFilledRectangle(Palette.Highlight, 2, 1, Math.Max(1, Width - 4), Math.Max(1, Height - 2));
            DrawString(text, Palette.HighlightText, textX, textY, fontSize);
        }
        else
        {
            Color color = enabled ? textColor : disabledTextColor;
            DrawString(text, color, textX, textY, fontSize);
        }

        if (hasSubmenu)
        {
            Color arrowColor = highlighted ? Palette.HighlightText : Palette.ControlBlack;
            int arrowX = Width - 12;
            int arrowY = Math.Max(3, Height / 2 - 3);

            DrawLine(arrowColor, arrowX, arrowY, arrowX, arrowY + 6);
            DrawLine(arrowColor, arrowX + 1, arrowY + 1, arrowX + 1, arrowY + 5);
            DrawLine(arrowColor, arrowX + 2, arrowY + 2, arrowX + 2, arrowY + 4);
            DrawLine(arrowColor, arrowX + 3, arrowY + 3, arrowX + 3, arrowY + 3);
        }
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (submenu != null && submenu.Visible && submenu.IsInsideAbsolute(mouseX, mouseY))
            return submenu.HandleInput(mouseX, mouseY, mouse);

        if (!enabled || drawSeparator)
            return IsInsideAbsolute(mouseX, mouseY);

        bool inside = IsInsideAbsolute(mouseX, mouseY);

        switch (mouse.left)
        {
            case MouseEvents.Press:
                isPressed = true;
                MarkDirty();
                return true;

            case MouseEvents.Release:
                bool wasPressed = isPressed;
                isPressed = false;
                MarkDirty();

                if (wasPressed && IsInsideAbsolute(mouseX, mouseY))
                {
                    if (submenu != null)
                    {
                        ToggleSubmenu();
                    }
                    else
                    {
                        click?.Invoke();

                        if (closeParentOnClick)
                            CloseMenuChain();
                    }
                }

                return true;

            case MouseEvents.None:
                if (isPressed)
                {
                    isPressed = false;
                    MarkDirty();
                }
                return inside || (submenu != null && submenu.Visible);
        }

        return true;
    }

    public MenuPopup CreateSubmenu(int width = 160)
    {
        submenu = new MenuPopup(width, 24);
        submenu.Visible = false;
        return submenu;
    }

    public MenuItem AddSubmenuItem(string text, Action click = null)
    {
        if (submenu == null)
            CreateSubmenu();

        return submenu.AddItem(text, click);
    }

    public MenuItem AddSubmenuSeparator()
    {
        if (submenu == null)
            CreateSubmenu();

        return submenu.AddSeparator();
    }

    private void ToggleSubmenu()
    {
        if (submenu.Visible)
            submenu.Hide();
        else
            ShowSubmenu();

        MarkDirty();
    }

    private void ShowSubmenu()
    {
        submenu.ShowAt(AbsoluteX + Width - 2, AbsoluteY - 2);
        MarkDirty();
    }

    private void CloseMenuChain()
    {
        MenuPopup.HideAll();

        if (Explorer.startMenu != null)
        {
            Explorer.startMenu.Visible = false;
            Explorer.startMenu.MarkDirty();
        }
    }

    public override string GetName() => "MenuItem";
}
