using System.Drawing;

public class PropertyDescriptor
{
    public string Name = "";
    public Func<Component, string> Get = _ => "";
    public Action<Component, string> Set = (_, _) => { };
}

public class ComponentDescriptor
{
    public string TypeName = "";
    public Func<Component> Create = null!;
    public bool IsContainer;
    public List<PropertyDescriptor> Properties = new List<PropertyDescriptor>();

    public ComponentDescriptor Prop(string n, Func<Component, string> g, Action<Component, string> s)
    {
        Properties.Add(new PropertyDescriptor { Name = n, Get = g, Set = s });
        return this;
    }

    public PropertyDescriptor? Find(string n)
    {
        foreach (var p in Properties) if (p.Name == n) return p;
        return null;
    }
}

public static class UiRegistry
{
    public static readonly Dictionary<string, ComponentDescriptor> Types = new Dictionary<string, ComponentDescriptor>();

    public static ComponentDescriptor Register(string type, Func<Component> create, bool container)
    {
        var d = new ComponentDescriptor
        {
            TypeName = type,
            IsContainer = container,
            Create = () =>
            {
                Component c = create();
                c.designType = type;                                   // never rely on GetComponentName()
                c.horizontalAlignment = HorizontalAlignment.Left;      // anchors must not move designed controls
                c.verticalAlignment = VerticalAlignment.Top;
                return c;
            }
        };

        d.Prop("name", c => c.name, (c, v) => c.name = v);
        d.Prop("x", c => c.X.ToString(), (c, v) => { if (int.TryParse(v, out int n)) c.SetBounds(n, c.Y, c.Width, c.Height); });
        d.Prop("y", c => c.Y.ToString(), (c, v) => { if (int.TryParse(v, out int n)) c.SetBounds(c.X, n, c.Width, c.Height); });
        d.Prop("w", c => c.Width.ToString(), (c, v) => { if (int.TryParse(v, out int n)) Resized(c, n, c.Height); });
        d.Prop("h", c => c.Height.ToString(), (c, v) => { if (int.TryParse(v, out int n)) Resized(c, c.Width, n); });
        d.Prop("text", c => c.text ?? "", (c, v) => { c.text = v; c.MarkDirty(); });
        d.Prop("dock", c => c.dock.ToString(), (c, v) => { c.dock = ParseDock(v); (c.Parent as DockPanel)?.ResolveDockLayout(); });

        Types[type] = d;
        return d;
    }

    private static void Resized(Component c, int w, int h)
    {
        c.SetBounds(c.X, c.Y, w, h);
        (c.Parent as DockPanel)?.ResolveDockLayout();     // dock reads the child's own size
    }

    private static Dock ParseDock(string v) => v switch
    {
        "Top" => Dock.Top,
        "Bottom" => Dock.Bottom,
        "Left" => Dock.Left,
        "Right" => Dock.Right,
        "Fill" => Dock.Fill,
        _ => Dock.None
    };

    public static ComponentDescriptor? For(Component c)
        => c.designType.Length > 0 && Types.TryGetValue(c.designType, out var d) ? d : null;

    public static bool IsContainer(Component c) => For(c)?.IsContainer == true;

