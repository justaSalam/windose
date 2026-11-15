using Cosmos.Kernel.System.Graphics;
using System.Drawing;

public class MenuPopup : Component
{
    private static readonly List<MenuPopup> openMenus = new List<MenuPopup>();

    public StackPanel items;
    public int itemWidth;
    public int itemHeight;
    public Png? Icon;

    public MenuPopup(int width, int height, Png? icon = null) : base(0, 0, width, height)
    {
        zLayer = DrawLayer.Popup;
        RegisterAsRoot();
        clampSize = false;
        Visible = false;
        itemWidth = width;
        itemHeight = 24;
        Icon = icon;

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
    public MenuItem AddItem(Image icon, string text, Action click = null)
    {
        MenuItem item = new MenuItem(icon, 0, 0, itemWidth - 4, itemHeight)
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

    public int keyboardSelectedIndex = -1;

    public void Hide()
    {
        Visible = false;
        openMenus.Remove(this);
        keyboardSelectedIndex = -1;
        for (int i = 0; i < items.children.Count; i++)
            if (items.children[i] is MenuItem mi) mi.isKeyboardSelected = false;
        MarkDirty();
    }

    public static void HideAll()
    {
        for (int i = openMenus.Count - 1; i >= 0; i--)
            openMenus[i].Hide();

        openMenus.Clear();
    }

    /// <summary>Shows only items matching the query (parents stay if any child matches).</summary>
    public void FilterItems(string query)
    {
        bool active = !string.IsNullOrEmpty(query);
        for (int i = 0; i < items.children.Count; i++)
        {
            MenuItem mi = items.children[i] as MenuItem;
            if (mi == null) continue;
            if (mi.submenu != null)
            {
                mi.submenu.FilterItems(query);
                mi.Visible = !active || mi.submenu.HasVisibleItems();
            }
            else if (mi.drawSeparator)
            {
                mi.Visible = !active;
            }
            else
            {
                mi.Visible = !active || mi.text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
        MarkDirty();
    }

    public bool HasVisibleItems()
    {
        for (int i = 0; i < items.children.Count; i++)
        {
            MenuItem mi = items.children[i] as MenuItem;
            if (mi != null && mi.Visible) return true;
        }
        return false;
    }

    public static bool HasOpenMenus => openMenus.Count > 0;

    public static bool HandleOpenMenuKeyboard(Cosmos.Kernel.System.Input.KeyEvent key)
    {
        if (openMenus.Count == 0) return false;
        MenuPopup topMenu = openMenus[openMenus.Count - 1];
        return topMenu.HandleMenuKey(key);
    }

    public bool HandleMenuKey(Cosmos.Kernel.System.Input.KeyEvent key)
    {
        List<MenuItem> visibleItems = new List<MenuItem>();
        for (int i = 0; i < items.children.Count; i++)
        {
            if (items.children[i] is MenuItem mi && mi.Visible && !mi.drawSeparator)
                visibleItems.Add(mi);
        }

        if (visibleItems.Count == 0) return false;

        switch (key.Key)
        {
            case Cosmos.Kernel.System.Input.Key.UpArrow:
                SelectMenuItem(visibleItems, keyboardSelectedIndex <= 0 ? visibleItems.Count - 1 : keyboardSelectedIndex - 1);
                return true;

            case Cosmos.Kernel.System.Input.Key.DownArrow:
                SelectMenuItem(visibleItems, (keyboardSelectedIndex + 1) % visibleItems.Count);
                return true;

            case Cosmos.Kernel.System.Input.Key.Enter:
            case Cosmos.Kernel.System.Input.Key.Spacebar:
                if (keyboardSelectedIndex >= 0 && keyboardSelectedIndex < visibleItems.Count)
                {
                    visibleItems[keyboardSelectedIndex].Trigger();
                    return true;
                }
                return false;

            case Cosmos.Kernel.System.Input.Key.RightArrow:
                if (keyboardSelectedIndex >= 0 && keyboardSelectedIndex < visibleItems.Count)
                {
                    MenuItem selected = visibleItems[keyboardSelectedIndex];
                    if (selected.submenu != null)
                    {
                        selected.Trigger();
                        return true;
                    }
                }
                return false;

            case Cosmos.Kernel.System.Input.Key.LeftArrow:
                if (openMenus.Count > 1)
                {
                    Hide();
                    return true;
                }
                return false;

            case Cosmos.Kernel.System.Input.Key.Escape:
                HideAll();
                return true;
        }

        char printable = GetPrintableCharacter(key);
        if (printable != '\0' && !char.IsControl(printable))
        {
            char target = char.ToUpperInvariant(printable);
            int start = keyboardSelectedIndex >= 0 ? keyboardSelectedIndex + 1 : 0;
            for (int offset = 0; offset < visibleItems.Count; offset++)
            {
                int check = (start + offset) % visibleItems.Count;
                string t = visibleItems[check].text ?? "";
                if (t.Length > 0 && char.ToUpperInvariant(t[0]) == target)
                {
                    SelectMenuItem(visibleItems, check);
                    return true;
                }
            }
        }

        return false;
    }

    private void SelectMenuItem(List<MenuItem> visibleItems, int idx)
    {
        for (int i = 0; i < visibleItems.Count; i++)
        {
            visibleItems[i].isKeyboardSelected = (i == idx);
            visibleItems[i].MarkDirty();
        }
        keyboardSelectedIndex = idx;
        MarkDirty();
    }

    public static bool HandleOpenMenuInput(int mouseX, int mouseY, MouseState mouse)
    {
        for (int i = openMenus.Count - 1; i >= 0; i--)
        {
            MenuPopup menu = openMenus[i];
            if (!menu.Visible || !menu.IsInsideAbsolute(mouseX, mouseY)) continue;

            menu.HandleInput(mouseX, mouseY, mouse);
            return true;
        }

        if (mouse.left == MouseEvents.Press)
            HideAll();

        return false;
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        items.Resize(Math.Max(1, width - 4), Math.Max(1, height - 4));
        items.ResolveStackLayout();
    }


    public override void DrawLocal()
    {
        if (Palette.FlatControls)
        {
            int r = 3;
            DrawFilledRoundedRectangle(Palette.MenuBackground, 0, 0, Width, Height, r);
            DrawRoundedRectangle(Color.FromArgb(170, 170, 170), 0, 0, Width, Height, r);
            if (Width > 4 && Height > 2)
                DrawHorizontalSpan(Color.FromArgb(255, 255, 255), 2, 1, Width - 4);
        }
        else
        {
            DrawRaisedRectangle(0, 0, Width, Height);
        }

        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            DrawChild(child);
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

    public override string GetComponentName() => "MenuPopup";
}
