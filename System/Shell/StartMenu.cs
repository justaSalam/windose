using System.Drawing;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.System;
using Windose;

public class StartMenu : Window
{

    private StackPanel panel;
    public StartMenu(int x, int y, int width, int height, string title, bool useTitleBar) : base(x, y, width, height, title, useTitleBar)
    {
        zLayer = DrawLayer.Popup;
        Visible = false;
        canResize = false;
        canMove = false;

        panel = new StackPanel(Color.White, 0, 0, width, height)
        {
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            orientation = StackOrientation.Vertical,
            clampSize = false,
            Margin = new Thickness(0),
            Padding = new Thickness(4),
        };

        AddChild(panel);

        panel.AddStackChild(new Label(0, 0, width, 20)
        {
            verticalAlignment = VerticalAlignment.Top,
            horizontalAlignment = HorizontalAlignment.Stretch,
            text = $"Windose NativeAOT {Cosmos.Kernel.System.Kernel.VersionString}.",
            useBackground = false,

            fontSize = 16,
            clampSize = false,
            Margin = new Thickness(0)

        });

        MenuItem programs = new MenuItem(0, 0, width, 24)
        {
            text = "Programs",
            fontSize = 16,
            horizontalAlignment = HorizontalAlignment.Stretch,
            clampSize = false,
            Margin = new Thickness(0)
        };

        MenuItem powerOptions = new MenuItem(0, 0, width, 24)
        {
            text = "Power",
            fontSize = 16,
            horizontalAlignment = HorizontalAlignment.Stretch,
            clampSize = false,
            Margin = new Thickness(0)
        };

        programs.AddSubmenuItem("Accessories");
        programs.AddSubmenuItem("System Manager", () =>
        {
            WindowManager.Register(new SystemManager(100, 100, 250, 250, "System Manager", true));
        });
        programs.AddSubmenuSeparator();
        programs.AddSubmenuItem("Windose Shell");



        powerOptions.AddSubmenuItem("Log Off");
        powerOptions.AddSubmenuItem("Shutdown", () =>
        {
            GarbageCollector.Collect();
            Power.Shutdown();
        });

        powerOptions.AddSubmenuItem("Reboot", () =>
        {
            GarbageCollector.Collect();

            Power.Reboot();
        });

        powerOptions.AddSubmenuItem("PANIC", () =>
{

    Panic.Halt("Forced panic");
});

        panel.AddStackChild(programs);
        panel.AddStackChild(powerOptions);
        MarkDirty();
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

            child.DrawLocal();
            buffer.DrawImageAlpha(child.GetBuffer(), child.X, child.Y);
            child.MarkCleaned();
        }
    }

    public override void OnLoseFocus()
    {
        MenuPopup.HideAll();
        Visible = false;
        MarkDirty();
    }

    public void HideMenu()
    {
        MenuPopup.HideAll();
        Visible = false;
        MarkDirty();
    }


    public override string GetName() => "StartMenu";
}
