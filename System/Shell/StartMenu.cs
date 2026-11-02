using System;
using System.IO;
using System.Drawing;
using Cosmos.Executable.Lua;
using Cosmos.Kernel.System;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using Windose;
using Windose.Programs;
using Windose.System.Elf;
using Windose.System.Kernel;
using Windose.System.Kernel.Subsystem;
using Windose.System.Lua;
using Windose.System.Media;
using Windose.System.System_Calls;

public class StartMenu : Window
{
    private readonly TextField searchField;
    private readonly Label userTile;
    private readonly MenuItem allProgramsMenu;
    private Png? userAvatar;

    public StartMenu(int x, int y, int width, int height, string title, bool useTitleBar)
        : base(x, y, width, height, title, useTitleBar)
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

        try
        {
            if (File.Exists("/mnt/System/Icons/users.png"))
                userAvatar = new Png("/mnt/System/Icons/users.png");
            else if (File.Exists("/mnt/System/Icons/users_key.png"))
                userAvatar = new Png("/mnt/System/Icons/users_key.png");
        }
        catch { }

        // ==========================================
        // LEFT COLUMN: Pinned Programs (y: 12..410)
        // ==========================================
        int leftX = 12;
        int leftW = 238;
        int itemH = 34;
        int curY = 12;

        AddLeftProgram("Internet Explorer", "/mnt/System/Icons/msie.png", leftX, curY, leftW, itemH,
            () => LaunchTracker.Start(() => new InternetExplorer(200, 200)));
        curY += itemH;

        AddLeftProgram("Windows Media Player", "/mnt/System/Icons/media_player.png", leftX, curY, leftW, itemH,
            () => LaunchTracker.Start(() => new MediaPlayer()));
        curY += itemH;

        AddLeftProgram("Calculator", "/mnt/System/Icons/calculator.png", leftX, curY, leftW, itemH,
            () => LaunchTracker.Start(() => new Calculator()));
        curY += itemH;

        AddLeftProgram("Paint", "/mnt/System/Icons/paint_file.png", leftX, curY, leftW, itemH,
            () => LaunchTracker.Start(() => new Paint()));
        curY += itemH;

        AddLeftProgram("Breeze Editor", "/mnt/System/Icons/write_wordpad.png", leftX, curY, leftW, itemH,
            () => LaunchTracker.Start(() => new BreezeEditor()));
        curY += itemH;

        AddLeftProgram("Command Prompt", "/mnt/System/Icons/console_prompt.png", leftX, curY, leftW, itemH, () =>
        {
            if (File.Exists("/mnt/Programs/terminal.elf"))
                ElfRunner.RunFile("/mnt/Programs/terminal.elf");
            else
                LaunchTracker.Start(() => new CommandPrompt());
        });
        curY += itemH;

        AddLeftProgram("Task Manager", "/mnt/System/Icons/computer_taskmgr.png", leftX, curY, leftW, itemH,
            () => LaunchTracker.Start(() => new PerformanceMonitor(180, 120)));
        curY += itemH;

        AddLeftProgram("Disk Management", "/mnt/System/Icons/hard_disk_drive_pie.png", leftX, curY, leftW, itemH,
            () => LaunchTracker.Start(() => new DiskManagement(100, 100, 600, 350)));
        curY += itemH;

        // "All Programs" Submenu
        allProgramsMenu = new MenuItem(leftX, 422, leftW, 26)
        {
            text = "All Programs",
            fontSize = 14,
            drawSubmenuArrow = true
        };

        allProgramsMenu.AddSubmenuItem(new Png("/mnt/System/Icons/search_directory.png"), "File Explorer", () =>
        {
            if (File.Exists("/mnt/Programs/explorer.elf"))
            {
                byte[] data = File.ReadAllBytes("/mnt/Programs/explorer.elf");
                ElfRunner.Spawn(data, "explorer.elf", new string[] { "/mnt" }, out _);
            }
            else
            {
                LaunchTracker.Start(() => new FileExplorer(100, 100, 800, 500, "File Explorer", "/mnt"));
            }
        });

        MenuItem accessories = allProgramsMenu.AddSubmenuItem("Accessories");
        accessories.AddSubmenuItem(new Png("/mnt/System/Icons/calculator.png"), "Calculator", () => LaunchTracker.Start(() => new Calculator()));
        accessories.AddSubmenuItem(new Png("/mnt/System/Icons/paint_file.png"), "Paint", () => LaunchTracker.Start(() => new Paint()));
        accessories.AddSubmenuItem(new Png("/mnt/System/Icons/write_wordpad.png"), "Breeze Editor", () => LaunchTracker.Start(() => new BreezeEditor()));
        accessories.AddSubmenuItem(new Png("/mnt/System/Icons/console_prompt.png"), "Command Prompt", () => LaunchTracker.Start(() => new CommandPrompt()));
        accessories.AddSubmenuItem(new Png("/mnt/System/Icons/search_file.png"), "Run...", () => LaunchTracker.Start(() => new RunDialog()));

