using System.Drawing;
using Windose;

public class ComboBox : Component
{
    public List<object> items = new List<object>();
    private int selectedIndex = -1;
    private bool isDroppedDown;
    private float hoverBlend;
    private bool isPressed;
    private readonly int collapsedHeight = 25;
    private MenuPopup dropDown;

    public bool useBorders = true;
    public Color borderColor = Palette.ControlShadow;
    public Color textColor = Palette.ControlBlack;
    public Color dropDownBackColor = Palette.ControlWhite;
    public Color highlightColor = Palette.Highlight;
    public Color highlightTextColor = Palette.HighlightText;
    public int fontSize = 0;
    public int dropDownMaxHeight = 200;

    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < -1 || value >= items.Count) return;
            if (selectedIndex == value) return;

            selectedIndex = value;
            MarkDirty();
            SelectedIndexChanged?.Invoke(selectedIndex);
        }
    }

    public object SelectedItem
    {
        get => selectedIndex >= 0 && selectedIndex < items.Count ? items[selectedIndex] : null;
        set
        {
            int index = items.IndexOf(value);
            if (index >= 0) SelectedIndex = index;
        }
    }

    public string SelectedText
    {
        get => selectedIndex >= 0 && selectedIndex < items.Count ? items[selectedIndex].ToString() : "";
    }

    public event Action<int> SelectedIndexChanged;
    public event Action<int> ItemClicked;

    public ComboBox(int x, int y, int width) : base(x, y, width, 25)
    {
        clampSize = false;
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, collapsedHeight);

        if (dropDown == null) return;

        dropDown.itemWidth = Width;
        RebuildDropDown();
    }

    public void AddItem(object item)
    {
        items.Add(item);
        RebuildDropDown();
        MarkDirty();
    }

    public void AddRange(IEnumerable<object> newItems)
    {
        foreach (object item in newItems)
            items.Add(item);
        RebuildDropDown();
        MarkDirty();
    }

    public void ClearItems()
    {
        items.Clear();
        selectedIndex = -1;
        SetDroppedDown(false);
        RebuildDropDown();
        MarkDirty();
    }

    public int ItemCount => items.Count;

    private int DropDownHeight => Math.Min(items.Count * collapsedHeight + 4, dropDownMaxHeight);

    public object GetItemAt(int index)
    {
        return index >= 0 && index < items.Count ? items[index] : null;
    }

    public override void Update()
    {
        if (dropDown != null && !dropDown.Visible && isDroppedDown)
            isDroppedDown = false;

        base.Update();

        float target = state == State.Highlighted || isPressed ? 1f : 0f;
        if (Math.Abs(hoverBlend - target) < 0.01f)
        {
            hoverBlend = target;
            return;
        }

        float step = (float)Math.Clamp(Kernel.DeltaTimeMs / 120.0, 0.02, 0.35);
        hoverBlend += target > hoverBlend ? step : -step;
        hoverBlend = Math.Clamp(hoverBlend, 0f, 1f);
        MarkDirty();
    }

    public override void DrawLocal()
    {
        int arrowWidth = 18;
        int textAreaWidth = Width - arrowWidth;
        int boxHeight = collapsedHeight;

        // Classic: raised combo box
        DrawRaisedRectangle(0, 0, Width, boxHeight);

        // Dropdown arrow
        DrawFilledRectangle(Palette.ControlFace, textAreaWidth + 1, 1, arrowWidth - 2, boxHeight - 2);
        DrawRectangle(Palette.ControlShadow, textAreaWidth, 0, arrowWidth, boxHeight);

        int arrowCenterX = textAreaWidth + arrowWidth / 2;
        int arrowCenterY = boxHeight / 2;
        DrawFilledTriangle(Palette.ControlBlack, arrowCenterX, arrowCenterY, 5);


        // Selected text
        string displayText = selectedIndex >= 0 && selectedIndex < items.Count
            ? items[selectedIndex].ToString()
            : text;
        if (displayText != "")
        {
            int effectiveFontSize = fontSize > 0 ? fontSize : Math.Max(1, boxHeight - 8);
            int textY = Math.Max(0, (boxHeight - MeasureStringHeight(effectiveFontSize)) / 2);
            DrawString(displayText, textColor, 4, textY, effectiveFontSize);
        }
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (mouse.left == MouseEvents.Press)
        {
            isPressed = true;
            MarkDirty();
            return true;
        }

        if (mouse.left == MouseEvents.Release)
        {
            isPressed = false;

            if (isDroppedDown)
            {
                SetDroppedDown(false);
            }
            else
            {
                SetDroppedDown(items.Count > 0);
            }

            MarkDirty();
            return true;
        }

        return true;
    }

    private void SetDroppedDown(bool droppedDown)
    {
        EnsureDropDown();

        if (isDroppedDown == droppedDown && dropDown.Visible == droppedDown)
        {
            return;
        }

        isDroppedDown = droppedDown;

        if (droppedDown)
            dropDown.ShowAt(AbsoluteX, AbsoluteY + collapsedHeight - 1);
        else
            dropDown.Hide();

        MarkDirty();
    }

    private void EnsureDropDown()
    {
        if (dropDown != null)
            return;

        dropDown = new MenuPopup(Width, DropDownHeight)
        {
            itemHeight = collapsedHeight,
        };
        RebuildDropDown();
    }

    private void RebuildDropDown()
    {
        if (dropDown == null)
            return;

        bool wasVisible = dropDown.Visible;
        dropDown.Hide();
        for (int i = dropDown.items.children.Count - 1; i >= 0; i--)
        {
            Component child = dropDown.items.children[i];
            dropDown.items.RemoveStackChild(child);
        }
        dropDown.Resize(Width, DropDownHeight);

        for (int i = 0; i < items.Count; i++)
        {
            int index = i;
            MenuItem item = dropDown.AddItem(items[i]?.ToString() ?? "", () =>
            {
                SelectedIndex = index;
                ItemClicked?.Invoke(index);
                isDroppedDown = false;
                MarkDirty();
            });
            item.fontSize = fontSize > 0 ? fontSize : 14;
        }

        if (wasVisible && items.Count > 0)
            dropDown.ShowAt(AbsoluteX, AbsoluteY + collapsedHeight - 1);
    }

    private void DrawFilledTriangle(Color color, int centerX, int centerY, int size)
    {
        for (int y = 0; y < size; y++)
        {
            int halfWidth = y * size / (size * 2);
            for (int x = -halfWidth; x <= halfWidth; x++)
            {
                int px = centerX + x;
                int py = centerY + y - size / 2;
                if (px >= 0 && px < Width && py >= 0 && py < Height)
                    buffer.SetPixelAlpha(px, py, color.ToArgb());
            }
        }
    }

    public override string GetComponentName() => "ComboBox";

    public override void Dispose()
    {
        dropDown?.Hide();
        dropDown?.Dispose();
        dropDown = null;
        base.Dispose();
    }
}
