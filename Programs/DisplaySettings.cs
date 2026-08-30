using Cosmos.Kernel.System.Graphics;
using System.Drawing;
using Windose;
using Windose.System.GUI.Components;
using Windose.System.Kernel;

public sealed class DisplaySettings : Window
{
    private const string WidthKey = "System/Display/Width";
    private const string HeightKey = "System/Display/Height";
    private const string BppKey = "System/Display/BitsPerPixel";

    private ComboBox resolutionCombo;
    private ComboBox colorDepthCombo;
    private Checkbox snapToGrid;
    private Checkbox showGrid;
    private Slider gridWidthSlider;
    private Slider gridHeightSlider;
    private Label currentMode;
    private Label restartNotice;
    private Label gridWidthValue;
    private Label gridHeightValue;

    public DisplaySettings(int x = 150, int y = 110)
        : base(x, y, 520, 390, "Display Properties", true, new Png("/mnt/System/Icons/display_properties.png"))
    {
        canResize = false;
        canMaximize = false;

        DockPanel root = new DockPanel(0, 0, Width, Height)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(28, 8, 8, 8),
            Padding = new Thickness(8),
            useBackground = true,
            backgroundColor = Palette.ControlFace,
        };

        TabControl tabs = new TabControl(0, 0, 480, 290)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0),
        };

        TabPage displayPage = tabs.AddPage("Settings");
        TabPage desktopPage = tabs.AddPage("Desktop");

        BuildDisplayPage(displayPage);
        BuildDesktopPage(desktopPage);

        Panel buttons = new Panel(Palette.ControlFace, 0, 0, Width, 34)
        {
            useBackground = true,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 8, 0, 0),
        };

        Button ok = CreateButton(212, 4, "OK", () =>
        {
            ApplySettings();
            WindowManager.PostClose(this);
        });
        Button apply = CreateButton(298, 4, "Apply", ApplySettings);
        Button cancel = CreateButton(384, 4, "Cancel", () => WindowManager.PostClose(this));

        buttons.AddChild(ok);
        buttons.AddChild(apply);
        buttons.AddChild(cancel);

        root.AddDockChild(buttons, Dock.Bottom);
        root.AddDockChild(tabs, Dock.Fill);
        AddChild(root);

        resolutionCombo.SelectedIndex = FindResolutionIndex();
        colorDepthCombo.SelectedIndex = FindDepthIndex();
        snapToGrid.Checked = Registry.GetBoolean("System/Desktop/IconGridEnabled", true);
        showGrid.Checked = Registry.GetBoolean("System/Desktop/IconGridVisible", false);
        gridWidthSlider.Value = Registry.GetInteger("System/Desktop/IconGridWidth", 80);
        gridHeightSlider.Value = Registry.GetInteger("System/Desktop/IconGridHeight", 80);
        UpdateGridLabels();
        UpdateDisplayLabels();
    }

    private void BuildDisplayPage(TabPage page)
    {
        GroupBox monitor = new GroupBox(12, 16, 444, 86)
        {
            text = "Monitor",
            useBackground = false,
        };

        currentMode = CreatePlainLabel(0, 0, 380, 22, "");
        restartNotice = CreatePlainLabel(0, 0, 390, 34, "");
        restartNotice.textColor = Color.DarkRed;

        monitor.AddGroupChild(currentMode);
        monitor.AddGroupChild(restartNotice);

        GroupBox mode = new GroupBox(12, 116, 444, 128)
        {
            text = "Display mode",
            useBackground = false,
        };

        mode.AddGroupChild(CreatePlainLabel(0, 0, 390, 20, "Resolution"));
        resolutionCombo = new ComboBox(0, 0, 210)
        {
            text = "Select a resolution",
            fontSize = 14,
        };
        resolutionCombo.AddItem("640 x 480");
        resolutionCombo.AddItem("800 x 600");
        resolutionCombo.AddItem("1024 x 768");
        resolutionCombo.AddItem("1280 x 720");
        resolutionCombo.AddItem("1920 x 1080");
        resolutionCombo.SelectedIndexChanged += _ => UpdateDisplayLabels();
        mode.AddGroupChild(resolutionCombo);

        mode.AddGroupChild(CreatePlainLabel(0, 0, 390, 20, "Colors"));
        colorDepthCombo = new ComboBox(0, 0, 210)
        {
            text = "Select color depth",
            fontSize = 14,
        };
        colorDepthCombo.AddItem("4-bit");
        colorDepthCombo.AddItem("16-bit");
        colorDepthCombo.AddItem("24-bit");
        colorDepthCombo.AddItem("32-bit");
        colorDepthCombo.SelectedIndexChanged += _ => UpdateDisplayLabels();
        mode.AddGroupChild(colorDepthCombo);

        page.AddControl(monitor);
        page.AddControl(mode);
    }

    private void BuildDesktopPage(TabPage page)
    {
        GroupBox grid = new GroupBox(12, 16, 444, 190)
        {
            text = "Icon grid",
            useBackground = false,
        };

        snapToGrid = new Checkbox(0, 0)
        {
            text = "Snap icons to grid",
            fontSize = 14,
        };
        showGrid = new Checkbox(0, 0)
        {
            text = "Show icon grid",
            fontSize = 14,
        };

        gridWidthValue = CreatePlainLabel(0, 0, 390, 20, "");
        gridWidthSlider = new Slider(0, 0, 260)
        {
            Minimum = 48,
            Maximum = 160,
            SmallChange = 4,
            LargeChange = 8,
            showTicks = true,
        };
        gridWidthSlider.ValueChanged += _ => UpdateGridLabels();

        gridHeightValue = CreatePlainLabel(0, 0, 390, 20, "");
        gridHeightSlider = new Slider(0, 0, 260)
        {
            Minimum = 48,
            Maximum = 160,
            SmallChange = 4,
            LargeChange = 8,
            showTicks = true,
        };
        gridHeightSlider.ValueChanged += _ => UpdateGridLabels();

        grid.AddGroupChild(snapToGrid);
        grid.AddGroupChild(showGrid);
        grid.AddGroupChild(gridWidthValue);
        grid.AddGroupChild(gridWidthSlider);
        grid.AddGroupChild(gridHeightValue);
        grid.AddGroupChild(gridHeightSlider);

        Button compact = CreateButton(20, 222, "Compact", () => SetGridPreset(80, 76));
        Button comfortable = CreateButton(118, 222, "Comfortable", () => SetGridPreset(88, 84));
        Button wide = CreateButton(238, 222, "Wide", () => SetGridPreset(104, 92));

        page.AddControl(grid);
        page.AddControl(compact);
        page.AddControl(comfortable);
        page.AddControl(wide);
    }

    private Button CreateButton(int x, int y, string text, Action action)
    {
        return new Button(text,x, y, 78, 26)
        {
            text = text,
            clampSize = false,
            leftClickAction = action,
        };
    }

    private static Label CreatePlainLabel(int x, int y, int width, int height, string text)
    {
        return new Label(x, y, width, height)
        {
            text = text,
            fontSize = 14,
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0),
        };
    }

    private void ApplySettings()
    {
        GetSelectedResolution(out int width, out int height);
        Registry.Set(WidthKey, (long)width);
        Registry.Set(HeightKey, (long)height);
        Registry.Set(BppKey, (long)GetSelectedDepth());
        Registry.Set("System/Desktop/IconGridEnabled", snapToGrid.Checked);
        Registry.Set("System/Desktop/IconGridVisible", showGrid.Checked);
        Registry.Set("System/Desktop/IconGridWidth", (long)gridWidthSlider.Value);
        Registry.Set("System/Desktop/IconGridHeight", (long)gridHeightSlider.Value);

        UpdateDisplayLabels();
    }

    private void SetGridPreset(int width, int height)
    {
        gridWidthSlider.Value = width;
        gridHeightSlider.Value = height;
        UpdateGridLabels();
    }

    private void UpdateDisplayLabels()
    {
        long currentWidth = Registry.GetInteger("System/Display/CurrentWidth", Global.screenWidth);
        long currentHeight = Registry.GetInteger("System/Display/CurrentHeight", Global.screenHeight);
        GetSelectedResolution(out int requestedWidth, out int requestedHeight);

        currentMode.text = "Current display: " + currentWidth + " x " + currentHeight + ", " + GetSelectedDepth() + "-bit color";
        restartNotice.text = requestedWidth == currentWidth && requestedHeight == currentHeight
            ? "These settings match the current display."
            : "Resolution changes will apply after restart.";
        currentMode.MarkDirty();
        restartNotice.MarkDirty();
    }

    private void UpdateGridLabels()
    {
        if (gridWidthValue != null)
        {
            gridWidthValue.text = "Grid width: " + (int)gridWidthSlider.Value + " px";
            gridWidthValue.MarkDirty();
        }

        if (gridHeightValue != null)
        {
            gridHeightValue.text = "Grid height: " + (int)gridHeightSlider.Value + " px";
            gridHeightValue.MarkDirty();
        }
    }

    private int FindResolutionIndex()
    {
        long width = Registry.GetInteger(WidthKey, 1920);
        long height = Registry.GetInteger(HeightKey, 1080);
        string value = width + " x " + height;

        for (int i = 0; i < resolutionCombo.ItemCount; i++)
            if (resolutionCombo.GetItemAt(i).ToString() == value) return i;

        return 5;
    }

    private int FindDepthIndex()
    {
        long depth = Registry.GetInteger(BppKey, 32);
        if (depth == 16) return 0;
        if (depth == 24) return 1;
        return 2;
    }

    private void GetSelectedResolution(out int width, out int height)
    {
        switch (resolutionCombo.SelectedText)
        {
            case "640 x 480": width = 640; height = 480; return;
            case "800 x 600": width = 800; height = 600; return;
            case "1024 x 768": width = 1024; height = 768; return;
            case "1280 x 720": width = 1280; height = 720; return;
            default: width = 1920; height = 1080; return;
        }
    }

    private int GetSelectedDepth()
    {
        switch (colorDepthCombo.SelectedText)
        {
            case "4-bit": return 4;
            case "16-bit": return 16;
            case "24-bit": return 24;
            default: return 32;
        }
    }

    public override string GetComponentName() => "DisplaySettings";
}
