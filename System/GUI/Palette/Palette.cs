using System;
using System.Drawing;

public sealed class UiTheme
{
    public string Name = "";
    public string DisplayName = "";
    public string Description = "";

    public Color ControlFace;
    public Color ControlWhite;
    public Color ControlHighlight;
    public Color ControlShadow;
    public Color ControlBlack;
    public Color ActiveTitle;
    public Color InactiveTitle;
    public Color Highlight;
    public Color HighlightText;
    public Color DesktopBackground;
    public Color WindowBackground;
    public Color WindowBorder;
    public Color TaskbarBackground;
    public Color MenuBackground;
    public Color TitleText;
    public Color TitleTextInactive;

    // Glass / translucent color variants for modern transparency effects
    public Color TaskbarGlass;
    public Color WindowGlass;
    public Color MenuGlass;
    public Color TitleBarGlass;

    public int TitleBarHeight;
    public int BorderSize;
    public bool FlatControls;
    public bool GlassEnabled;
}

public static class Palette
{
    private const string ThemeRegistryKey = "System/Theme/Name";

    public static readonly string[] AvailableThemes = { "classic", "modern" };

    public static Color ControlFace { get; private set; }
    public static Color ControlWhite { get; private set; }
    public static Color ControlHighlight { get; private set; }
    public static Color ControlShadow { get; private set; }
    public static Color ControlBlack { get; private set; }
    public static Color ActiveTitle { get; private set; }
    public static Color InactiveTitle { get; private set; }
    public static Color Highlight { get; private set; }
    public static Color HighlightText { get; private set; }
    public static Color DesktopBackground { get; private set; }
    public static Color WindowBackground { get; private set; }
    public static Color WindowBorder { get; private set; }
    public static Color TaskbarBackground { get; private set; }
    public static Color MenuBackground { get; private set; }
    public static Color TitleText { get; private set; }
    public static Color TitleTextInactive { get; private set; }
    public static Color TaskbarGlass { get; private set; }
    public static Color WindowGlass { get; private set; }
    public static Color MenuGlass { get; private set; }
    public static Color TitleBarGlass { get; private set; }
    public static int TitleBarHeight { get; private set; }
    public static int BorderSize { get; private set; }
    public static bool FlatControls { get; private set; }
    public static bool GlassEnabled { get; private set; }
    public static string ThemeName { get; private set; } = "modern";
    public static string ThemeDisplayName { get; private set; } = "Aero";

    public static event Action ThemeChanged;

    private static bool initialized;

    static Palette()
    {
        ApplyTheme(CreateModernTheme());
    }

