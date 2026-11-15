using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Windose;
using Windose.System.Kernel;
using Windose.System.Kernel.Subsystem;
using Windose.System.Resources;
using Windose.System.System_Calls;
using Windose.System.Elf;

namespace Windose.Programs
{
    /// <summary>
    /// Windows 7 "Open with" modal dialog.
    /// Allows users to choose which application to launch a given file with,
    /// and optionally associate the file extension permanently.
    /// </summary>
    public class OpenWithDialog : Window
    {
        private readonly string targetPath;
        private readonly ListView programList;
        private readonly Checkbox alwaysUseCheck;
        private readonly Button okButton;
        private readonly Button cancelButton;

        public OpenWithDialog(int x, int y, string filePath)
            : base(x, y, 460, 420, "Open with", true)
        {
            targetPath = filePath ?? "";
            canResize = false;
            canMaximize = false;

            try
            {
                SetIcon("shell32.dll,-25");
            }
            catch { }

            if (Palette.GlassEnabled)
            {
                useGlass = true;
            }

            // Top Header: File Icon and Name
            string fileName = Path.GetFileName(targetPath);
            if (string.IsNullOrEmpty(fileName)) fileName = targetPath;

            Image? fileIcon = null;
            try
            {
                fileIcon = FileAssociations.GetIcon(targetPath) ?? IconResource.Load("imageres.dll,-2", 32);
            }
            catch { }

            if (fileIcon != null)
            {
                AddChild(new ImageView(fileIcon, 16, 36, 32, 32));
            }

            AddChild(new Label(56, 36, 380, 20)
            {
                text = fileName,
                fontSize = 14,
                textColor = Palette.ControlBlack,
                useBackground = false
            });

            AddChild(new Label(56, 58, 380, 20)
            {
                text = "Choose the program you want to use to open this file:",
                fontSize = 12,
                textColor = Palette.ControlShadow,
                useBackground = false
            });

            // Program ListView
            programList = new ListView(16, 84, 428, 214)
            {
                detailsRowHeight = 26,
                fontSize = 13,
                iconSize = 16
            };
            programList.AddColumn("Program", 270);
            programList.AddColumn("Type", 140);
            programList.itemDoubleClick += _ => LaunchSelected();
            programList.selectedChanged += _ => UpdateButtonState();
            AddChild(programList);

            PopulatePrograms();

            // Checkbox: "Always use the selected program..."
            string ext = Path.GetExtension(targetPath);
            string checkText = string.IsNullOrEmpty(ext)
                ? "Always use the selected program to open this file"
                : $"Always use the selected program to open {ext} files";

            alwaysUseCheck = new Checkbox(16, 312)
            {
                text = checkText,
                fontSize = 12,
                textColor = Palette.ControlBlack,
                Checked = false
            };
            AddChild(alwaysUseCheck);

            // Separator
            AddChild(new Separator(12, 344, 436, 2));

            // Buttons: OK and Cancel
            okButton = new Button("OK", 276, 356, 80, 26)
            {
                Enabled = false,
                leftClickAction = LaunchSelected
            };
            AddChild(okButton);

            cancelButton = new Button("Cancel", 364, 356, 80, 26)
            {
                leftClickAction = () => WindowManager.Close(this)
            };
            AddChild(cancelButton);
        }

