using Cosmos.Kernel.System.Input;
using System.Drawing;

/// <summary>
/// Month calendar: navigate months, pick a day. Keyboard: arrows move a day/week, PgUp/PgDn a month.
/// </summary>
public class Calendar : Component
{
    private static readonly string[] MonthNames = new string[]
    {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    };
    private static readonly string[] DayNames = new string[] { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" };

    private DateTime selected = DateTime.UtcNow.Date;
    private DateTime visibleMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

    private readonly Button prevButton;
    private readonly Button nextButton;
    private readonly Label titleLabel;

    public event Action<DateTime> DateChanged;

    public DateTime SelectedDate
    {
        get => selected;
        set
        {
            DateTime day = value.Date;
            if (day == selected) return;
            selected = day;
            visibleMonth = new DateTime(day.Year, day.Month, 1);
            MarkDirty();
            DateChanged?.Invoke(selected);
        }
    }

    public Calendar(int x, int y, int width = 220, int height = 200) : base(x, y, width, height)
    {
        clampSize = false;
        TabStop = true;

        prevButton = new Button("<", 2, 2, 26, 22) { clampSize = false, Margin = new Thickness(0) };
        nextButton = new Button(">", width - 28, 2, 26, 22) { clampSize = false, Margin = new Thickness(0) };
        titleLabel = new Label(30, 2, width - 60, 22)
        {
            useBackground = false,
            clampSize = false,
            Margin = new Thickness(0),
            horizontalTextAlignment = HorizontalAlignment.Center,
            fontSize = 14,
        };
        prevButton.leftClickAction = () => { visibleMonth = visibleMonth.AddMonths(-1); MarkDirty(); };
        nextButton.leftClickAction = () => { visibleMonth = visibleMonth.AddMonths(1); MarkDirty(); };

        AddChild(prevButton);
        AddChild(nextButton);
        AddChild(titleLabel);
    }

    public override void DrawLocal()
    {
        DrawFilledRectangle(Palette.ControlWhite, 0, 0, Width, Height);
        DrawRectangle(Palette.ControlShadow, 0, 0, Width, Height);

        titleLabel.text = MonthNames[visibleMonth.Month - 1] + " " + visibleMonth.Year;

        int headerY = 28;
        int gridY = headerY + 16;
        int cols = 7;
        int cellW = (Width - 4) / cols;
        int cellH = Math.Max(14, (Height - gridY - 2) / 6);

        for (int d = 0; d < 7; d++)
            DrawAlignedText(DayNames[d], Palette.ControlBlack, 12,
                new Rectangle(2 + d * cellW, headerY, cellW, 14),
                HorizontalAlignment.Center, VerticalAlignment.Center);

        int days = DateTime.DaysInMonth(visibleMonth.Year, visibleMonth.Month);
        int firstDow = (int)new DateTime(visibleMonth.Year, visibleMonth.Month, 1).DayOfWeek;
        for (int day = 1; day <= days; day++)
        {
            int slot = firstDow + day - 1;
            int cx = 2 + (slot % cols) * cellW;
            int cy = gridY + (slot / cols) * cellH;
            bool isSelected = selected.Year == visibleMonth.Year && selected.Month == visibleMonth.Month && selected.Day == day;
            if (isSelected)
                DrawFilledRectangle(Palette.Highlight, cx, cy, cellW, cellH);
            DrawAlignedText(day.ToString(), isSelected ? Palette.HighlightText : Palette.ControlBlack, 14,
                new Rectangle(cx, cy, cellW, cellH),
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    private bool TryPickDay(int mouseX, int mouseY, out DateTime day)
    {
        day = selected;
        int headerY = 28;
        int gridY = headerY + 16;
        int cellW = (Width - 4) / 7;
        int cellH = Math.Max(14, (Height - gridY - 2) / 6);
        int lx = mouseX - AbsoluteX - 2;
        int ly = mouseY - AbsoluteY - gridY;
        if (lx < 0 || ly < 0) return false;
        int col = lx / Math.Max(1, cellW);
        int row = ly / Math.Max(1, cellH);
        if (col < 0 || col > 6 || row < 0 || row > 5) return false;
        int firstDow = (int)new DateTime(visibleMonth.Year, visibleMonth.Month, 1).DayOfWeek;
        int days = DateTime.DaysInMonth(visibleMonth.Year, visibleMonth.Month);
        int d = row * 7 + col - firstDow + 1;
        if (d < 1 || d > days) return false;
        day = new DateTime(visibleMonth.Year, visibleMonth.Month, d);
        return true;
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInputEnabled()) return false;
        base.HandleInput(mouseX, mouseY, mouse);
        if (mouse.left == MouseEvents.Release && TryPickDay(mouseX, mouseY, out DateTime day))
        {
            SelectedDate = day;
            return true;
        }
        return IsInsideAbsolute(mouseX, mouseY);
    }

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        if (!IsInputEnabled()) return;
        switch (keyEvent.Key)
        {
            case Key.LeftArrow: SelectedDate = selected.AddDays(-1); break;
            case Key.RightArrow: SelectedDate = selected.AddDays(1); break;
            case Key.UpArrow: SelectedDate = selected.AddDays(-7); break;
            case Key.DownArrow: SelectedDate = selected.AddDays(7); break;
            case Key.PageUp: SelectedDate = selected.AddMonths(-1); break;
            case Key.PageDown: SelectedDate = selected.AddMonths(1); break;
            case Key.Home: SelectedDate = new DateTime(selected.Year, selected.Month, 1); break;
        }
    }

    public override string GetComponentName() => "Calendar";
}
