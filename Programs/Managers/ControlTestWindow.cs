using System.Drawing;
using Cosmos.Kernel.System.Graphics;

public sealed class ControlTestWindow : Window
{
    private readonly Label status;
    private readonly ProgressBar progress;
    private readonly Slider slider;

    public ControlTestWindow(int x = 100, int y = 70, int width = 760, int height = 560)
        : base(x, y, width, height, "Control Test", true, new Png("/mnt/System/Icons/appwizard.png"))
    {
        DockPanel root = new DockPanel(0, 0, Width, Height)
        {
            clampSize = false,
            Margin = new Thickness(28, 2, 2, 2),
            Padding = new Thickness(6),
            useBackground = true,
            backgroundColor = Palette.ControlFace,
        };
        AddChild(root);

        status = new Label(0, 0, Width, 24)
        {
            clampSize = false,
            useBackground = false,
            text = "Ready — click controls to test input and layout.",
            Margin = new Thickness(2),
        };
        root.AddDockChild(status, Dock.Bottom);

        TabControl tabs = new TabControl(0, 0, Width, Height - 60)
        {
            clampSize = false,
            Margin = new Thickness(0),
        };
        root.AddDockChild(tabs, Dock.Fill);

        TabPage basics = tabs.AddPage("Basics");
        StackPanel basicStack = CreatePageStack(basics);
        basicStack.AddStackChild(new Label(0, 0, 320, 24)
        {
            text = "Buttons, labels, text input, and selection",
            useBackground = false,
            clampSize = false,
        });

        StackPanel buttonRow = CreateRow(basicStack);
        Button iconButton = new Button(new Png("/mnt/System/Icons/settings_gear.png"), "Icon + Label", 0, 0, 150, 32)
        {
            Padding = new Thickness(6),
            Margin = new Thickness(2),
            leftClickAction = () => SetStatus("Icon and label button clicked"),
        };
        buttonRow.AddStackChild(iconButton);
        buttonRow.AddStackChild(new Button("Text Button", 0, 0, 120, 32)
        {
            Margin = new Thickness(2),
            leftClickAction = () => SetStatus("Text button clicked"),
        });

        basicStack.AddStackChild(new Label(0, 0, 280, 22)
        {
            text = "Name:",
            useBackground = false,
            clampSize = false,
        });
        TextField textField = new TextField(0, 0, 240, 28)
        {
            text = "Type here",
            Margin = new Thickness(2),
        };
        basicStack.AddStackChild(textField);
        basicStack.AddStackChild(new Checkbox(0, 0)
        {
            text = "Enable option",
            Width = 150,
            Height = 24,
            Margin = new Thickness(2),
        });

        RadioButton radioOne = new RadioButton(0, 0)
        {
            text = "Choice A",
            Group = "control-test",
            Width = 100,
            Height = 24,
            Margin = new Thickness(2),
        };
        RadioButton radioTwo = new RadioButton(0, 0)
        {
            text = "Choice B",
            Group = "control-test",
            Width = 100,
            Height = 24,
            Margin = new Thickness(2),
        };
        StackPanel radioRow = CreateRow(basicStack);
        radioRow.AddStackChild(radioOne);
        radioRow.AddStackChild(radioTwo);

        ComboBox combo = new ComboBox(0, 0, 200) { Margin = new Thickness(2) };
        combo.AddRange(new object[] { "Default", "Compact", "Comfortable" });
        basicStack.AddStackChild(combo);

        TabPage layout = tabs.AddPage("Layout & Progress");
        StackPanel layoutStack = CreatePageStack(layout);
        layoutStack.AddStackChild(new Label(0, 0, 360, 24)
        {
            text = "Resize the window to verify the controls reflow.",
            useBackground = false,
            clampSize = false,
        });

        progress = new ProgressBar(0, 0, 420, 28)
        {
            showText = true,
            text = "Progress",
            Margin = new Thickness(2),
        };
        progress.Value = 35;
        layoutStack.AddStackChild(progress);

        slider = new Slider(0, 0, 420, 32, Orientation.Horizontal)
        {
            ShowValue = true,
            Margin = new Thickness(2),
        };
        slider.ValueChanged += value =>
        {
            progress.Value = value;
            SetStatus($"Slider value: {(int)value}");
        };
        layoutStack.AddStackChild(slider);

        StackPanel progressButtons = CreateRow(layoutStack);
        progressButtons.AddStackChild(new Button("Increase", 0, 0, 100, 28)
        {
            Margin = new Thickness(2),
            leftClickAction = () => progress.Value = Math.Min(100, progress.Value + 10),
        });
        progressButtons.AddStackChild(new Button("Reset", 0, 0, 80, 28)
        {
            Margin = new Thickness(2),
            leftClickAction = () =>
            {
                progress.Value = 0;
                slider.Value = 0;
                SetStatus("Progress reset");
            },
        });

        TabPage collections = tabs.AddPage("Lists & Tree");
        StackPanel collectionRow = new StackPanel(Palette.ControlFace, 4, 4, Width - 24, Height - 100)
        {
            clampSize = false,
            orientation = StackOrientation.Horizontal,
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(4),
            spacing = 6,
        };
        collections.AddChild(collectionRow);

        ListView list = new ListView(0, 0, 280, 300)
        {
            clampSize = false,
            Margin = new Thickness(2),
            headerHeight = 24,
            detailsRowHeight = 24,
        };
        list.AddColumn("Control", 130);
        list.AddColumn("Status", 110);
        list.AddItem(new[] { "Button", "Ready" });
        list.AddItem(new[] { "TextField", "Ready" });
        list.AddItem(new[] { "Slider", "Ready" });
        list.selectedChanged += item => SetStatus(item == null ? "No control selected" : $"Selected: {item.Text[0]}");
        collectionRow.AddStackChild(list);

        TreeView tree = new TreeView(0, 0, 240, 300)
        {
            clampSize = false,
            Margin = new Thickness(2),
        };
        TreeViewItem controls = tree.AddRoot("Controls");
        controls.AddChild("Buttons");
        controls.AddChild("Text inputs");
        controls.AddChild("Selection controls");
        controls.AddChild("Layout containers");
        collectionRow.AddStackChild(tree);
    }

    private static StackPanel CreatePageStack(Component parent)
    {
        StackPanel stack = new StackPanel(Palette.ControlFace, 8, 8, parent.Width - 16, parent.Height - 44)
        {
            clampSize = false,
            orientation = StackOrientation.Vertical,
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0),
            Padding = new Thickness(4),
            spacing = 5,
        };
        parent.AddChild(stack);
        return stack;
    }

    private static StackPanel CreateRow(Component parent)
    {
        StackPanel row = new StackPanel(Palette.ControlFace, 0, 0, parent.Width - 16, 40)
        {
            clampSize = false,
            orientation = StackOrientation.Horizontal,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            spacing = 5,
        };
        parent.AddChild(row);
        return row;
    }

    private void SetStatus(string message)
    {
        status.text = message;
        status.MarkDirty();
    }
}
