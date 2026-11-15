using System;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using Windose.System.GUI.Components;
using Windose.System.Kernel;
using Windose.System.Kernel.Subsystem;
using Windose.System.System_Calls;

public sealed class CreateShortcutDialog : Window
{
    private readonly TextField targetField;
    private readonly TextField nameField;
    private readonly Label statusLabel;
    private readonly string destinationFolder;
    private string autoDerivedName = "";

    public CreateShortcutDialog(string destFolder = "/mnt/user/desktop", string initialTarget = "", int x = -1, int y = -1)
        : base(x > 0 ? x : Math.Max(20, (Global.screenWidth - 480) / 2),
               y > 0 ? y : Math.Max(20, (Global.screenHeight - 260) / 2),
               480, 260, "Create Shortcut", true)
    {
        destinationFolder = string.IsNullOrEmpty(destFolder) ? "/mnt/user/desktop" : destFolder;
        canResize = false;
        canMaximize = false;
        try { SetIcon("shell32.dll,-264"); } catch { }

        var layout = new DialogLayout(this, showFooterSeparator: true);
        layout.SetHeader("What item would you like to create a shortcut for?\nEnter the path of the program, file, or folder, or click Browse:", Icon);

        targetField = new TextField(0, 0, 100, 26)
        {
            text = initialTarget ?? "",
            PlaceholderText = "e.g. /mnt/Programs/calc.elf or /mnt/user"
        };
        targetField.TextChanged += OnTargetChanged;

        Button browseButton = new Button("Browse...", 0, 0, 84, 26)
        {
            leftClickAction = OnBrowseClicked
        };

        layout.AddRow("Location:", targetField, browseButton);

        string defaultName = !string.IsNullOrEmpty(initialTarget) ? DeriveFriendlyName(initialTarget) : "";
        autoDerivedName = defaultName;

        nameField = new TextField(0, 0, 100, 26)
        {
            text = defaultName,
            PlaceholderText = "e.g. Calculator"
        };
        layout.AddRow("Shortcut name:", nameField);

        statusLabel = new Label(0, 0, 100, 18) { text = "" };
        layout.AddStatus(statusLabel);

        Button createButton = new Button("Create", 0, 0, 80, 26) { leftClickAction = OnCreateClicked };
        Button cancelButton = new Button("Cancel", 0, 0, 80, 26) { leftClickAction = () => WindowManager.PostClose(this) };
        layout.SetButtons(createButton, cancelButton);
    }

    private void OnTargetChanged(string newTarget)
    {
        if (string.IsNullOrEmpty(nameField.text) || nameField.text == autoDerivedName)
        {
            autoDerivedName = DeriveFriendlyName(newTarget);
            nameField.text = autoDerivedName;
            nameField.MarkDirty();
        }
    }

    private void OnBrowseClicked()
    {
        string startDir = "/mnt";
        string currentVal = targetField.text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(currentVal))
        {
            if (Directory.Exists(currentVal))
            {
                startDir = currentVal;
            }
            else
            {
                string? dir = Path.GetDirectoryName(currentVal);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    startDir = dir;
            }
        }
        else if (Directory.Exists("/mnt/Programs"))
        {
            startDir = "/mnt/Programs";
        }

        FileDialogOptions options = new FileDialogOptions
        {
            Title = "Select Target for Shortcut",
            InitialPath = startDir,
            FilterDescription = "All items (*.*)",
            AcceptButtonText = "Select"
        };

        LaunchTracker.Start(() => new FileDialog(options, selectedPath =>
        {
            if (!string.IsNullOrEmpty(selectedPath))
            {
                targetField.text = selectedPath;
                autoDerivedName = DeriveFriendlyName(selectedPath);
                nameField.text = autoDerivedName;
                targetField.MarkDirty();
                nameField.MarkDirty();
                statusLabel.text = "";
                statusLabel.MarkDirty();
            }
        }));
    }

    private void OnCreateClicked()
    {
        string target = (targetField.text ?? "").Trim();
        if (string.IsNullOrEmpty(target))
        {
            statusLabel.text = "Please enter or browse to a location.";
            statusLabel.MarkDirty();
            return;
        }

        // Normalization: if target is relative, check in /mnt/Programs/
        if (!target.StartsWith("/") && !target.StartsWith("\\"))
        {
            string programPath = "/mnt/Programs/" + target;
            if (File.Exists(programPath))
                target = programPath;
            else if (File.Exists(programPath + ".elf"))
                target = programPath + ".elf";
        }

        string name = (nameField.text ?? "").Trim();
        if (string.IsNullOrEmpty(name))
        {
            name = DeriveFriendlyName(target);
        }

        if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4).Trim();
        else if (name.EndsWith("lnk", StringComparison.OrdinalIgnoreCase) && name.Length > 3)
            name = name.Substring(0, name.Length - 3).Trim();

        if (string.IsNullOrEmpty(name))
        {
            name = "Shortcut";
        }

        if (!Directory.Exists(destinationFolder))
        {
            try { Directory.CreateDirectory(destinationFolder); } catch { }
        }

        string lnkPath = FileSystemManager.GetUniquePath(destinationFolder, name, ".lnk");
        string? iconPath = null;
        if (Directory.Exists(target))
        {
            iconPath = "imageres.dll,-3";
        }
        else
        {
            iconPath = FileAssociations.GetIconPath(target);
        }

        bool ok = ShortcutFile.Create(lnkPath, target, name, iconPath);
        if (!ok)
        {
            statusLabel.text = "Failed to create shortcut file.";
            statusLabel.MarkDirty();
            return;
        }

        // Refresh desktop icons if created on desktop
        string normDest = destinationFolder.Replace('\\', '/').TrimEnd('/');
        if (normDest.Equals("/mnt/user/desktop", StringComparison.OrdinalIgnoreCase))
        {
            Desktop.Instance?.RefreshDesktopIcons();
        }

        WindowManager.PostClose(this);
    }

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        if (keyEvent.Key == Key.Enter)
        {
            OnCreateClicked();
            return;
        }
        if (keyEvent.Key == Key.Escape)
        {
            WindowManager.PostClose(this);
            return;
        }
        base.HandleKeyboard(keyEvent);
    }

    private static string DeriveFriendlyName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        string fn = Path.GetFileName(path.TrimEnd('/', '\\'));
        if (string.IsNullOrEmpty(fn)) return "Shortcut";

        if (fn.EndsWith(".elf", StringComparison.OrdinalIgnoreCase))
            fn = fn.Substring(0, fn.Length - 4);
        else if (fn.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            fn = fn.Substring(0, fn.Length - 4);

        if (fn.Length == 0) return "Shortcut";

        // Title-case single words e.g. "calc" -> "Calc"
        if (fn.Length == 1) return fn.ToUpper();
        return char.ToUpper(fn[0]) + fn.Substring(1);
    }
}
