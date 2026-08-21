using Cosmos.Kernel.Core;
using Cosmos.Kernel.System;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Network.Config;
using Cosmos.Kernel.System.Network.IPv4.UDP.DHCP;
using System.Drawing;
using Windose;
using Windose.System.System_Calls;

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
            text = $"Windose NativeAOT {Cosmos.Kernel.System.Kernel.VersionString}",
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
        programs.AddSubmenuItem("VMWare SVGA Test", () => WindowManager.Register(new GraphicsEngine()));
        programs.AddSubmenuItem("File Explorer", () => WindowManager.Register(new FileExplorer(100, 100, 800, 500, "File Explorer", true)));

            
        programs.AddSubmenuItem("Disk Management", () => WindowManager.Register(new DiskManagement(100, 100, 600, 350)));
        programs.AddSubmenuItem("Task Manager", () => WindowManager.Register(new PerformanceMonitor(180, 120)));
        programs.AddSubmenuSeparator();
        programs.AddSubmenuItem("Network Configuration", () => 
        {
            if (NetworkManager.PrimaryDevice != null)
            {
                NetworkStack.Initialize();
                DHCPClient dhcpClient = new DHCPClient();

                if (dhcpClient.SendDiscoverPacket() != -1)
                {
                    IPConfig? config = NetworkConfigManager.Get(NetworkManager.PrimaryDevice);

                    SystemLogger.WriteLine("Network", "DHCP configuration obtained successfully", ConsoleMessageType.Log);

                    SystemLogger.WriteLine("Network", $"IP address: {config.IPAddress}", ConsoleMessageType.Log);
                    SystemLogger.WriteLine("Network", $"Subnet: {config.SubnetMask}", ConsoleMessageType.Log);
                    SystemLogger.WriteLine("Network", $"Gateway: {config.DefaultGateway}", ConsoleMessageType.Log);
                }
                else
                {
                    SystemLogger.WriteLine("Network", "DHCP timed out", ConsoleMessageType.Warning);
                }

            }
        });

        programs.AddSubmenuSeparator();
        programs.AddSubmenuItem("SYSDUMP", SystemLogger.Dump);


        MenuItem breeze = programs.AddSubmenuItem("Breeze");
        breeze.AddSubmenuItem("Breeze Editor", () => WindowManager.Register(new BreezeEditor()));
        breeze.AddSubmenuItem("Breeze API", () => WindowManager.Register(new BreezeApiBrowser()));


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

        settings.AddSubmenuItem("Control Panel", () =>
        {
            FileExplorer explorer =
                new FileExplorer(100, 100, 700, 480, "Control Panel", true);

            explorer.NavigateToPath("control");
            WindowManager.Register(explorer);
        });
        settings.AddSubmenuItem("Display Settings", () =>
        {
            WindowManager.Register(new DisplaySettings());
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
            Desktop.SaveLayout();

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

        DrawRaisedRectangle(0, 0, Width, Height);


        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            DrawChild(child);
        }
    }

    public void ApplyThemeStyle()
    {
        panel.MarkDirty();
        MarkDirty();
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


    public override string GetName() => "StartMenu";
}
