using System;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using Windose.System.GUI.Components;
using Windose.System.Kernel;
using Windose.System.Kernel.Subsystem;
using Windose.System.System_Calls;

namespace Windose.Programs
{
    public sealed class RenameDialog : Window
    {
        private readonly string targetPath;
        private readonly bool isDirectory;
        private readonly string extension;
        private readonly TextField nameField;
        private readonly Action onRenamed;

        public RenameDialog(string path, Action? onRenamed = null, int x = -1, int y = -1)
            : base(x > 0 ? x : Math.Max(20, (Global.screenWidth - 380) / 2),
                   y > 0 ? y : Math.Max(20, (Global.screenHeight - 160) / 2),
                   380, 160, "Rename", true)
        {
            targetPath = path;
            this.onRenamed = onRenamed;
            canResize = false;
            canMaximize = false;
            try { SetIcon("shell32.dll,-134"); } catch { }

            isDirectory = Directory.Exists(path);
            string currentName = Path.GetFileName(path.TrimEnd('/', '\\'));
            extension = isDirectory ? "" : Path.GetExtension(currentName);
            string baseName = isDirectory ? currentName : Path.GetFileNameWithoutExtension(currentName);

            var layout = new DialogLayout(this, showFooterSeparator: true);
            layout.SetHeader("Enter a new name:", Icon);

            nameField = new TextField(0, 0, 100, 26)
            {
                text = baseName
            };
            layout.AddControl(nameField);

            Button renameButton = new Button("Rename", 0, 0, 80, 26)
            {
                leftClickAction = PerformRename
            };

            Button cancelButton = new Button("Cancel", 0, 0, 80, 26)
            {
                leftClickAction = () => WindowManager.PostClose(this)
            };

            layout.SetButtons(renameButton, cancelButton);
        }

        private void PerformRename()
        {
            string newBase = (nameField.text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(newBase) || newBase == "." || newBase == ".." ||
                newBase.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
            {
                return;
            }

            if (!string.IsNullOrEmpty(extension) && newBase.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                newBase = newBase.Substring(0, newBase.Length - extension.Length);
            }

            string newFullName = isDirectory ? newBase : (newBase + extension);
            string parentDir = Path.GetDirectoryName(targetPath) ?? "/mnt";
            string newPath = Path.Combine(parentDir, newFullName);

            if (string.Equals(targetPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                WindowManager.PostClose(this);
                return;
            }

            try
            {
                if (isDirectory)
                {
                    if (Directory.Exists(newPath)) return;
                    Directory.Move(targetPath, newPath);
                }
                else
                {
                    if (File.Exists(newPath)) return;
                    File.Move(targetPath, newPath);
                }

                onRenamed?.Invoke();
            }
            catch (Exception ex)
            {
                SystemLogger.WriteLine("Rename", $"Failed to rename: {ex.Message}", ConsoleMessageType.Error);
            }

            WindowManager.PostClose(this);
        }

        public override void HandleKeyboard(KeyEvent keyEvent)
        {
            if (keyEvent.Key == Key.Enter)
            {
                PerformRename();
                return;
            }
            if (keyEvent.Key == Key.Escape)
            {
                WindowManager.PostClose(this);
                return;
            }
            base.HandleKeyboard(keyEvent);
        }
    }
}
