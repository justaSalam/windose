using Cosmos.Executable.Lua;
using Cosmos.Kernel.System;
using Windose.System.Elf;
using Cosmos.Kernel.System.Graphics;
using System.Drawing;
using Windose.Programs;
using Windose.System.Kernel;
using Windose.System.Kernel.Subsystem;
using Windose.System.Lua;
using Windose.System.System_Calls;

public class StartMenu : Window
{

    private StackPanel panel;
    private MenuItem programsMenu;
    private TextField searchField;
    private Label userTile;
    public StartMenu(int x, int y, int width, int height, string title, bool useTitleBar) : base(x, y, width, height, title, useTitleBar)
    {
        process.processType = ProcessType.System;
        zLayer = DrawLayer.Popup;
        Visible = false;
        canResize = false;
        canMove = false;
        showInTaskbar = false;
        if (Palette.GlassEnabled)
        {
            useGlass = true;
            glassStyle = GlassStyle.AeroStartMenu();
        }

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
            PlaceholderText = "Search programs and files",
        };
        searchField.TextChanged += delegate (string text)
        {
            if (programsMenu != null && programsMenu.submenu != null)
                programsMenu.submenu.FilterItems(text);
        };
        searchField.EnterPressed += () =>
        {
            string query = (searchField.text ?? "").Trim();
            if (string.IsNullOrEmpty(query)) return;

            HideMenuImmediate();

            // 1. If it's a file path that exists, launch it directly
            if (File.Exists(query))
            {
                FileAssociations.Launch(query);
                return;
            }

            // 2. Try resolving an ELF program
            string? elfPath = CommandRegistry.ResolveElfPath("/mnt", query);
            if (elfPath != null)
            {
                FileAssociations.Launch(elfPath);
                return;
            }

            // 3. Known quick aliases
            string lower = query.ToLowerInvariant();
            if (lower == "cmd" || lower == "terminal")
            {
                if (File.Exists("/mnt/Programs/terminal.elf"))
                    FileAssociations.Launch("/mnt/Programs/terminal.elf");
                else
                    LaunchTracker.Start(() => new CommandPrompt());
                return;
            }
            if (lower == "explorer" || lower == "files")
            {
                LaunchTracker.Start(() => new FileExplorer(100, 100, 800, 500, "File Explorer", "/mnt"));
                return;
            }
            if (lower == "taskmgr" || lower == "perf")
            {
                LaunchTracker.Start(() => new PerformanceMonitor(180, 120));
                return;
            }
            if (lower == "regedit" || lower == "registry")
            {
                LaunchTracker.Start(() => new RegistryEditor());
                return;
            }

            // 4. Try matching any visible/matching item in programsMenu
            if (programsMenu?.submenu != null)
            {
                foreach (Component child in programsMenu.submenu.children)
                {
                    if (child is MenuItem item && item.Visible)
                    {
                        if (item.text.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            item.leftClickAction?.Invoke();
                            return;
                        }
                    }
                }
            }
        };
        panel.AddStackChild(searchField);

        userTile = new Label(0, 0, width, 22)
        {
            verticalAlignment = VerticalAlignment.Top,
            horizontalAlignment = HorizontalAlignment.Stretch,
            horizontalTextAlignment = HorizontalAlignment.Left,
            text = "Guest",
            fontSize = 14,
            textColor = Palette.TitleText,
            useBackground = false,
            clampSize = false,
            Margin = new Thickness(0, 0, 0, 2),
        };
        panel.AddStackChild(userTile);

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
        programs.AddSubmenuItem(new Png("/mnt/System/Icons/search_directory.png"), "File Explorer", () =>
        {
            if (File.Exists("/mnt/Programs/explorer.elf"))
            {
                byte[] data = File.ReadAllBytes("/mnt/Programs/explorer.elf");
                Windose.System.Elf.ElfRunner.Spawn(data, "explorer.elf", new string[] { "/mnt" }, out _);
            }
            else
            {
                LaunchTracker.Start(() => new FileExplorer(100, 100, 800, 500, "File Explorer", "/mnt"));
            }
        });


