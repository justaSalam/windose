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
        // Register("TextBox", () => new TextBox(0, 0, 140, 22), false);   // enable once the constructor matches
    }
}