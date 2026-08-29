using Windose.System.GUI.Components;

public class AddressBar : Component
{
    public Panel label;
    public TextField addressField;
    public Button dropDownButton;

    public AddressBar(int x, int y, int width, int height = 26) : base(x, y, width, height)
    {
        clampSize = false;

        label = new Panel(Palette.ControlFace, 4, 3, 64, height - 6)
        {
            text = "Address",
            fontSize = 16,
            useBackground = false,
            textColor = Palette.ControlBlack,
            clampSize = false,
            Margin = new Thickness(3, 3, 0, 4),
        };

        addressField = new TextField(68, 3, width - 92, height - 6)
        {
            fontSize = 16,
            text = "",
            clampSize = false,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(3, 3, 24, 68),
        };

        dropDownButton = new Button("v",width - 22, 3, 18, height - 6)
        {
            useBorders = true,
            clampSize = false,
            horizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(3, 3, 4, 3),
        };

        AddChild(label);
        AddChild(addressField);
        AddChild(dropDownButton);
    }

    public string Address
    {
        get { return addressField.text; }
        set
        {
            addressField.text = value;
            addressField.MarkDirty();
        }
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        label.Resize(64, Math.Max(1, height - 6));
        addressField.Resize(Math.Max(1, width - 92), Math.Max(1, height - 6));
        dropDownButton.Resize(18, Math.Max(1, height - 6));
        ResolveChildren();
    }

    public override void Draw()
    {
        base.Draw();
    }

    public override void DrawLocal()
    {
        DrawFilledRectangle(Palette.ControlFace, 0, 0, Width, Height);
        DrawLine(Palette.ControlWhite, 0, 0, Width - 1, 0);
        DrawLine(Palette.ControlShadow, 0, Height - 1, Width - 1, Height - 1);

        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            DrawChild(child);
        }
    }

    public override string GetComponentName() => "AddressBar";
}