        MenuItem systemTools = allProgramsMenu.AddSubmenuItem("System Tools");
        systemTools.AddSubmenuItem(new Png("/mnt/System/Icons/scanregw.png"), "Registry Editor", () => LaunchTracker.Start(() => new RegistryEditor()));
        systemTools.AddSubmenuItem(new Png("/mnt/System/Icons/computer_taskmgr.png"), "Task Manager", () => LaunchTracker.Start(() => new PerformanceMonitor(180, 120)));
        systemTools.AddSubmenuItem(new Png("/mnt/System/Icons/hard_disk_drive_pie.png"), "Disk Management", () => LaunchTracker.Start(() => new DiskManagement(100, 100, 600, 350)));
        systemTools.AddSubmenuItem(new Png("/mnt/System/Icons/mci_devices.png"), "Device Manager", () => LaunchTracker.Start(() => new DeviceManager(100, 100, 600, 350)));
        systemTools.AddSubmenuItem(new Png("/mnt/System/Icons/gears_tweakui_a.png"), "SYSDUMP", SystemLogger.Dump);

        MenuItem breeze = allProgramsMenu.AddSubmenuItem("Breeze Scripting");
        breeze.AddSubmenuItem(new Png("/mnt/System/Icons/write_wordpad.png"), "Breeze Editor", () => LaunchTracker.Start(() => new BreezeEditor()));
        breeze.AddSubmenuItem("Breeze API", () => LaunchTracker.Start(() => new BreezeApiBrowser()));
        breeze.AddSubmenuItem("Breeze Demo", BreezeDemo.Run);

        allProgramsMenu.AddSubmenuItem(new Png("/mnt/System/Icons/directx.png"), "VMWare SVGA Test", () => LaunchTracker.Start(() => new GraphicsEngine()));

        AddChild(allProgramsMenu);

        // Search Field at bottom of left pane
        searchField = new TextField(leftX, 458, leftW, 24)
        {
            PlaceholderText = "Search programs and files"
        };
        searchField.TextChanged += delegate (string queryText)
        {
            if (allProgramsMenu?.submenu != null)
                allProgramsMenu.submenu.FilterItems(queryText);
        };
        searchField.EnterPressed += ExecuteSearch;
        AddChild(searchField);

        // ==========================================
        // RIGHT COLUMN: User & Links (x: 258..412)
        // ==========================================
        int rightX = 260;
        int rightW = 150;

        // User Name Label
        userTile = new Label(rightX + 44, 20, rightW - 44, 22)
        {
            text = Session.CurrentUser != null ? Session.CurrentUser.username : "Guest",
            fontSize = 14,
            textColor = Color.White,
            useBackground = false,
            clampSize = false
        };
        AddChild(userTile);

        // Personal links
        int linkY = 56;
        int linkH = 26;

        AddRightLink("User Files", rightX, linkY, rightW, linkH, () =>
        {
            LaunchTracker.Start(() => new FileExplorer(100, 100, 800, 500, "User", "/mnt"));
        }, true);
        linkY += linkH;

        AddRightLink("Documents", rightX, linkY, rightW, linkH, () =>
        {
            Directory.CreateDirectory("/mnt/Documents");
            LaunchTracker.Start(() => new FileExplorer(100, 100, 800, 500, "Documents", "/mnt/Documents"));
        }, true);
        linkY += linkH;

        AddRightLink("Pictures", rightX, linkY, rightW, linkH, () =>
        {
            Directory.CreateDirectory("/mnt/Pictures");
            LaunchTracker.Start(() => new FileExplorer(100, 100, 800, 500, "Pictures", "/mnt/Pictures"));
        }, true);
        linkY += linkH;

        AddRightLink("Music", rightX, linkY, rightW, linkH, () =>
        {
            Directory.CreateDirectory("/mnt/Music");
            LaunchTracker.Start(() => new FileExplorer(100, 100, 800, 500, "Music", "/mnt/Music"));
        }, true);
        linkY += linkH;

        AddRightLink("Computer", rightX, linkY, rightW, linkH, () =>
        {
            LaunchTracker.Start(() => new FileExplorer(100, 100, 800, 500, "Computer", "/mnt"));
        }, true);
        linkY += linkH + 12; // Gap for separator

