using System.Globalization;
using Windose.System.GUI.Components;

public sealed class RegistryValueDialog : Window
{
    private readonly string parentPath;
    private readonly string existingKey;
    private readonly RegistryValueKind kind;
    private readonly Action saved;
    private readonly TextField nameField;
    private readonly TextField valueField;
    private readonly TextField descriptionField;
    private readonly TextField restartField;
    private readonly Panel errorLabel;

    public RegistryValueDialog(string parentPath, RegistryValueKind kind, Action saved, int x, int y)
        : this(parentPath, null, kind, "", "", false, false, saved, x, y)
    {
    }

    public RegistryValueDialog(RegistryEntry entry, Action saved, int x, int y)
        : this(RegistryEditor.GetParentKey(entry.Key), entry.Key, KindFromValue(entry.Value),
            RegistryEditor.FormatValue(entry.Value), entry.Description, entry.RequiresRestart,
            entry.IsBuiltIn, saved, x, y)
    {
    }

    private RegistryValueDialog(string parentPath, string existingKey, RegistryValueKind kind,
        string value, string description, bool requiresRestart, bool metadataReadOnly,
        Action saved, int x, int y)
        : base(x, y, 520, 300, existingKey == null ? "New Registry Value" : "Modify Registry Value", true)
    {
        this.parentPath = parentPath ?? "";
        this.existingKey = existingKey;
        this.kind = kind;
        this.saved = saved;
        canResize = false;
        canMaximize = false;

        Panel body = new Panel(Palette.ControlFace, 0, 0, Width, Height)
        {
            Margin = new Thickness(28, 8, 8, 8),
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            clampSize = false,
            useBackground = true,
        };

        AddLabel(body, "Name:", 12, 14, 110);
        nameField = AddField(body, 124, 10, 372, 26,
            existingKey == null ? "NewValue" : RegistryEditor.GetLeafName(existingKey));
        nameField.readOnly = existingKey != null;

        AddLabel(body, "Type:", 12, 50, 110);
        AddLabel(body, kind.ToString(), 124, 50, 250);

        AddLabel(body, "Value:", 12, 84, 110);
        valueField = AddField(body, 124, 80, 372, 26, value);

        AddLabel(body, "Description:", 12, 120, 110);
        descriptionField = AddField(body, 124, 116, 372, 26, description);
        descriptionField.readOnly = metadataReadOnly;

        AddLabel(body, "Restart required:", 12, 156, 140);
        restartField = AddField(body, 154, 152, 90, 26, requiresRestart ? "true" : "false");
        restartField.readOnly = metadataReadOnly;

        errorLabel = new Panel(Palette.ControlFace, 12, 188, 484, 24)
        {
            fontSize = 14,
            textColor = System.Drawing.Color.FromArgb(160, 0, 0),
            useBackground = false,
            clampSize = false,
            Margin = new Thickness(0),
        };

        Button saveButton = new Button("Ok", 332, 220, 78, 26)
        {
            clampSize = false,
            Margin = new Thickness(0),
            leftClickAction = Save,
        };
        Button cancelButton = new Button("Cancel", 418, 220, 78, 26)
        {
            text = "Cancel",
            clampSize = false,
            Margin = new Thickness(0),
            leftClickAction = () => WindowManager.PostClose(this),
        };

        body.AddChild(errorLabel);
        body.AddChild(saveButton);
        body.AddChild(cancelButton);
        AddChild(body);
    }

    private void Save()
    {
        string name = (nameField.text ?? "").Trim();
        if (existingKey != null) name = RegistryEditor.GetLeafName(existingKey);
        if (name == "" || name.Contains("\\") || name.StartsWith("/") || name.EndsWith("/") || name.Contains("//"))
        {
            ShowError("Enter a name or relative path using single '/'.");
            return;
        }

        string key = existingKey ?? Registry.NormalizeKey(parentPath == "" ? name : parentPath + "/" + name);
        if (existingKey == null && Registry.Exists(key))
        {
            ShowError("A value with this name already exists.");
            return;
        }

        if (!TryParseValue(valueField.text, kind, out object value, out string error))
        {
            ShowError(error);
            return;
        }
        if (!bool.TryParse((restartField.text ?? "").Trim(), out bool requiresRestart))
        {
            ShowError("Restart required must be true or false.");
            return;
        }

        RegistryEntry current = existingKey == null ? null : Registry.GetEntry(existingKey);
        if (current == null || !current.IsBuiltIn)
            Registry.Define(key, value, descriptionField.text ?? "", requiresRestart);
        Registry.Set(key, value);
        Registry.Save();
        saved?.Invoke();
        WindowManager.PostClose(this);
    }

    private void ShowError(string message)
    {
        errorLabel.text = message;
        errorLabel.MarkDirty();
        ForceDirty();
    }

    private static bool TryParseValue(string text, RegistryValueKind kind, out object value, out string error)
    {
        string input = text ?? "";
        switch (kind)
        {
            case RegistryValueKind.Integer:
                if (long.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer))
                { value = integer; error = ""; return true; }
                value = null; error = "Enter a whole number."; return false;
            case RegistryValueKind.Number:
                if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                { value = number; error = ""; return true; }
                value = null; error = "Enter a number using '.' as the decimal separator."; return false;
            case RegistryValueKind.Boolean:
                if (bool.TryParse(input, out bool boolean))
                { value = boolean; error = ""; return true; }
                value = null; error = "Enter true or false."; return false;
            default:
                value = input; error = ""; return true;
        }
    }

    private static RegistryValueKind KindFromValue(object value)
    {
        if (value is bool) return RegistryValueKind.Boolean;
        if (value is long || value is int) return RegistryValueKind.Integer;
        if (value is double || value is float) return RegistryValueKind.Number;
        return RegistryValueKind.String;
    }

    private static void AddLabel(Panel parent, string text, int x, int y, int width)
    {
        parent.AddChild(new Label(x, y, width, 24)
        {
            text = text,
            fontSize = 16,
            useBackground = false,
            clampSize = false,
            Margin = new Thickness(0),
        });
    }

    private static TextField AddField(Panel parent, int x, int y, int width, int height, string text)
    {
        TextField field = new TextField(x, y, width, height)
        {
            text = text ?? "",
            fontSize = 16,
            clampSize = false,
            Margin = new Thickness(0),
        };
        parent.AddChild(field);
        return field;
    }
}

public sealed class RegistryDeleteDialog : Window
{
    public RegistryDeleteDialog(RegistryEntry entry, Action deleted, int x, int y)
        : base(x, y, 460, 190, "Confirm Value Delete", true)
    {
        canResize = false;
        canMaximize = false;

        Panel body = new Panel(Palette.ControlFace, 0, 0, Width, Height)
        {
            Margin = new Thickness(28, 8, 8, 8),
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            clampSize = false,
            useBackground = true,
        };
        body.AddChild(new Label(12, 16, 420, 48)
        {
            text = "Delete '" + entry.Key + "'?",
            fontSize = 16,
            useBackground = false,
            clampSize = false,
            Margin = new Thickness(0),
        });
        body.AddChild(new Button("Delete", 270, 92, 78, 26)
        {
            clampSize = false,
            Margin = new Thickness(0),
            leftClickAction = () =>
            {
                Registry.Delete(entry.Key);
                deleted?.Invoke();
                WindowManager.PostClose(this);
            },
        });
        body.AddChild(new Button("Cancel",356, 92, 78, 26)
        {
            clampSize = false,
            Margin = new Thickness(0),
            leftClickAction = () => WindowManager.PostClose(this),
        });
        AddChild(body);
    }
}
