using System.Drawing;

public class GroupBox : Component
{

    public int fontSize = 14;
    public Color borderColor = Color.White;
    public Color textColor = Color.Black;
    public Color shadow = Color.Black;
    public Color highlight = Color.White;

    public GroupBox(Color color, int x, int y, int width, int height) : base(x, y, width, height)
    {
    }



    public override void Draw()
    {
        DrawLocal();
        base.Draw();
    }

    public override void DrawLocal()
    {
        int labelX = X + 8;
        int labelY = Y;
        int textWidth = MeasureStringWidth(text, 12);

        // top line, split so label has a gap
        DrawLine(shadow, X, Y + 7, labelX - 3, Y + 7);
        DrawLine(shadow, labelX + textWidth + 3, Y + 7, X + Width, Y + 7);

        // left/top shadow
        DrawLine(shadow, X, Y + 7, X, Y + Height);
        DrawLine(shadow, X, Y + Height, X + Width, Y + Height);

        // highlight offset gives etched look
        DrawLine(highlight, X + 1, Y + 8, X + 1, Y + Height - 1);
        DrawLine(highlight, X + 1, Y + Height - 1, X + Width - 1, Y + Height - 1);

        DrawString(text, Color.Black, labelX, labelY);
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        return true;
    }

    public override string GetName() => "Button";
}
