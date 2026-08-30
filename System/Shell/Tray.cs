using Cosmos.Kernel.System.Graphics;
using System.Drawing;

public class Tray : Window
{

    private GridPanel panel;
    public Tray(int x, int y) : base(x, y, 160, 160, "Taskbar Tray", false)
    {
        zLayer = DrawLayer.Popup;
        Visible = false;
        canResize = false;
        canMove = false;
        showInTaskbar = false;

        panel = new GridPanel(0, 0, Width, Height)
        {
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            cellHeight = 32,
            cellWidth = 32,
            spacing = 4,
            Padding = new Thickness(8)

        };
        AddChild(panel);


        panel.AddGridChild(new Button(new Png("/mnt/System/Icons/cable.png"),0, 0, 32, 32));
        panel.AddGridChild(new Button(new Png("/mnt/System/Icons/cable.png"),0, 0, 32, 32));
        panel.AddGridChild(new Button(new Png("/mnt/System/Icons/cable.png"),0, 0, 32, 32));
        panel.AddGridChild(new Button(new Png("/mnt/System/Icons/cable.png"),0, 0, 32, 32));
        panel.AddGridChild(new Button(new Png("/mnt/System/Icons/cable.png"),0, 0, 32, 32));
        panel.AddGridChild(new Button(new Png("/mnt/System/Icons/cable.png"),0, 0, 32, 32));
        panel.AddGridChild(new Button(new Png("/mnt/System/Icons/cable.png"),0, 0, 32, 32));
        panel.AddGridChild(new Button(new Png("/mnt/System/Icons/cable.png"),0, 0, 32, 32));
    }



    public override void Update()
    {
        base.Update();
    }


    public override void Draw()
    {
        DrawLocal();
        DrawToScreen();
    }

    public override void DrawLocal()
    {

        DrawRaisedRectangle(0, 0, Width, Height);


        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            DrawChild(child);
        }
    }


    public override void OnLoseFocus()
    {
        Visible = false;
    }
    public override string GetComponentName() => "Tray";
}
