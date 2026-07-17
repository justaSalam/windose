using System.Drawing;
using System.Globalization;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Mouse;

public class Desktop : Component
{
    private Color backgroundColor;

    private MenuPopup contextMenu;

    public Desktop(int x, int y, int width, int height) : base(x, y, width, height)
    {
        zLayer = DrawLayer.Desktop;

        ApplyRegistryBackground();

        SystemRegistry.Changed += OnRegistryChanged;

        contextMenu = new MenuPopup(250, 24 * 3)
        {
            itemHeight = 20
        };

        rightClickAction = () =>
        {
            int x = Math.Min(MouseManager.X, Math.Max(0, Global.screenWidth - contextMenu.Width));
            int y = Math.Min(MouseManager.Y, Math.Max(0, Global.screenHeight - contextMenu.Height));
            contextMenu.ShowAt(x, y);

            MarkDirty();
        };


        contextMenu.AddItem("Refresh");

        MenuItem viewItem = contextMenu.AddItem("View");
        viewItem.AddSubmenuItem("Large Icons");
        viewItem.AddSubmenuItem("Medium Icons");
        viewItem.AddSubmenuItem("Small Icons");
        viewItem.AddSubmenuSeparator();
        viewItem.AddSubmenuItem("Toggle Icons");

        contextMenu.AddSeparator();
        contextMenu.AddItem("Paste");
        contextMenu.AddSeparator();

        MenuItem newItem = contextMenu.AddItem("New");
        newItem.AddSubmenuItem("File");
        newItem.AddSubmenuItem("Folder");
        newItem.AddSubmenuSeparator();
        newItem.AddSubmenuItem("Breeze Application");


        contextMenu.AddSeparator();
        contextMenu.AddItem("Display Settings");
        contextMenu.AddItem("Personalise");

    }


    public override void Update()
    {
        // The desktop is a background layer; the compositor handles redraw dependencies.
    }
    public override void DrawLocal()
    {
        DrawFilledRectangle(backgroundColor, 0, 0, Width, Height);
    }

    private void OnRegistryChanged(RegistryChange change)
    {
        if (!change.Key.Equals("System/Desktop/BackgroundColor", StringComparison.OrdinalIgnoreCase) &&
            !change.Key.Equals("System/Theme/Name", StringComparison.OrdinalIgnoreCase)) return;
        ApplyRegistryBackground();
        ForceDirty();
    }

    private void ApplyRegistryBackground()
    {
        string value = SystemRegistry.GetString("System/Desktop/BackgroundColor", "theme");
        backgroundColor = IsThemeBackground(value)
            ? Palette.DesktopBackground
            : TryParseColor(value, out Color color) ? color : Palette.DesktopBackground;
    }

    private static bool IsThemeBackground(string value)
    {
        return string.IsNullOrWhiteSpace(value) ||
            value.Trim().Equals("theme", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseColor(string value, out Color color)
    {
        string hex = (value ?? "").Trim().TrimStart('#');
        if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            color = Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            return true;
        }
        color = Color.Empty;
        return false;
    }

    public override void Dispose()
    {
        SystemRegistry.Changed -= OnRegistryChanged;
        base.Dispose();
    }

}
