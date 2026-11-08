using System;
using System.IO;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using Windose;
using Windose.Programs;
using Windose.System.Kernel;
using Windose.System.Kernel.Subsystem;
using Windose.System.Elf;

namespace Windose.Programs
{
    /// <summary>
    /// Windows 7 Run Dialog (Win+R / run command).
    /// Opens programs, documents, folders, and resources.
    /// </summary>
    public class RunDialog : Window
    {
        private readonly TextField input;
        private Cosmos.Kernel.System.Graphics.Image? dialogIcon;

        public RunDialog(int x = 80, int y = 380)
            : base(x, y, 420, 190, "Run", true)
        {
            canResize = false;
            canMaximize = false;

            try
            {
                dialogIcon = Windose.System.Resources.IconResource.Load("shell32.dll,-25", 32);
                SetIcon("shell32.dll,-25");
            }
            catch { }

            if (Palette.GlassEnabled)
            {
                useGlass = true;
            }

            // Prompt description
            AddChild(new Label(58, 14, 344, 40)
            {
                text = "Type the name of a program, folder, document, or Internet resource, and Windose will open it for you.",
                fontSize = 12,
                textColor = Palette.ControlBlack,
                useBackground = false,
                clampSize = false
            });

            // "Open:" label
            AddChild(new Label(16, 68, 40, 20)
            {
                text = "Open:",
                fontSize = 14,
                textColor = Palette.ControlBlack,
                useBackground = false
            });

            // Text input field
            input = new TextField(60, 64, 342, 24)
            {
                PlaceholderText = "e.g. calc, notepad, cmd, /mnt"
            };
            input.RequestFocus();
            input.EnterPressed += ExecuteRun;
            AddChild(input);

            // Separator line above buttons
            AddChild(new Separator(12, 106, 396, 2)
            {
                orientation = LayoutOrientation.Horizontal
            });

            // Action buttons
            Button okBtn = new Button("OK", 158, 118, 76, 26);
            okBtn.leftClickAction = ExecuteRun;
            AddChild(okBtn);

            Button cancelBtn = new Button("Cancel", 242, 118, 76, 26);
            cancelBtn.leftClickAction = () => WindowManager.PostClose(this);
            AddChild(cancelBtn);

            Button browseBtn = new Button("Browse...", 326, 118, 76, 26);
            browseBtn.leftClickAction = () =>
            {
                LaunchTracker.Start(() => new FileExplorer(100, 100, 750, 480, "Browse", "/mnt"));
            };
            AddChild(browseBtn);
        }

        public override void DrawLocal()
        {
            base.DrawLocal();
            if (dialogIcon != null)
            {
                try
                {
                    DrawImage(dialogIcon, 16, 16);
                }
                catch { }
            }
        }

        private void ExecuteRun()
        {
            string query = (input.text ?? "").Trim();
            if (string.IsNullOrEmpty(query)) return;

            // 1. Direct path exists
            if (File.Exists(query) || Directory.Exists(query))
            {
                FileAssociations.Launch(query);
                WindowManager.PostClose(this);
                return;
            }

            // 2. Try ELF resolution
            string? elf = CommandRegistry.ResolveElfPath("/mnt", query);
            if (elf != null)
            {
                FileAssociations.Launch(elf);
                WindowManager.PostClose(this);
                return;
            }

            // 3. Known system applets & tools
            string lower = query.ToLowerInvariant();
            if (lower == "cmd" || lower == "terminal")
            {
                if (File.Exists("/mnt/Programs/terminal.elf"))
                    FileAssociations.Launch("/mnt/Programs/terminal.elf");
                else
                    LaunchTracker.Start(() => new CommandPrompt());
                WindowManager.PostClose(this);
                return;
            }
            if (lower == "calc" || lower == "calculator")
            {
                LaunchTracker.Start(() => new Calculator());
                WindowManager.PostClose(this);
                return;
            }
            if (lower == "paint" || lower == "mspaint")
            {
                LaunchTracker.Start(() => new Paint());
                WindowManager.PostClose(this);
                return;
            }
            if (lower == "winver" || lower == "about")
            {
                LaunchTracker.Start(() => new AboutWindows());
                WindowManager.PostClose(this);
                return;
            }
            if (lower == "explorer" || lower == "files")
            {
                LaunchTracker.Start(() => new FileExplorer(100, 100, 800, 500, "File Explorer", "/mnt"));
                WindowManager.PostClose(this);
                return;
            }
            if (lower == "taskmgr" || lower == "perf")
            {
                LaunchTracker.Start(() => new PerformanceMonitor(180, 120));
                WindowManager.PostClose(this);
                return;
            }
            if (lower == "regedit" || lower == "registry")
            {
                LaunchTracker.Start(() => new RegistryEditor());
                WindowManager.PostClose(this);
                return;
            }
            if (lower == "diskmgmt")
            {
                LaunchTracker.Start(() => new DiskManagement(100, 100, 600, 350));
                WindowManager.PostClose(this);
                return;
            }
            if (lower == "devmgmt")
            {
                LaunchTracker.Start(() => new DeviceManager(100, 100, 600, 350));
                WindowManager.PostClose(this);
                return;
            }

            MessageBox.Show($"Cannot find '{query}'. Make sure you typed the name correctly, and then try again.", "Run");
        }

        public override string GetComponentName() => "RunDialog";
    }
}
