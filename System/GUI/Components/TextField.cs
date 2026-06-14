using System.Drawing;
using Cosmos.Kernel.System.Keyboard;

public class TextField : Component
{
    public Color color1;

    public bool useBackground = true;
    public bool useBorders = false;
    public bool truncate = true;

    public Color borderColor = Color.White;
    public int fontSize = 0;
    public Color textColor = Color.Black;

    public TextField(Color color, int x, int y, int width, int height) : base(x, y, width, height)
    {
        color1 = color;
    }



    public override void Draw()
    {
        DrawLocal();
        base.Draw();
    }

    public override void DrawLocal()
    {

        if (useBackground)
        {
            DrawFilledRectangle(color1, 0, 0, Width, Height);
        }

        if (useBorders) DrawRectangle(borderColor, 0, 0, Width, Height);

        if (text != "")
        {
            int effectiveFontSize = fontSize > 0 ? fontSize : Math.Max(1, Height - 4);
            int textY = Math.Max(0, (Height - MeasureStringHeight(effectiveFontSize)) / 2);

            if (MeasureStringWidth(text, fontSize) >= Width && truncate)
            {
                text = text.Substring(0, text.Length - 3) + "...";
            }
            DrawString(text, textColor, 2, textY, effectiveFontSize);
        }
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        return true;
    }

    public override void HandleKeyboard(KeyEvent keyEvent)
    {

        switch (keyEvent.Key)
        {
            case ConsoleKeyEx.Backspace:
                if (text.Length != 0) text = text.Substring(0, text.Length - 1);
                break;

            case ConsoleKeyEx.Enter:
                text += "\n";
                break;

            default:
                text += keyEvent.KeyChar;
                break;
        }

        MarkDirty();
    }

    public override string GetName() => "Button";
}
