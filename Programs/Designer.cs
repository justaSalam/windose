using System.Drawing;
using Windose.System.Kernel;

public sealed class DesignerWindow : Window
{
    private const int TitleBar = 28;

    private readonly DockPanel root;
    private readonly ListView toolbox;
    private readonly ListView propGrid;
    private readonly DesignerSurface surface;
    private readonly TextField editBox;
    private readonly TextField pathBox;
    private readonly Label statusLabel;

    private PropertyDescriptor? editProp;

    public DesignerWindow(int x, int y, int width, int height)
        : base(x, y, width, height, "UI Designer", true)
    {
        if (UiRegistry.Types.Count == 0) UiRegistry.RegisterAll();

        root = new DockPanel(0, TitleBar, Width, Height - TitleBar) { useBackground = true };
        AddChild(root);                                         // attach first

        // toolbar
        var toolbar = new DockPanel(0, 0, Width, 30);
        AddToolbarButton(toolbar, "New", 60, NewForm);
        AddToolbarButton(toolbar, "Open", 60, OpenForm);
        AddToolbarButton(toolbar, "Save", 60, SaveForm);
        AddToolbarButton(toolbar, "Preview", 80, Preview);
        AddToolbarButton(toolbar, "Undo", 60, () => surface.Undo());
        AddToolbarButton(toolbar, "Redo", 60, () => surface.Redo());
        AddToolbarButton(toolbar, "Copy", 60, () => surface.DuplicateSelected());
        AddToolbarButton(toolbar, "Delete", 70, () => surface.DeleteSelected());

        // toolbox
        toolbox = new ListView(0, 0, 150, 100)
        {
            Margin = new Thickness(0, 0, 4, 0),
            columns = new List<ListViewColumn> { new() { Header = "Toolbox", Width = 146 } }
        };
        foreach (var kv in UiRegistry.Types)
            toolbox.AddItem(new[] { kv.Key }, null, kv.Value);

        // property grid + edit row
        var rightPane = new DockPanel(0, 0, 280, 100) { useBackground = true };
        propGrid = new ListView(0, 0, 280, 100)
        {
            columns = new List<ListViewColumn>
            {
                new() { Header = "Property", Width = 90 },
                new() { Header = "Value",    Width = 186 },
            }
        };
        propGrid.selectedChanged = item =>
        {
            editProp = item?.Tag as PropertyDescriptor;
            editBox.text = item == null ? "" : item.Text[1];
            editBox.MarkDirty();
        };

        var editRow = new DockPanel(0, 0, 280, 26);
        editBox = new TextField(0, 0, 200, 24);                   // ASSUMPTION: (x, y, w, h)
        var applyButton = new Button("Apply", 0, 0, 70, 24);
        applyButton.leftClickAction = ApplyEdit;
        editRow.AddDockChild(applyButton, Dock.Right);
        editRow.AddDockChild(editBox, Dock.Fill);

        rightPane.AddDockChild(editRow, Dock.Bottom);
        rightPane.AddDockChild(propGrid, Dock.Fill);

        // bottom rows
        var fileRow = new DockPanel(0, 0, Width, 26);
        var fileLabel = new Label(0, 0, 40, 20) { text = "File:", useBackground = false };
        Pin(fileLabel);
        pathBox = new TextField(0, 0, 300, 24) { text = "/mnt/Apps/design.ui" };
        fileRow.AddDockChild(fileLabel, Dock.Left);
        fileRow.AddDockChild(pathBox, Dock.Fill);

        statusLabel = new Label(0, 0, Width, 20) { text = "Ready", useBackground = false };
        Pin(statusLabel);

        // surface
        surface = new DesignerSurface(0, 0, 400, 300) { Margin = new Thickness(0, 0, 4, 0) };
        surface.selectionChanged = OnSelectionChanged;
        surface.status = SetStatus;
        surface.SetContent(MakeBlankForm());

        toolbox.selectedChanged = item =>
        {
            if (item?.Tag is ComponentDescriptor d) surface.AddControl(d);
        };

        // dock: edges first, Fill last
        root.AddDockChild(toolbar, Dock.Top);
        root.AddDockChild(statusLabel, Dock.Bottom);
        root.AddDockChild(fileRow, Dock.Bottom);
        root.AddDockChild(toolbox, Dock.Left);
        root.AddDockChild(rightPane, Dock.Right);
        root.AddDockChild(surface, Dock.Fill);
        root.ResolveDockLayout();

        SetStatus(UiRegistry.Types.Count + " control types");
    }