        programs.AddSubmenuItem(new Png("/mnt/System/Icons/hard_disk_drive_pie.png"), "Disk Management", () => LaunchTracker.Start(() => new DiskManagement(100, 100, 600, 350)));
        programs.AddSubmenuItem(new Png("/mnt/System/Icons/mci_devices.png"), "Device Manager", () => LaunchTracker.Start(() => new DeviceManager(100, 100, 600, 350)));
        programs.AddSubmenuItem(new Png("/mnt/System/Icons/msie.png"), "Internet Explorer", () => LaunchTracker.Start(() => new InternetExplorer(200, 200)));
        programs.AddSubmenuItem(new Png("/mnt/System/Icons/media_player.png"), "Windows Media Player", () => LaunchTracker.Start(() => new MediaPlayer()));
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
            if (File.Exists("/mnt/Programs/terminal.elf"))
                ElfRunner.RunFile("/mnt/Programs/terminal.elf");
            else
                LaunchTracker.Start(() => new CommandPrompt());
        });

        programs.AddSubmenuItem(new Png("/mnt/System/Icons/console_prompt.png"), "Lua", () =>
        {
            try
            {
                string demoPath = "/mnt/System/Lua/demo.lua";
                if (!File.Exists(demoPath))
                {
                    Directory.CreateDirectory("/mnt/System/Lua");
                    File.WriteAllText(demoPath, "windose.log(\"Running Lua demo\")\nwindose.open(\"explorer\")\n");
                }
                var lua = LuaAPI.NewState();
                lua.L_OpenLibs();
                new LuaWindoseLib(demoPath, new[] { "fs.read", "fs.write", "ui.window", "reg.read", "reg.write" }).Install(lua);
                lua.L_DoFile(demoPath);
            }
            catch (Exception ex)
            {
                SystemLogger.WriteLine("Lua", $"Lua execution error: {ex.Message}", ConsoleMessageType.Error);
            }
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
        documents.AddSubmenuItem(new Png("/mnt/System/Icons/directory_open_file_mydocs.png"), "My Documents", () =>
        {
            Directory.CreateDirectory("/mnt/Documents");
            LaunchTracker.Start(() => new FileExplorer(100, 100, 800, 500, "Documents", "/mnt/Documents"));
        });
        try
        {
            Directory.CreateDirectory("/mnt/Documents");
            string[] docFiles = Directory.GetFiles("/mnt/Documents");
            if (docFiles.Length > 0)
            {
                documents.AddSubmenuSeparator();
                for (int i = 0; i < Math.Min(docFiles.Length, 8); i++)
                {
                    string filePath = docFiles[i];
                    string fileName = Path.GetFileName(filePath);
                    documents.AddSubmenuItem(fileName, () => FileAssociations.Launch(filePath));
                }
            }
        }
        catch { }

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
        powerOptions.AddSubmenuItem("Restart Shell", () => Explorer.RestartShell());
        powerOptions.AddSubmenuItem("Lock", () => LockManager.Lock());
        powerOptions.AddSubmenuItem("Log Off", () =>
        {
            Session.Logout();
            LockManager.Lock();
        });
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
        if (userTile != null)
        {
            string name = Session.CurrentUser != null ? Session.CurrentUser.username : "Guest";
            if (userTile.text != name)
            {
                userTile.text = name;
                userTile.MarkDirty();
            }
        }
    }



    public override void DrawLocal()
    {
        if (useGlass)
        {
            int r = 6;
            // Windows 7 Start Menu Aero glass backdrop with rounded corners
            FillRaw(Color.FromArgb(0, 0, 0, 0), 0, 0, Width, Height);
            DrawOverGradientVerticalRounded(Color.FromArgb(220, 24, 40, 60), Color.FromArgb(245, 14, 24, 36), 0, 0, Width, Height, r);
            DrawRoundedRectangle(Color.FromArgb(180, 50, 75, 105), 0, 0, Width, Height, r);
            if (Width > 4 && Height > 2)
                DrawHorizontalSpan(Color.FromArgb(80, 255, 255, 255), 2, 1, Width - 4);
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
