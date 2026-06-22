using System.Drawing;
using Cosmos.Kernel.System.Keyboard;
using Windose;

public class TextField : Component
{
    public bool useBackground = true;
    public bool truncate = true;
    public bool readOnly;

    public int fontSize = 0;
    public Color textColor = Color.Black;

    public TextField(int x, int y, int width, int height) : base(x, y, width, height)
    {
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


        int effectiveFontSize = fontSize > 0 ? fontSize : Math.Max(1, Height - 4);
        int textY = Math.Max(0, (Height - MeasureStringHeight(effectiveFontSize)) / 2);
        string visibleText = text;
        if (visibleText != "")
        {

            if (MeasureStringWidth(visibleText, effectiveFontSize) >= Width && truncate)
            {
                int maxCharacters = Math.Max(0, (Width - MeasureStringWidth("...", effectiveFontSize) - 4) / Math.Max(1, MeasureStringWidth("W", effectiveFontSize)));

                if (visibleText.Length > maxCharacters)
                    visibleText = visibleText.Substring(0, maxCharacters) + "...";
            }
            DrawString(visibleText, textColor, 2, textY, effectiveFontSize);
        }

        if (!readOnly)
            DrawString("_", MeasureStringWidth(visibleText, effectiveFontSize), textY);
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        return true;
    }

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        if (readOnly) return;

        if (KeyboardManager.ControlPressed)
        {
            if (keyEvent.Key == ConsoleKeyEx.C)
            {
                WindoseClipboard.SetText(text);
                return;
            }
            if (keyEvent.Key == ConsoleKeyEx.X)
            {
                WindoseClipboard.SetText(text);
                if (text.Length == 0) return;
                text = "";
                MarkDirty();
                return;
            }
            if (keyEvent.Key == ConsoleKeyEx.V)
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
            case ConsoleKeyEx.Backspace:
                if (text.Length != 0)
                {
                    text = text.Substring(0, text.Length - 1);
                    changed = true;
                }
                break;

            case ConsoleKeyEx.Enter:
                text += "\n";
                changed = true;
                break;

            default:
                if (keyEvent.KeyChar != '\0')
                {
                    text += keyEvent.KeyChar;
                    changed = true;
                }
                break;
        }

        if (changed)
            MarkDirty();
    }

    public override string GetName() => "TextField";
}