    public static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        ApplyFromRegistry();
        Registry.Changed += OnRegistryChanged;
    }

    public static void ApplyFromRegistry()
    {
        Apply(Registry.GetString(ThemeRegistryKey, "modern"), persist: false);
    }

    public static bool Apply(string name, bool persist = true)
    {
        string normalized = (name ?? "").Trim().ToLowerInvariant();
        UiTheme theme;
        if (normalized == "classic")
            theme = CreateClassicTheme();
        else
            theme = CreateModernTheme();

        ApplyTheme(theme);

        if (persist)
        {
            string current = Registry.GetString(ThemeRegistryKey, theme.Name);
            if (!current.Equals(theme.Name, StringComparison.OrdinalIgnoreCase))
                Registry.Set(ThemeRegistryKey, theme.Name);
        }

        return true;
    }


    public static bool IsKnownTheme(string name)
    {
        string normalized = (name ?? "").Trim().ToLowerInvariant();
        for (int i = 0; i < AvailableThemes.Length; i++)
        {
            if (AvailableThemes[i].Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void OnRegistryChanged(RegistryChange change)
    {

    }

    private static void ApplyTheme(UiTheme theme)
    {
        ThemeName = theme.Name;
        ThemeDisplayName = theme.DisplayName;
        ControlFace = theme.ControlFace;
        ControlWhite = theme.ControlWhite;
        ControlHighlight = theme.ControlHighlight;
        ControlShadow = theme.ControlShadow;
        ControlBlack = theme.ControlBlack;
        ActiveTitle = theme.ActiveTitle;
        InactiveTitle = theme.InactiveTitle;
        Highlight = theme.Highlight;
        HighlightText = theme.HighlightText;
        DesktopBackground = theme.DesktopBackground;
        WindowBackground = theme.WindowBackground;
        WindowBorder = theme.WindowBorder;
        TaskbarBackground = theme.TaskbarBackground;
        MenuBackground = theme.MenuBackground;
        TitleText = theme.TitleText;
        TitleTextInactive = theme.TitleTextInactive;
        TaskbarGlass = theme.TaskbarGlass;
        WindowGlass = theme.WindowGlass;
        MenuGlass = theme.MenuGlass;
        TitleBarGlass = theme.TitleBarGlass;
        TitleBarHeight = theme.TitleBarHeight;
        BorderSize = theme.BorderSize;
        FlatControls = theme.FlatControls;
        GlassEnabled = theme.GlassEnabled;

        ThemeChanged?.Invoke();
    }

    private static UiTheme CreateClassicTheme()
    {
        return new UiTheme
        {
            Name = "classic",
            DisplayName = "Warm Classic",
            Description = "Raised cream controls, sage title bars, and a soft green desktop.",
            ControlFace = Color.FromArgb(218, 211, 192),
            ControlWhite = Color.FromArgb(250, 246, 232),
            ControlHighlight = Color.FromArgb(255, 252, 242),
            ControlShadow = Color.FromArgb(151, 148, 127),
            ControlBlack = Color.FromArgb(43, 47, 39),
            ActiveTitle = Color.FromArgb(76, 101, 79),
            InactiveTitle = Color.FromArgb(133, 143, 119),
            Highlight = Color.FromArgb(105, 132, 101),
            HighlightText = Color.FromArgb(255, 252, 242),
            DesktopBackground = Color.FromArgb(120, 139, 108),
            WindowBackground = Color.FromArgb(218, 211, 192),
            WindowBorder = Color.FromArgb(104, 111, 91),
            TaskbarBackground = Color.FromArgb(218, 211, 192),
            MenuBackground = Color.FromArgb(235, 228, 207),
            TitleText = Color.FromArgb(255, 252, 242),
            TitleTextInactive = Color.FromArgb(250, 246, 232),
            TaskbarGlass = Color.FromArgb(218, 211, 192),
            WindowGlass = Color.FromArgb(218, 211, 192),
            MenuGlass = Color.FromArgb(235, 228, 207),
            TitleBarGlass = Color.FromArgb(76, 101, 79),
            TitleBarHeight = 20,
            BorderSize = 2,
            FlatControls = false,
            GlassEnabled = false,
        };
    }

    private static UiTheme CreateModernTheme()
    {
        return new UiTheme
        {
            Name = "modern",
            DisplayName = "Aero",
            Description = "Windows 7-style translucent glass, blue title bars, and soft gradients.",
            // Controls: soft neutral grey like Win7
            ControlFace = Color.FromArgb(240, 240, 240),
            ControlWhite = Color.FromArgb(255, 255, 255),
            ControlHighlight = Color.FromArgb(229, 243, 255),
            ControlShadow = Color.FromArgb(160, 160, 160),
            ControlBlack = Color.FromArgb(0, 0, 0),
            // Title bars: Win7 blue
            ActiveTitle = Color.FromArgb(52, 100, 168),
            InactiveTitle = Color.FromArgb(131, 151, 180),
            // Selection highlight: Win7 blue
            Highlight = Color.FromArgb(51, 153, 255),
            HighlightText = Color.FromArgb(255, 255, 255),
            // Desktop
            DesktopBackground = Color.FromArgb(58, 110, 165),
            // Window body
            WindowBackground = Color.FromArgb(245, 246, 247),
            WindowBorder = Color.FromArgb(100, 130, 180),
            // Taskbar: dark translucent
            TaskbarBackground = Color.FromArgb(28, 42, 58),
            // Menus
            MenuBackground = Color.FromArgb(245, 246, 247),
            // Title text
            TitleText = Color.FromArgb(255, 255, 255),
            TitleTextInactive = Color.FromArgb(200, 206, 216),
            // Glass colours
            TaskbarGlass = Color.FromArgb(180, 28, 42, 58),
            WindowGlass = Color.FromArgb(140, 116, 150, 194),
            MenuGlass = Color.FromArgb(220, 245, 246, 247),
            TitleBarGlass = Color.FromArgb(120, 52, 100, 168),
            // Frame dimensions
            TitleBarHeight = 28,
            BorderSize = 4,
            FlatControls = true,
            GlassEnabled = true,
        };
    }

}

