using Cosmos.Kernel.System.Graphics;
using System.Drawing;


public sealed class ControlEnv : Window
{

    public ControlEnv(int x, int y) : base(x, y, 400, 250, "Control Test Environment", true)
    {
        AddChild(new Toolbar(0, 0, 100));
        AddChild(new Label(0, 20, 100, 20));
        AddChild(new TextField(0, 40, 100, 20));
        AddChild(new Checkbox(0, 60));
        AddChild(new ComboBox(0, 80, 100));
        AddChild(new StatusBar(0, 100, 100));
        AddChild(new TreeView(0, 120, 100, 100));

    }

}