    public static void RegisterAll()
    {
        Register("DockPanel", () => new DockPanel(0, 0, 200, 100) { useBackground = true }, true)
            .Prop("useBackground", c => ((DockPanel)c).useBackground.ToString(),
                                   (c, v) => { ((DockPanel)c).useBackground = v == "True"; c.MarkDirty(); });

        Register("Button", () => new Button("Button", 0, 0, 80, 26), false);
        Register("Label", () => new Label(0, 0, 80, 20) { text = "Label", useBackground = false }, false);
        Register("ListView", () => new ListView(0, 0, 200, 120), false);
        Register("ComboBox", () => new ComboBox(0, 0, 160), false);

        Register("TextField", () => new TextField(0, 0, 140, 22), false)
            .Prop("placeholder", c => ((TextField)c).PlaceholderText, (c, v) => { ((TextField)c).PlaceholderText = v; c.MarkDirty(); })
            .Prop("readOnly", c => ((TextField)c).readOnly.ToString(), (c, v) => { ((TextField)c).readOnly = v == "True"; c.MarkDirty(); })
            .Prop("multiline", c => ((TextField)c).Multiline.ToString(), (c, v) => { ((TextField)c).Multiline = v == "True"; c.MarkDirty(); });

        Register("Checkbox", () => new Checkbox(0, 0) { text = "Checkbox" }, false)
            .Prop("checked", c => ((Checkbox)c).Checked.ToString(), (c, v) => { ((Checkbox)c).Checked = v == "True"; c.MarkDirty(); });

        Register("RadioButton", () => new RadioButton(0, 0) { text = "RadioButton", Group = "group1" }, false)
            .Prop("checked", c => ((RadioButton)c).Checked.ToString(), (c, v) => { ((RadioButton)c).Checked = v == "True"; c.MarkDirty(); })
            .Prop("group", c => ((RadioButton)c).Group, (c, v) => { ((RadioButton)c).Group = v; c.MarkDirty(); });

        Register("ProgressBar", () => new ProgressBar(0, 0, 140, 20), false)
            .Prop("value", c => ((ProgressBar)c).Value.ToString(), (c, v) => { if (float.TryParse(v, out float n)) ((ProgressBar)c).Value = n; })
            .Prop("min", c => ((ProgressBar)c).Minimum.ToString(), (c, v) => { if (float.TryParse(v, out float n)) ((ProgressBar)c).Minimum = n; })
            .Prop("max", c => ((ProgressBar)c).Maximum.ToString(), (c, v) => { if (float.TryParse(v, out float n)) ((ProgressBar)c).Maximum = n; });

        Register("Slider", () => new Slider(0, 0, 140), false)
            .Prop("value", c => ((Slider)c).Value.ToString(), (c, v) => { if (float.TryParse(v, out float n)) ((Slider)c).Value = n; })
            .Prop("min", c => ((Slider)c).Minimum.ToString(), (c, v) => { if (float.TryParse(v, out float n)) ((Slider)c).Minimum = n; })
            .Prop("max", c => ((Slider)c).Maximum.ToString(), (c, v) => { if (float.TryParse(v, out float n)) ((Slider)c).Maximum = n; });

        Register("NumericUpDown", () => new NumericUpDown(0, 0, 100, 24), false)
            .Prop("value", c => ((NumericUpDown)c).Value.ToString(), (c, v) => { if (float.TryParse(v, out float n)) ((NumericUpDown)c).Value = n; })
            .Prop("min", c => ((NumericUpDown)c).Minimum.ToString(), (c, v) => { if (float.TryParse(v, out float n)) ((NumericUpDown)c).Minimum = n; })
            .Prop("max", c => ((NumericUpDown)c).Maximum.ToString(), (c, v) => { if (float.TryParse(v, out float n)) ((NumericUpDown)c).Maximum = n; })
            .Prop("increment", c => ((NumericUpDown)c).Increment.ToString(), (c, v) => { if (float.TryParse(v, out float n)) ((NumericUpDown)c).Increment = n; });

        Register("Panel", () => new Panel(Color.FromArgb(255, 230, 230, 230), 0, 0, 160, 100) { useBackground = true }, true)
            .Prop("useBackground", c => ((Panel)c).useBackground.ToString(), (c, v) => { ((Panel)c).useBackground = v == "True"; c.MarkDirty(); })
            .Prop("useBorders", c => ((Panel)c).useBorders.ToString(), (c, v) => { ((Panel)c).useBorders = v == "True"; c.MarkDirty(); });

        Register("GroupBox", () => new GroupBox(0, 0, 200, 140) { text = "Group Box" }, true)
            .Prop("useBackground", c => ((GroupBox)c).useBackground.ToString(), (c, v) => { ((GroupBox)c).useBackground = v == "True"; c.MarkDirty(); });

        Register("Separator", () => new Separator(0, 0, 140, 6) { orientation = LayoutOrientation.Horizontal }, false)
            .Prop("orientation", c => ((Separator)c).orientation.ToString(), (c, v) => { ((Separator)c).orientation = v == "Vertical" ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal; c.MarkDirty(); });

        Register("LinkLabel", () => new LinkLabel("Link", 0, 0, 80, 20), false)
            .Prop("underline", c => ((LinkLabel)c).underline.ToString(), (c, v) => { ((LinkLabel)c).underline = v == "True"; c.MarkDirty(); });
    }
}