    // keep root in step with the window however it was resized
    public override void Resize(int w, int h)
    {
        base.Resize(w, h);
        SyncRoot();
    }

    public override void Update()
    {
        base.Update();
        SyncRoot();
    }

    private void SyncRoot()
    {
        int w = Width + (Width & 1);
        int h = Height - TitleBar;
        if (root.Width == w && root.Height == h && root.Y == TitleBar) return;
        root.Y = TitleBar;
        root.Resize(w, h);
    }

    private static void Pin(Component c)
    {
        c.horizontalAlignment = HorizontalAlignment.Left;
        c.verticalAlignment = VerticalAlignment.Top;
    }

    private void AddToolbarButton(DockPanel bar, string text, int width, Action click)
    {
        var b = new Button(text, 0, 0, width, 26) { Margin = new Thickness(0, 2, 4, 2) };
        b.leftClickAction = click;
        bar.AddDockChild(b, Dock.Left);
    }

    private static Component MakeBlankForm()
    {
        var form = (DockPanel)UiRegistry.Types["DockPanel"].Create();
        form.name = "form";
        form.gridDots = 8;
        form.SetBounds(8, 8, 420, 300);
        return form;
    }

    private void SetStatus(string s)
    {
        statusLabel.text = s;
        statusLabel.MarkDirty();
    }

    private void OnSelectionChanged(Component? c)
    {
        propGrid.ClearItems();
        editProp = null;
        editBox.text = "";
        editBox.MarkDirty();

        var d = c == null ? null : UiRegistry.For(c);
        if (d == null || c == null) return;

        foreach (var p in d.Properties)
            propGrid.AddItem(new[] { p.Name, p.Get(c) }, null, p);
    }

    private void ApplyEdit()
    {
        var target = surface.selected;
        if (editProp == null || target == null) return;

        surface.BeginChange();
        editProp.Set(target, editBox.text);
        surface.MarkDirty();
        surface.Select(target);                                 // rebuild the grid with fresh values
    }

    private void NewForm()
    {
        surface.SetContent(MakeBlankForm());
        surface.ClearHistory();
        SetStatus("New form");
    }

    private void SaveForm()
    {
        if (surface.content == null) return;
        try
        {
            File.WriteAllText(pathBox.text, UiFile.Save(surface.content));
            SetStatus("Saved " + pathBox.text);
        }
        catch (Exception e) { SetStatus("Save failed: " + e.Message); }
    }

    private void OpenForm()
    {
        try
        {
            Component? loaded = UiFile.Load(File.ReadAllLines(pathBox.text));
            if (loaded == null) { SetStatus("Nothing to load in " + pathBox.text); return; }

            if (loaded is DockPanel dp) dp.gridDots = 8;
            loaded.SetBounds(8, 8, loaded.Width, loaded.Height);
            surface.SetContent(loaded);
            surface.ClearHistory();
            SetStatus("Opened " + pathBox.text);
        }
        catch (Exception e) { SetStatus("Open failed: " + e.Message); }
    }

    private void Preview()
    {
        if (surface.content == null) return;
        string text = UiFile.Save(surface.content);
        var win = new PreviewWindow(text, 120, 80, surface.content.Width + 8, surface.content.Height + TitleBar + 8);
        LaunchTracker.Start(() => win);                                 // ASSUMPTION: use whatever opens DiskManagement
    }
}

public sealed class PreviewWindow : Window
{
    private const int TitleBar = 28;

    public PreviewWindow(string uiText, int x, int y, int width, int height)
        : base(x, y, width, height, "Preview", true)
    {
        Component? ui = UiFile.Load(uiText);
        if (ui == null) return;

        AddChild(ui);
        ui.SetBounds(0, TitleBar, ui.Width, ui.Height);
    }
}