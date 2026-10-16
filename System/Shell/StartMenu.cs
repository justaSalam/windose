using Cosmos.Kernel.System;
using Cosmos.Kernel.System.Graphics;
using Windose.Programs;
using Windose.System.Kernel;
using Windose.System.System_Calls;

public class StartMenu : Window
{

    private StackPanel panel;
    private MenuItem programsMenu;
    private TextField searchField;
    public StartMenu(int x, int y, int width, int height, string title, bool useTitleBar) : base(x, y, width, height, title, useTitleBar)
    {
        process.processType = ProcessType.System;
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
            text = $"Windose NativeAOT {Kernel.VersionString}",
            useBackground = false,

            fontSize = 16,
            clampSize = false,
            Margin = new Thickness(0)

        });

        searchField = new TextField(0, 0, width, 24)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Top,
            clampSize = false,
            Margin = new Thickness(0, 0, 0, 4),
            PlaceholderText = "Search programs",
        };
        searchField.TextChanged += delegate (string text)
        {
            if (programsMenu != null && programsMenu.submenu != null)
                programsMenu.submenu.FilterItems(text);
        };
        panel.AddStackChild(searchField);

        MenuItem programs = new MenuItem(0, 0, width + 30, 24)
        {
            text = "Programs",
            fontSize = 16,
            horizontalAlignment = HorizontalAlignment.Stretch,
            clampSize = false,
            Margin = new Thickness(0)
        };
        programsMenu = programs;
        programs.AddSubmenuItem(new Png("/mnt/System/Icons/directx.png"), "VMWare SVGA Test", () => LaunchTracker.Start(() => new GraphicsEngine()));
        programs.AddSubmenuItem(new Png("/mnt/System/Icons/search_directory.png"), "File Explorer", () => LaunchTracker.Start(() => new FileExplorer(100, 100, 800, 500, "File Explorer", "/mnt")));


        programs.AddSubmenuItem(new Png("/mnt/System/Icons/hard_disk_drive_pie.png"), "Disk Management", () => LaunchTracker.Start(() => new DiskManagement(100, 100, 600, 350)));
        programs.AddSubmenuItem(new Png("/mnt/System/Icons/mci_devices.png"), "Device Manager", () => LaunchTracker.Start(() => new DeviceManager(100, 100, 600, 350)));
        programs.AddSubmenuItem(new Png("/mnt/System/Icons/msie.png"), "Internet Explorer", () => LaunchTracker.Start(() => new InternetExplorer(200, 200)));
        programs.AddSubmenuItem(new Png("/mnt/System/Icons/document.png"), "Designer", () => LaunchTracker.Start(() => new DesignerWindow(200, 200, 800, 600)));
        programs.AddSubmenuItem(new Png("/mnt/System/Icons/computer_taskmgr.png"), "Task Manager", () => LaunchTracker.Start(() => new PerformanceMonitor(180, 120)));
        programs.AddSubmenuItem(new Png("/mnt/System/Icons/appwizard.png"), "Control Test", () => LaunchTracker.Start(() => new ControlTestWindow()));



        programs.AddSubmenuSeparator();
        programs.AddSubmenuItem(new Png("/mnt/System/Icons/gears_tweakui_a.png"), "SYSDUMP", SystemLogger.Dump);


        MenuItem breeze = programs.AddSubmenuItem("Breeze");
        breeze.AddSubmenuItem(new Png("/mnt/System/Icons/write_wordpad.png"), "Breeze Editor", () => LaunchTracker.Start(() => new BreezeEditor()));
        breeze.AddSubmenuItem("Breeze API", () => LaunchTracker.Start(() => new BreezeApiBrowser()));


        breeze.AddSubmenuItem("Breeze Demo", BreezeDemo.Run);

        MenuItem apps = breeze.AddSubmenuItem("Applications");

        apps.AddSubmenuItem("Run Main", () =>
        {
            BreezeHost.RunFile("/mnt/Programs/main.breeze");
        });
        apps.AddSubmenuItem("Control Test", () =>
        {
            BreezeHost.RunFile("/mnt/Programs/ControlTest.breeze");
        });


        programs.AddSubmenuItem(new Png("/mnt/System/Icons/console_prompt.png"), "Command Prompt", () =>
        {
            LaunchTracker.Start(() => new CommandPrompt());
        });

        programs.AddSubmenuItem(new Png("/mnt/System/Icons/console_prompt.png"), "Lua", () =>
        {
            string script = "local w = win.createWindow(\"Lua App\", 160, 120, 360, 220)\nlocal label = win.createLabel(\"Hello from Lua ABI\", 24, 48, 220, 28)\nlocal button = win.createButton(\"Close\", 24, 90, 90, 28)\n\nwin.addChild(w, label)\nwin.addChild(w, button)\n\nwin.setOnClick(button, function()\n  win.closeWindow(w)\nend)\n\nwin.showWindow(w)";

        });

        MenuItem systemTools = programs.AddSubmenuItem("System Tools");
        systemTools.AddSubmenuItem(new Png("/mnt/System/Icons/scanregw.png"), "Registry Editor", () =>
        {
            LaunchTracker.Start(() => new RegistryEditor());
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

        settings.AddSubmenuItem(new Png("/mnt/System/Icons/appwizard.png"), "Control Panel", () =>
        {
            FileExplorer explorer =
                new FileExplorer(100, 100, 700, 480, "Control Panel");

            explorer.NavigateToPath("control");
            LaunchTracker.Start(() => explorer);
        });
        settings.AddSubmenuItem(new Png("/mnt/System/Icons/monitor_gear.png"), "Display Settings", () =>
        {
            LaunchTracker.Start(() => new DisplaySettings());
        });




        MenuItem powerOptions = new MenuItem(0, 0, width, 24)
        {
            text = "Power",
            fontSize = 16,
            horizontalAlignment = HorizontalAlignment.Stretch,
            clampSize = false,
            Margin = new Thickness(0)
        };
        powerOptions.AddSubmenuItem("Lock", () => LockManager.Lock());
        powerOptions.AddSubmenuItem("Log Off");
        powerOptions.AddSubmenuItem("Shutdown", () =>
        {
            Desktop.SaveLayout();
            Power.Shutdown();

        });

        powerOptions.AddSubmenuItem("Reboot", () =>
        {
            Desktop.SaveLayout();
            Power.Reboot();
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

    public void HideMenuImmediate()
    {
        MenuPopup.HideAll();
        Visible = false;
        MarkDirty();
    }


    public override string GetComponentName() => "StartMenu";
}
