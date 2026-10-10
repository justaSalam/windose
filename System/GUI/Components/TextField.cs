using System.Drawing;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Input;
using Cosmos.Kernel.System.Timers;


/// <summary>
/// TODO Implement multi line fields
/// </summary>
public class TextField : Component
{
    public bool useBackground = true;
    public bool truncate = true;
    public bool readOnly;

    public Color textColor = Color.Black;
    private bool cursorVisible = true;
    private bool selected;

    private SoftwareTimer ?cursorTimer;


    private TrueTypeFont font = new TrueTypeFont("/mnt/System/Fonts/ARIAL.ttf");

    public TextField(int x, int y, int width, int height = 20) : base(x, y, width, height)
    {
        cursorTimer = TimerManager.ScheduleRecurring(() =>
        {
            cursorVisible = !cursorVisible;
            MarkDirty();
        }, TimeSpan.FromMilliseconds(500));
    }

    public override void DrawLocal()
    {
        if (useBackground)
        {
            DrawSunkenRectangle(0, 0, Width, Height);
        }

        int effectiveFontSize = font.SizePx > 0 ? font.SizePx : Math.Max(1, Height - Padding.top - Padding.bottom);
        Rectangle content = GetContentBounds();
        int textY = content.Y + Math.Max(0, (content.Height - effectiveFontSize) / 2);

        string displayText = text;
        if (displayText != "")
        {
            if (truncate)
                displayText = FitTextToWidth(displayText, content.Width, effectiveFontSize, font);

            DrawString(displayText, font, effectiveFontSize, textColor, content.X, textY);
        }

        if (!readOnly && cursorVisible && selected)
        {
            int cursorX = content.X + Math.Min(content.Width, font.MeasureString(displayText, effectiveFontSize));
            if (cursorX < content.Right)
                DrawString("_", font, effectiveFontSize, Color.Black, cursorX, textY);
        }
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {

        selected = base.HandleInput(mouseX, mouseY, mouse);
        return selected;
    }

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        if (readOnly) return;

        bool isControlPressed = IsControlPressed(keyEvent);

        if (isControlPressed)
        {
            if (keyEvent.Key == Key.C)
            {
                WindoseClipboard.SetText(text);
                return;
            }
            if (keyEvent.Key == Key.X)
            {
                WindoseClipboard.SetText(text);
                if (text.Length == 0) return;
                text = "";
                MarkDirty();
                return;
            }
            if (keyEvent.Key == Key.V)
            {
                if (!WindoseClipboard.HasText) return;
                text += WindoseClipboard.Text;
                MarkDirty();
                return;
            }
            return;
        }

        bool changed = false;
        switch (keyEvent.Key)
        {
            case Key.Backspace:
                if (text.Length != 0)
                {
                    text = text.Substring(0, text.Length - 1);
                    changed = true;
                }
                break;

            default:
                char printable = GetPrintableCharacter(keyEvent);
                if (printable != '\0')
                {
                    text += printable;
                    changed = true;
                }
                break;
        }

        if (changed)
            MarkDirty();
    }

    public override string GetComponentName() => "TextField";
}
