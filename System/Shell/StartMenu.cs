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
        programs.AddSubmenuItem("Accessories");
        programs.AddSubmenuItem("File Explorer", () =>
        {
            WindowManager.Register(new FileExplorer(100, 100, 800, 500, "Control Panel", true));

        });
        programs.AddSubmenuItem("File Properties", () =>
{
    WindowManager.Register(new FileProperties(400, 400, new FileEntry()));

});
        programs.AddSubmenuItem("Task Manager", () =>
        {
            WindowManager.Register(new PerformanceMonitor(180, 120));
        });
        programs.AddSubmenuItem("Breeze Demo", () =>
        {
            BreezeDemo.Run();
        });
        programs.AddSubmenuItem("Breeze Editor", () =>
        {
            WindowManager.Register(new BreezeEditor());
        });
        programs.AddSubmenuItem("Breeze API", () =>
        {
            WindowManager.Register(new BreezeApiBrowser());
        });
        programs.AddSubmenuItem("Run main.breeze", () =>
        {
            BreezeHost.RunFile(@"0:\Apps\main.breeze");
        });
        programs.AddSubmenuItem("System Tools");

        MenuItem documents = new MenuItem(0, 0, width, 24)
        {
            text = "Documents",
            fontSize = 16,
            horizontalAlignment = HorizontalAlignment.Stretch,
            clampSize = false,
            Margin = new Thickness(0)
        };

        MenuItem settings = new MenuItem(0, 0, width, 24)
        {
            text = "Settings",
            fontSize = 16,
            horizontalAlignment = HorizontalAlignment.Stretch,
            clampSize = false,
            Margin = new Thickness(0)
        };

        settings.AddSubmenuItem("Control Panel", () =>
        {
        });
        settings.AddSubmenuItem("Resource Manager");




        MenuItem powerOptions = new MenuItem(0, 0, width, 24)
        {
            text = "Power",
            fontSize = 16,
            horizontalAlignment = HorizontalAlignment.Stretch,
            clampSize = false,
            Margin = new Thickness(0)
        };
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
        panel.AddStackChild(documents);
        panel.AddStackChild(settings);
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
            DrawChild(child);
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
