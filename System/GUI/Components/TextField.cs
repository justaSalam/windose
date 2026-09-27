using System.Drawing;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Timer;


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
    private Label label;
    //private SoftwareTimer cursorTimer;


    private TrueTypeFont font = new TrueTypeFont("/mnt/System/Fonts/ARIAL.ttf");

    public TextField(int x, int y, int width, int height = 20) : base(x, y, width, height)
    {
        TimerManager.ScheduleRecurring(() =>
        {
            cursorVisible = !cursorVisible;
            MarkDirty();
        }, TimeSpan.FromMilliseconds(500));


        label = new Label(0, 0, width, height)
        {
            text = text,
            useBackground = false,
            useForeground = false,
            textColor = textColor,
            leftClickAction = leftClickAction,
            horizontalTextAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Center
        };

        AddChild(label);
    }



    public override void Draw()
    {
        base.Draw();
    }

    public override void DrawLocal()
    {

        if (useBackground)
        {
            DrawSunkenRectangle(0, 0, Width, Height);
        }


        int effectiveFontSize = font.SizePx > 0 ? font.SizePx : Math.Max(1, Height - 4);
        int textY = Math.Max(0, (Height - font.SizePx) / 2);

        string visibleText = label.text;
        int stringWidth = font.MeasureString(visibleText);


        if (visibleText != "")
        {

            if (stringWidth >= Width && truncate)
            {
                int maxCharacters = Math.Max(0, (Width - font.MeasureString("...", effectiveFontSize) - 4) / Math.Max(1, font.MeasureString("W", effectiveFontSize)));

                if (visibleText.Length > maxCharacters)
                    visibleText = visibleText.Substring(0, maxCharacters) + "...";
            }
            //DrawString(visibleText, textColor, 2, textY, effectiveFontSize);
        }

        DrawChild(label);

        if (!readOnly && cursorVisible && selected)
            DrawString("_", Color.Black, font.MeasureString(visibleText), textY);
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
            if (keyEvent.Key == ConsoleKeyEx.C)
            {
                WindoseClipboard.SetText(label.text);
                return;
            }
            if (keyEvent.Key == ConsoleKeyEx.X)
            {
                WindoseClipboard.SetText(label.text);
                if (label.text.Length == 0) return;
                label.text = "";
                MarkDirty();
                return;
            }
            if (keyEvent.Key == ConsoleKeyEx.V)
            {
                if (!WindoseClipboard.HasText) return;
                label.text += WindoseClipboard.Text;
                MarkDirty();
                return;
            }
            return;
        }

        bool changed = false;
        switch (keyEvent.Key)
        {
            case ConsoleKeyEx.Backspace:
                if (text.Length != 0)
                {
                    label.text = label.text.Substring(0, label.text.Length - 1);
                    changed = true;
                }
                break;

            default:
                char printable = GetPrintableCharacter(keyEvent);
                if (printable != '\0')
                {
                    label.text += printable;
                    changed = true;
                }
                break;
        }

        if (changed)
            MarkDirty();
    }

    public override string GetComponentName() => "TextField";
}