        // System links
        AddRightLink("Control Panel", rightX, linkY, rightW, linkH, () =>
        {
            FileExplorer explorer = new FileExplorer(100, 100, 700, 480, "Control Panel");
            explorer.NavigateToPath("control");
            LaunchTracker.Start(() => explorer);
        });
        linkY += linkH;

        AddRightLink("Devices and Printers", rightX, linkY, rightW, linkH, () =>
        {
            LaunchTracker.Start(() => new DeviceManager(100, 100, 600, 350));
        });
        linkY += linkH;

        AddRightLink("Default Programs", rightX, linkY, rightW, linkH, () =>
        {
            LaunchTracker.Start(() => new BreezeEditor());
        });
        linkY += linkH + 12; // Gap for separator

        AddRightLink("Run...", rightX, linkY, rightW, linkH, () =>
        {
            LaunchTracker.Start(() => new RunDialog());
        });

        // Bottom Right: Shut down Button & Flyout
        Button shutdownBtn = new Button("Shut down", rightX, 458, 104, 26);
        shutdownBtn.leftClickAction = () =>
        {
            AudioEngine.Instance.PlayClick();
            HideMenuImmediate();
            Desktop.SaveLayout();
            Power.Shutdown();
        };
        AddChild(shutdownBtn);

        MenuItem powerFlyout = new MenuItem(rightX + 108, 458, 38, 26)
        {
            text = "►",
            fontSize = 12,
            drawSubmenuArrow = false
        };
        powerFlyout.AddSubmenuItem("Restart", () =>
        {
            Desktop.SaveLayout();
            Power.Reboot();
        });
        powerFlyout.AddSubmenuItem("Lock", () => LockManager.Lock());
        powerFlyout.AddSubmenuItem("Log Off", () =>
        {
            Session.Logout();
            LockManager.Lock();
        });
        powerFlyout.AddSubmenuItem("Restart Shell", () => Explorer.RestartShell());
        AddChild(powerFlyout);

