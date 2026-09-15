

namespace Windose.Programs
{
    internal class MessageWindow : Window
    {

        public MessageWindow(int x, int y, string source, string message) : base(x, y, 800, 600, source, true, null)
        {
            Panel text = new Panel(Palette.ControlFace, 8, 34, 664, 48)
            {
                text = message,
                fontSize = 16,
                textColor = Palette.ControlBlack,
                useBackground = true,
                horizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(8, 34, 8, 8),

            };
            AddChild(text);
        }
    }
}
