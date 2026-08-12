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
    public static int TitleBarHeight { get; private set; }
    public static int BorderSize { get; private set; }
    public static string ThemeName { get; private set; } = "classic";
    public static string ThemeDisplayName { get; private set; } = "Classic Windows";

    public static event Action ThemeChanged;

    private static bool initialized;

    static Palette()
    {
        ApplyTheme(CreateClassicTheme());
    }

    public static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        ApplyFromRegistry();
        SystemRegistry.Changed += OnRegistryChanged;
    }

    public static void ApplyFromRegistry()
    {
        Apply(SystemRegistry.GetString(ThemeRegistryKey, "classic"), persist: false);
    }

    public static bool Apply(string name, bool persist = true)
    {
        UiTheme theme = CreateClassicTheme();
        ApplyTheme(theme);

        if (persist)
        {
            string current = SystemRegistry.GetString(ThemeRegistryKey, theme.Name);
            if (!current.Equals(theme.Name, StringComparison.OrdinalIgnoreCase))
                SystemRegistry.Set(ThemeRegistryKey, theme.Name);
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
        TitleBarHeight = theme.TitleBarHeight;
        BorderSize = theme.BorderSize;

        ThemeChanged?.Invoke();
    }

    private static UiTheme CreateClassicTheme()
    {
        return new UiTheme
        {
            Name = "classic",
            DisplayName = "Classic Windows",
            Description = "Raised gray controls, blue title bars, and a teal desktop.",
            ControlFace = Color.FromArgb(192, 192, 192),
            ControlWhite = Color.White,
            ControlHighlight = Color.FromArgb(223, 223, 223),
            ControlShadow = Color.FromArgb(128, 128, 128),
            ControlBlack = Color.Black,
            ActiveTitle = Color.FromArgb(0, 0, 128),
            InactiveTitle = Color.FromArgb(128, 128, 128),
            Highlight = Color.FromArgb(0, 0, 128),
            HighlightText = Color.White,
            DesktopBackground = Color.FromArgb(0, 128, 128),
            WindowBackground = Color.FromArgb(192, 192, 192),
            WindowBorder = Color.FromArgb(128, 128, 128),
            TaskbarBackground = Color.FromArgb(192, 192, 192),
            MenuBackground = Color.FromArgb(192, 192, 192),
            TitleText = Color.White,
            TitleTextInactive = Color.White,
            // Classic theme: no transparency (fully opaque glass = same as base colors)
            TaskbarGlass = Color.FromArgb(192, 192, 192),
            WindowGlass = Color.FromArgb(192, 192, 192),
            MenuGlass = Color.FromArgb(192, 192, 192),
            TitleBarGlass = Color.FromArgb(0, 0, 128),
            TitleBarHeight = 20,
            BorderSize = 2,
            FlatControls = false,
        };
    }

}