        MarkDirty();
    }

    private void AddLeftProgram(string title, string iconPath, int x, int y, int w, int h, Action click)
    {
        AddChild(new StartMenuProgramItem(title, iconPath, x, y, w, h, click));
    }

    private void AddRightLink(string title, int x, int y, int w, int h, Action click, bool bold = false)
    {
        AddChild(new StartMenuRightLink(title, x, y, w, h, click, bold));
    }

    private void ExecuteSearch()
    {
        string query = (searchField.text ?? "").Trim();
        if (string.IsNullOrEmpty(query)) return;

        HideMenuImmediate();

        // 1. Direct file path
        if (File.Exists(query))
        {
            FileAssociations.Launch(query);
            return;
        }

        // 2. Resolve ELF binary
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
        if (lower == "calc" || lower == "calculator")
        {
            LaunchTracker.Start(() => new Calculator());
            return;
        }
        if (lower == "paint" || lower == "mspaint")
        {
            LaunchTracker.Start(() => new Paint());
            return;
        }
        if (lower == "winver" || lower == "about")
        {
            LaunchTracker.Start(() => new AboutWindows());
            return;
        }
        if (lower == "run")
        {
            LaunchTracker.Start(() => new RunDialog());
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

        // 4. Match items in allPrograms
        if (allProgramsMenu?.submenu != null)
        {
            foreach (Component child in allProgramsMenu.submenu.children)
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
            int r = 8;
            // Aero Glass Backdrop
            FillRaw(Color.FromArgb(0, 0, 0, 0), 0, 0, Width, Height);
            DrawOverGradientVerticalRounded(Color.FromArgb(235, 18, 32, 48), Color.FromArgb(245, 10, 18, 28), 0, 0, Width, Height, r);
            DrawRoundedRectangle(Color.FromArgb(180, 50, 75, 105), 0, 0, Width, Height, r);
            if (Width > 4 && Height > 2)
                DrawHorizontalSpan(Color.FromArgb(80, 255, 255, 255), 2, 1, Width - 4);

            // Left Column: White/Light surface with rounded corners
            int leftW = 246;
            int leftH = Height - 16;
            DrawGradientVerticalRounded(Color.FromArgb(255, 255, 255), Color.FromArgb(248, 250, 253), 8, 8, leftW, leftH, 4);
            DrawRoundedRectangle(Color.FromArgb(170, 190, 210), 8, 8, leftW, leftH, 4);

            // Separator line between left items and search area
            DrawLine(Color.FromArgb(220, 226, 234), 10, 452, 8 + leftW - 2, 452);

            // Right column user avatar frame
            DrawRoundedRectangle(Color.FromArgb(100, 200, 230, 255), 262, 12, 36, 36, 4);
            if (userAvatar != null)
            {
                try { DrawImage(userAvatar, 264, 14); } catch { }
            }
            else
            {
                DrawFilledRectangle(Color.FromArgb(180, 60, 100, 140), 264, 14, 32, 32);
                DrawString("U", Color.White, 274, 22);
            }

            // Right column separator lines
            DrawLine(Color.FromArgb(60, 255, 255, 255), 262, 212, Width - 10, 212);
            DrawLine(Color.FromArgb(60, 255, 255, 255), 262, 322, Width - 10, 322);
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

/// <summary>
/// Clickable program entry in the Left Pane of the Windows 7 Start Menu.
/// </summary>
public class StartMenuProgramItem : Component
{
    private readonly Png? icon;
    public Action? clickAction;
    private bool isHovered;
    private bool isPressed;

    public StartMenuProgramItem(string title, string iconPath, int x, int y, int width, int height, Action click)
        : base(x, y, width, height)
    {
        this.text = title;
        this.clickAction = click;
        try
        {
            if (File.Exists(iconPath))
                this.icon = new Png(iconPath);
        }
        catch { }
    }

    public override void DrawLocal()
    {
        if (isPressed)
        {
            DrawGradientVerticalRounded(Color.FromArgb(190, 220, 250), Color.FromArgb(160, 200, 240), 0, 0, Width, Height, 3);
            DrawRoundedRectangle(Color.FromArgb(120, 170, 220), 0, 0, Width, Height, 3);
        }
        else if (isHovered)
        {
            DrawGradientVerticalRounded(Color.FromArgb(240, 246, 254), Color.FromArgb(220, 235, 252), 0, 0, Width, Height, 3);
            DrawRoundedRectangle(Color.FromArgb(184, 214, 251), 0, 0, Width, Height, 3);
        }

        int textX = 8;
        if (icon != null)
        {
            DrawImage(icon, 6, (Height - (int)icon.Height) / 2);
            textX = (int)icon.Width + 12;
        }

        DrawString(text, Color.FromArgb(20, 20, 20), textX, (Height - 16) / 2);
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        bool inside = IsInsideAbsolute(mouseX, mouseY);
        if (inside != isHovered)
        {
            isHovered = inside;
            MarkDirty();
        }

        if (mouse.left == MouseEvents.Press && inside)
        {
            isPressed = true;
            MarkDirty();
            return true;
        }

        if (mouse.left == MouseEvents.Release)
        {
            bool wasPressed = isPressed;
            isPressed = false;
            MarkDirty();
            if (wasPressed && inside)
            {
                AudioEngine.Instance.PlayClick();
                clickAction?.Invoke();
                Explorer.startMenu?.HideMenuImmediate();
                return true;
            }
        }

        return inside;
    }

    public override string GetComponentName() => "StartMenuProgramItem";
}

/// <summary>
/// Clickable link in the Right Pane of the Windows 7 Start Menu.
/// </summary>
public class StartMenuRightLink : Component
{
    public Action? clickAction;
    private bool isHovered;
    private bool isPressed;
    public bool isBold;

    public StartMenuRightLink(string title, int x, int y, int width, int height, Action click, bool bold = false)
        : base(x, y, width, height)
    {
        this.text = title;
        this.clickAction = click;
        this.isBold = bold;
    }

    public override void DrawLocal()
    {
        if (isPressed)
        {
            DrawGradientVerticalRounded(Color.FromArgb(90, 30, 60, 95), Color.FromArgb(70, 20, 45, 75), 0, 0, Width, Height, 3);
            DrawRoundedRectangle(Color.FromArgb(120, 70, 110, 160), 0, 0, Width, Height, 3);
        }
        else if (isHovered)
        {
            DrawGradientVerticalRounded(Color.FromArgb(80, 255, 255, 255), Color.FromArgb(40, 255, 255, 255), 0, 0, Width, Height, 3);
            DrawRoundedRectangle(Color.FromArgb(100, 200, 230, 255), 0, 0, Width, Height, 3);
        }

        DrawString(text, Color.FromArgb(240, 248, 255), 6, (Height - 16) / 2);
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        bool inside = IsInsideAbsolute(mouseX, mouseY);
        if (inside != isHovered)
        {
            isHovered = inside;
            MarkDirty();
        }

        if (mouse.left == MouseEvents.Press && inside)
        {
            isPressed = true;
            MarkDirty();
            return true;
        }

        if (mouse.left == MouseEvents.Release)
        {
            bool wasPressed = isPressed;
            isPressed = false;
            MarkDirty();
            if (wasPressed && inside)
            {
                AudioEngine.Instance.PlayClick();
                clickAction?.Invoke();
                Explorer.startMenu?.HideMenuImmediate();
                return true;
            }
        }

        return inside;
    }

    public override string GetComponentName() => "StartMenuRightLink";
}
