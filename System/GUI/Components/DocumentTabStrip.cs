using System.Drawing;

public class DocumentTabStrip : Component
{
    private readonly List<string> labels = new List<string>();
    private int pressedIndex = -1;
    public int selectedIndex;
    public int tabWidth = 150;
    public Action<int> tabSelected;

    public DocumentTabStrip(int x, int y, int width, int height = 26) : base(x, y, width, height)
    {
        clampSize = false;
        Margin = new Thickness(0);
    }

    public void SetTabs(List<string> values, int selected)
    {
        labels.Clear();
        if (values != null) labels.AddRange(values);
        selectedIndex = labels.Count == 0 ? -1 : Math.Max(0, Math.Min(labels.Count - 1, selected));
        MarkDirty();
    }

    public override void DrawLocal()
    {
        DrawFilledRectangle(Palette.ControlFace, 0, 0, Width, Height);
        DrawLine(Palette.ControlShadow, 0, Height - 1, Width - 1, Height - 1);

        int x = 2;
        for (int i = 0; i < labels.Count && x < Width - 2; i++)
        {
            int width = Math.Min(tabWidth, Width - x - 2);
            if (i == selectedIndex)
                DrawSunkenRectangle(x, 1, width, Height - 1);
            else
                DrawRaisedRectangle(x, 2, width, Height - 2);

            string label = Truncate(labels[i], Math.Max(1, width - 10));
            DrawString(label, Palette.ControlBlack, x + 5, 5, 16);
            x += tabWidth;
        }
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInsideAbsolute(mouseX, mouseY)) return false;
        int index = Math.Max(0, mouseX - AbsoluteX - 2) / Math.Max(1, tabWidth);
        if (index >= labels.Count) index = -1;

        if (mouse.left == MouseEvents.Press)
        {
            pressedIndex = index;
            return true;
        }

        if (mouse.left == MouseEvents.Release)
        {
            if (index >= 0 && index == pressedIndex)
            {
                selectedIndex = index;
                MarkDirty();
                tabSelected?.Invoke(index);
            }
            pressedIndex = -1;
            return true;
        }

        return true;
    }

    private string Truncate(string value, int availableWidth)
    {
        if (MeasureStringWidth(value, 16) <= availableWidth) return value;
        string result = value;
        while (result.Length > 1 && MeasureStringWidth(result + "...", 16) > availableWidth)
            result = result.Substring(0, result.Length - 1);
        return result + "...";
    }

    public override string GetComponentName() => "DocumentTabStrip";
}
