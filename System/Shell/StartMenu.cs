using System.Drawing;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.System;
using Windose;

public class StartMenu : Window
{

    private StackPanel panel;
    private readonly Rectangle homeBounds;
    public StartMenu(int x, int y, int width, int height, string title, bool useTitleBar) : base(x, y, width, height, title, useTitleBar)
    {
        zLayer = DrawLayer.Popup;
        Visible = false;
        canResize = false;
        canMove = false;
        showInTaskbar = false;

        panel = new StackPanel(Palette.MenuBackground, 0, 0, width, height)
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
            WindowManager.Register(new FileExplorer(100, 100, 800, 500, "File Explorer", true));

        });
        programs.AddSubmenuItem("File Properties", () =>
{
    WindowManager.Register(new FileProperties(400, 400, new FileEntry()));

});
        programs.AddSubmenuItem("Task Manager", () =>
        {
            WindowManager.Register(new PerformanceMonitor(180, 120));
        });
        MenuItem breeze = programs.AddSubmenuItem("Breeze");
        breeze.AddSubmenuItem("Breeze Editor", () =>
        {
            WindowManager.Register(new BreezeEditor());
        });
        breeze.AddSubmenuItem("Breeze API", () =>
        {
            WindowManager.Register(new BreezeApiBrowser());
        });
        breeze.AddSubmenuItem("Breeze Demo", () =>
        {
            BreezeDemo.Run();
        });
        breeze.AddSubmenuItem("Run main.breeze", () =>
        {
            BreezeHost.RunFile(@"0:\Apps\main.breeze");
        });
        programs.AddSubmenuItem("Command Prompt", () =>
        {
            WindowManager.Register(new CommandPrompt());
        });
        MenuItem systemTools = programs.AddSubmenuItem("System Tools");
        systemTools.AddSubmenuItem("Registry Editor", () =>
        {
            WindowManager.Register(new RegistryEditor());
        });

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


        settings.AddSubmenuItem("Theme Settings", () =>
        {
            HideMenuImmediate();
            WindowManager.Register(new ThemeSettings());
        });

        settings.AddSubmenuItem("Control Panel", () =>
        {
            FileExplorer explorer =
                new FileExplorer(100, 100, 700, 480, "Control Panel", true);

            explorer.NavigateToPath("control");
            WindowManager.Register(explorer);
        });




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
            HideMenuImmediate();
            SystemPowerManager.RequestShutdown();
        });

        powerOptions.AddSubmenuItem("Reboot", () =>
        {
            HideMenuImmediate();
            SystemPowerManager.RequestReboot();
        });

        powerOptions.AddSubmenuItem("PANIC", () =>
{
    KernelPanic.Show("MANUALLY_INITIATED_CRASH", "The crash screen was started from the system menu.");
});


        panel.AddStackChild(programs);
        panel.AddStackChild(documents);
        panel.AddStackChild(settings);
        panel.AddStackChild(powerOptions);
        homeBounds = bounds;
        MarkDirty();
    }

    public Rectangle HomeBounds => homeBounds;

    public bool AtHomePosition()
    {
        return bounds.X == homeBounds.X &&
               bounds.Y == homeBounds.Y &&
               bounds.Width == homeBounds.Width &&
               bounds.Height == homeBounds.Height;
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
        if (Palette.FlatControls)
        {
            DrawFilledRectangle(Palette.MenuBackground, 0, 0, Width, Height);
            DrawRectangle(Palette.WindowBorder, 0, 0, Width, Height);
        }
        else
        {
            DrawRaisedRectangle(0, 0, Width, Height);
        }

        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            DrawChild(child);
        }
    }

    public void ApplyThemeStyle()
    {
        panel.color1 = Palette.MenuBackground;
        panel.MarkDirty();
        MarkDirty();
    }

    public override void OnLoseFocus()
    {
        if (Visible)
            UiAnimations.HideStartMenu(this);
    }

    public void HideMenu()
    {
        UiAnimations.HideStartMenu(this);
    }

    public void HideMenuImmediate()
    {
        MenuPopup.HideAll();
        Visible = false;
        MarkDirty();
    }


    public override string GetName() => "StartMenu";
}