        private void PopulatePrograms()
        {
            programList.ClearItems();

            // 1. Common native C# tools
            Image? editorIcon = IconResource.Load("shell32.dll,-70", 16);
            Image? mediaIcon = IconResource.Load("wmploc.dll,-102", 16);
            Image? imgIcon = IconResource.Load("imageres.dll,-71", 16);
            Image? cmdIcon = IconResource.Load("shell32.dll,-284", 16);
            Image? appIcon = IconResource.Load("imageres.dll,-15", 16) ?? IconResource.Load("imageres.dll,-2", 16);

            // Aero Editor (.elf) or Breeze Editor
            if (File.Exists("/mnt/Programs/aeroeditor.elf"))
            {
                programList.AddItem(new[] { "Aero Editor", "ELF Application" }, editorIcon, "/mnt/Programs/aeroeditor.elf");
            }
            programList.AddItem(new[] { "Breeze Editor", "System Editor" }, editorIcon, typeof(BreezeEditor));

            // Paint (.elf)
            if (File.Exists("/mnt/Programs/paint.elf"))
            {
                programList.AddItem(new[] { "Paint", "ELF Application" }, imgIcon, "/mnt/Programs/paint.elf");
            }

            // Image Viewer
            programList.AddItem(new[] { "Windows Photo Viewer", "System Viewer" }, imgIcon, typeof(ImageViewer));

            // Media Player
            programList.AddItem(new[] { "Windows Media Player", "System Player" }, mediaIcon, typeof(MediaPlayer));

            // Command Prompt
            if (File.Exists("/mnt/Programs/terminal.elf"))
            {
                programList.AddItem(new[] { "Terminal", "ELF Application" }, cmdIcon, "/mnt/Programs/terminal.elf");
            }
            programList.AddItem(new[] { "Command Prompt", "System Utility" }, cmdIcon, typeof(CommandPrompt));

            // TCC Compiler (.elf)
            if (File.Exists("/mnt/Programs/tcc.elf"))
            {
                programList.AddItem(new[] { "TCC C Compiler", "ELF Application" }, appIcon, "/mnt/Programs/tcc.elf");
            }

            // 2. Discover any additional ELF programs in /mnt/Programs
            try
            {
                if (Directory.Exists("/mnt/Programs"))
                {
                    string[] elfs = Directory.GetFiles("/mnt/Programs");
                    for (int i = 0; i < elfs.Length; i++)
                    {
                        string p = elfs[i];
                        if (!p.EndsWith(".elf", StringComparison.OrdinalIgnoreCase)) continue;
                        string baseName = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();

                        // Skip programs already added above
                        if (baseName == "aeroeditor" || baseName == "paint" || baseName == "terminal" || baseName == "tcc")
                            continue;

                        string displayName = char.ToUpperInvariant(baseName[0]) + baseName.Substring(1);
                        programList.AddItem(new[] { displayName, "ELF Application" }, appIcon, p);
                    }
                }
            }
            catch { }

            // Auto-select first item
            if (programList.items.Count > 0)
            {
                programList.selectedItem = programList.items[0];
                UpdateButtonState();
            }
        }

        private void UpdateButtonState()
        {
            okButton.Enabled = programList.selectedItem != null;
            okButton.MarkDirty();
        }

        private void LaunchSelected()
        {
            ListViewItem? selected = programList.selectedItem;
            if (selected == null || selected.Tag == null) return;

            string ext = Path.GetExtension(targetPath);

            // Handle association if requested
            if (alwaysUseCheck.Checked && !string.IsNullOrEmpty(ext))
            {
                if (selected.Tag is string elfPath)
                {
                    FileAssociations.RegisterElf(ext, elfPath);
                }
                else if (selected.Tag is Type appType)
                {
                    FileAssociations.Register(ext, appType);
                }
            }

            // Launch target
            try
            {
                if (selected.Tag is string elfPath)
                {
                    LaunchTracker.StartElf(elfPath, new string[] { elfPath, targetPath });
                }
                else if (selected.Tag is Type appType)
                {
                    if (appType == typeof(MediaPlayer))
                    {
                        LaunchTracker.Start(() => new MediaPlayer(targetPath, 120, 100, 720, 520));
                    }
                    else if (appType == typeof(BreezeEditor))
                    {
                        LaunchTracker.Start(() => new BreezeEditor(100, 80, 900, 620, targetPath));
                    }
                    else if (appType == typeof(ImageViewer))
                    {
                        LaunchTracker.Start(() => new ImageViewer(targetPath, 120, 120, 640, 480));
                    }
                    else if (appType == typeof(CommandPrompt))
                    {
                        LaunchTracker.Start(() => new CommandPrompt(100, 100, 720, 480));
                    }
                    else
                    {
                        var ctor = appType.GetConstructor(new[] { typeof(string) });
                        if (ctor != null)
                        {
                            LaunchTracker.Start(() => (Window)ctor.Invoke(new object[] { targetPath }));
                        }
                        else
                        {
                            LaunchTracker.Start(() => (Window)Activator.CreateInstance(appType)!);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SystemLogger.WriteLine("OpenWith", $"Failed to launch {selected.Tag}: {ex.Message}", ConsoleMessageType.Error);
            }

            WindowManager.Close(this);
        }
    }
}
