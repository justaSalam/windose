using System.Drawing;

public sealed class ThemeSettings : Window
{
    private readonly Panel statusPanel;
    private readonly Panel classicPreview;
    private readonly Panel modernPreview;
    private Button classicButton;
    private Button modernButton;

    public ThemeSettings(int x = 180, int y = 120, int width = 560, int height = 360)
        : base(x, y, width, height, "Theme Settings", true)
    {
        canResize = false;

        Panel body = new Panel(Palette.ControlFace, 0, 0, Width, Height)
        {
            useBackground = true,
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(28, 2, 2, 2),
            Padding = new Thickness(12),
        };

        body.AddChild(new Label(0, 0, Width, 24)
        {
            text = "Choose a visual theme for windows, menus, and the desktop.",
            fontSize = 16,
            textColor = Palette.ControlBlack,
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(12, 12, 12, 0),
        });

        classicPreview = CreatePreviewPanel(12, 52, 250, 180, true);
        modernPreview = CreatePreviewPanel(278, 52, 250, 180, false);
        body.AddChild(classicPreview);
        body.AddChild(modernPreview);

        classicButton = CreateThemeButton("Classic Windows", 12, 240, 250, 28, "classic");
        modernButton = CreateThemeButton("Modern Windose", 278, 240, 250, 28, "modern");
        body.AddChild(classicButton);
        body.AddChild(modernButton);

        statusPanel = new Panel(Palette.ControlFace, 12, 280, Width - 24, 24)
        {
            useBackground = false,
            text = "Current theme: " + Palette.ThemeDisplayName,
            fontSize = 16,
            textColor = Palette.ControlBlack,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(12, 280, 12, 12),
        };
        body.AddChild(statusPanel);

        AddChild(body);
        RefreshSelection();
        Palette.ThemeChanged += RefreshSelection;
    }

    private Panel CreatePreviewPanel(int x, int y, int width, int height, bool classic)
    {
        UiTheme theme = classic ? Palette.CreateTheme("classic") : Palette.CreateTheme("modern");

        Panel panel = new Panel(theme.WindowBackground, x, y, width, height)
        {
            useBackground = true,
            useBorders = true,
            borderColor = theme.WindowBorder,
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0),
        };

        panel.AddChild(new Panel(theme.ActiveTitle, 8, 8, width - 16, theme.TitleBarHeight)
        {
            useBackground = true,
            text = classic ? "Classic Window" : "Modern Window",
            textColor = theme.TitleText,
            fontSize = 14,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0),
        });

        panel.AddChild(new Panel(theme.WindowBackground, 8, 8 + theme.TitleBarHeight + 8, 72, 24)
        {
            useBackground = true,
            useBorders = !classic,
            borderColor = theme.WindowBorder,
            text = "Button",
            textColor = theme.ControlBlack,
            fontSize = 14,
            Margin = new Thickness(0),
        });

        panel.AddChild(new Label(8, height - 28, width - 16, 20)
        {
            text = theme.Description,
            fontSize = 13,
            textColor = theme.ControlBlack,
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0),
        });

        return panel;
    }

    private Button CreateThemeButton(string label, int x, int y, int width, int height, string themeName)
    {
        Button button = new Button(x, y, width, height)
        {
            text = label,
            fontSize = 16,
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0),
            leftMouseRelease = () => ApplyTheme(themeName),
        };
        return button;
    }

    private void ApplyTheme(string themeName)
    {
        Palette.Apply(themeName);
        statusPanel.text = "Current theme: " + Palette.ThemeDisplayName;
        statusPanel.MarkDirty();
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        bool classic = Palette.ThemeName == "classic";
        classicButton.useBorders = classic;
        modernButton.useBorders = !classic;
        classicButton.borderColor = Palette.Highlight;
        modernButton.borderColor = Palette.Highlight;
        classicButton.MarkDirty();
        modernButton.MarkDirty();
    }

    public override void Dispose()
    {
        Palette.ThemeChanged -= RefreshSelection;
        base.Dispose();
    }

    public override string GetName() => "ThemeSettings";
}
