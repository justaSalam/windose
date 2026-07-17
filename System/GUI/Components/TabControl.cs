using System.Drawing;
using Windose;

public class TabPage : Panel
{
    public string Text
    {
        get => text;
        set => text = value ?? "";
    }

    public TabPage(string text, int x, int y, int width, int height)
        : base(Palette.ControlWhite, x, y, width, height)
    {
        Text = text;
        clampSize = false;
        useBackground = false;
        Margin = new Thickness(0);
        Padding = new Thickness(4);
    }

    public List<Component> Controls => children;

    public void AddControl(Component control) => AddChild(control);

    public void RemoveControl(Component control)
    {
        RemoveChild(control);
    }
}

public class TabControl : Component
{
    private readonly List<TabPage> pages = new List<TabPage>();
    private int selectedIndex;
    private int hoveredTabIndex = -1;
    private float[] tabHoverBlends;
    private int tabBarHeight = 25;

    public Color tabBackColor = Palette.ControlFace;
    public Color tabActiveColor = Palette.ControlWhite;
    public Color tabInactiveColor = Palette.ControlFace;
    public Color tabBorderColor = Palette.ControlShadow;
    public Color textColor = Palette.ControlBlack;
    public Color activeTextColor = Palette.ControlBlack;
    public Color pageBackColor = Palette.ControlWhite;
    public int fontSize = 0;

    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < 0 || value >= pages.Count || value == selectedIndex) return;
            SwitchToPage(value);
        }
    }

    public TabPage SelectedPage
    {
        get => selectedIndex >= 0 && selectedIndex < pages.Count ? pages[selectedIndex] : null;
    }

    public int PageCount => pages.Count;

    public event Action<int> SelectedIndexChanged;

    public TabControl(int x, int y, int width, int height) : base(x, y, width, height)
    {
        tabHoverBlends = new float[0];
    }

    public TabPage AddPage(string text)
    {
        TabPage page = new TabPage(text, 0, tabBarHeight, Width, Math.Max(1, Height - tabBarHeight))
        {
            Visible = pages.Count == 0,
        };
        pages.Add(page);
        tabHoverBlends = new float[pages.Count];
        AddChild(page);

        if (pages.Count == 1)
        {
            selectedIndex = 0;
        }

        MarkDirty();
        return page;
    }

    public void RemovePage(TabPage page)
    {
        int index = pages.IndexOf(page);
        if (index < 0) return;

        pages[index].Dispose();
        RemoveChild(pages[index]);
        pages.RemoveAt(index);
        tabHoverBlends = new float[pages.Count];

        if (selectedIndex >= pages.Count)
            selectedIndex = pages.Count - 1;

        if (selectedIndex >= 0 && selectedIndex < pages.Count)
            pages[selectedIndex].Visible = true;

        MarkDirty();
        SelectedIndexChanged?.Invoke(selectedIndex);
    }

    public void RemovePageAt(int index)
    {
        if (index < 0 || index >= pages.Count) return;
        RemovePage(pages[index]);
    }

    public void ClearPages()
    {
        for (int i = 0; i < pages.Count; i++)
            RemoveChild(pages[i]);
        pages.Clear();
        tabHoverBlends = new float[0];
        selectedIndex = -1;
        MarkDirty();
    }

    private void SwitchToPage(int newIndex)
    {
        if (selectedIndex >= 0 && selectedIndex < pages.Count)
            pages[selectedIndex].Visible = false;

        selectedIndex = newIndex;

        if (selectedIndex >= 0 && selectedIndex < pages.Count)
            pages[selectedIndex].Visible = true;

        MarkDirty();
        SelectedIndexChanged?.Invoke(selectedIndex);
    }

    public override void Update()
    {
        base.Update();

        bool anyChanged = false;
        for (int i = 0; i < pages.Count; i++)
        {
            float target = i == hoveredTabIndex ? 1f : 0f;
            if (Math.Abs(tabHoverBlends[i] - target) < 0.01f)
            {
                tabHoverBlends[i] = target;
                continue;
            }

            float step = (float)Math.Clamp(Kernel.DeltaTimeMs / 120.0, 0.02, 0.35);
            tabHoverBlends[i] += target > tabHoverBlends[i] ? step : -step;
            tabHoverBlends[i] = Math.Clamp(tabHoverBlends[i], 0f, 1f);
            anyChanged = true;
        }

        if (anyChanged) MarkDirty();
    }

    public override void DrawLocal()
    {
        int tabAreaWidth = Width;
        int pageAreaY = tabBarHeight;
        int pageAreaHeight = Height - tabBarHeight;

        for (int i = 0; i < pages.Count; i++)
        {
            pages[i].X = 0;
            pages[i].Y = tabBarHeight;
            pages[i].Resize(Width, Math.Max(1, pageAreaHeight));
        }

        // Calculate tab widths
        int tabCount = pages.Count;
        if (tabCount == 0)
        {
            // Empty tab control - just draw the page area
            if (Palette.FlatControls)
            {
                DrawFilledRectangle(pageBackColor, 0, pageAreaY, Width, pageAreaHeight);
                DrawRectangle(tabBorderColor, 0, pageAreaY, Width, pageAreaHeight);
            }
            else
            {
                DrawSunkenRectangle(0, pageAreaY, Width, pageAreaHeight);
            }
            return;
        }

        int tabWidth = Math.Min(120, tabAreaWidth / tabCount);
        if (tabWidth < 40) tabWidth = Math.Max(40, tabAreaWidth / tabCount);

        // Draw page area first (behind tabs)
        if (Palette.FlatControls)
        {
            DrawFilledRectangle(pageBackColor, 0, pageAreaY, Width, pageAreaHeight);
            DrawRectangle(tabBorderColor, 0, pageAreaY, Width, pageAreaHeight);
        }
        else
        {
            DrawSunkenRectangle(0, pageAreaY, Width, pageAreaHeight);
        }

        // Draw tabs
        for (int i = 0; i < tabCount; i++)
        {
            int tabX = i * tabWidth;
            bool isActive = i == selectedIndex;

            Color tabColor;
            if (isActive)
                tabColor = Palette.FlatControls ? tabActiveColor : Palette.ControlFace;
            else
                tabColor = GUIFeatures.Blend(tabInactiveColor, Palette.Highlight, tabHoverBlends[i] * 0.08f);

            if (Palette.FlatControls)
            {
                DrawFilledRectangle(tabColor, tabX, 0, tabWidth, tabBarHeight);
                if (isActive)
                {
                    // Active tab: no bottom border (connects to page area)
                    DrawRectangle(tabBorderColor, tabX, 0, tabWidth, tabBarHeight);
                    // Clear the bottom border line for active tab
                    DrawLine(tabActiveColor, tabX + 1, tabBarHeight - 1, tabWidth - 2, 1);
                }
                else
                {
                    DrawRectangle(tabBorderColor, tabX, 0, tabWidth, tabBarHeight);
                }
            }
            else
            {
                if (isActive)
                {
                    DrawRaisedRectangle(tabX, 0, tabWidth, tabBarHeight + 1);
                }
                else
                {
                    DrawRaisedRectangle(tabX, 2, tabWidth, tabBarHeight - 1);
                }
            }

            // Tab text
            string tabText = pages[i].Text;
            if (tabText != "")
            {
                int effectiveFontSize = fontSize > 0 ? fontSize : 14;
                int textY = Math.Max(0, (tabBarHeight - MeasureStringHeight(effectiveFontSize)) / 2);
                Color color = isActive ? activeTextColor : textColor;
                DrawString(tabText, color, tabX + 4, textY, effectiveFontSize);
            }
        }

        if (selectedIndex >= 0 && selectedIndex < pages.Count)
            DrawChild(pages[selectedIndex]);
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        int localY = mouseY - AbsoluteY;

        // Check tab clicks
        if (localY < tabBarHeight)
        {
            int tabCount = pages.Count;
            if (tabCount == 0) return true;

            int tabWidth = Math.Min(120, Width / tabCount);
            if (tabWidth < 40) tabWidth = Math.Max(40, Width / tabCount);

            int localX = mouseX - AbsoluteX;
            int clickedTab = localX / tabWidth;

            if (clickedTab >= 0 && clickedTab < tabCount)
            {
                if (mouse.left == MouseEvents.Release)
                {
                    if (clickedTab != selectedIndex)
                        SwitchToPage(clickedTab);
                    return true;
                }

                if (hoveredTabIndex != clickedTab)
                {
                    hoveredTabIndex = clickedTab;
                    MarkDirty();
                }
                return true;
            }
        }

        if (hoveredTabIndex != -1)
        {
            hoveredTabIndex = -1;
            MarkDirty();
        }

        if (selectedIndex >= 0 && selectedIndex < pages.Count)
        {
            TabPage page = pages[selectedIndex];
            if (page.Visible && page.IsInsideAbsolute(mouseX, mouseY) && page.HandleInput(mouseX, mouseY, mouse))
                return true;
        }

        return IsInsideAbsolute(mouseX, mouseY);
    }

    public override string GetName() => "TabControl";
}
