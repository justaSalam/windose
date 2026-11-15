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

            var layout = new Windose.System.GUI.Components.DialogLayout(this, showFooterSeparator: true);
            layout.SetHeader("Type the name of a program, folder, document, or Internet resource, and Windose will open it for you.", dialogIcon);

            input = new TextField(0, 0, 100, 24)
            {
                PlaceholderText = "e.g. calc, notepad, cmd, /mnt"
            };
            input.RequestFocus();
            input.EnterPressed += ExecuteRun;

            Button browseBtn = new Button("Browse...", 0, 0, 78, 26);
            browseBtn.leftClickAction = () =>
            {
                LaunchTracker.Start(() => new FileExplorer(100, 100, 750, 480, "Browse", "/mnt"));
                WindowManager.PostClose(this);
            };

            layout.AddRow("Open:", input);

            Button okBtn = new Button("OK", 0, 0, 76, 26);
            okBtn.leftClickAction = ExecuteRun;

            Button cancelBtn = new Button("Cancel", 0, 0, 76, 26);
            cancelBtn.leftClickAction = () => WindowManager.PostClose(this);

            layout.SetButtons(okBtn, cancelBtn, browseBtn);
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

            // 2. Canonical aliases for LaunchApp
            string app = query.ToLowerInvariant() switch
            {
                "cmd" => "terminal",
                "files" => "explorer",
                "perf" => "taskmgr",
                "diskmgmt" => "diskmanagement",
                "devmgmt" => "devicemanager",
                _ => query
            };

            if (ElfRunner.Host != null && ElfRunner.Host.LaunchApp(app, ""))
            {
                WindowManager.PostClose(this);
                return;
            }

            MessageBox.Show($"Cannot find '{query}'. Make sure you typed the name correctly, and then try again.", "Run");
        }

        public override string GetComponentName() => "RunDialog";
    }
}
