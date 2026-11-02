using System;
using System.Drawing;
using Windose.System.Media;

public class VolumeFlyout : Window
{
    private static VolumeFlyout? instance;
    private readonly Slider volSlider;
    private readonly Label volumePercentLabel;
    private readonly Checkbox muteCheck;

    private VolumeFlyout() : base(Global.screenWidth - 190, 0, 180, 210, "Volume", false)
    {
        canResize = false;
        canMaximize = false;
        canMove = false;
        showInTaskbar = false;
        zLayer = DrawLayer.Popup;

        StackPanel panel = new StackPanel(Palette.ControlFace, 0, 0, Width, Height)
        {
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            orientation = StackOrientation.Vertical,
            Padding = new Thickness(10),
            spacing = 8,
            clampSize = false
        };
        AddChild(panel);

        panel.AddStackChild(new Label(0, 0, 160, 20)
        {
            text = "Master Volume",
            fontSize = 14,
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Center
        });

        volumePercentLabel = new Label(0, 0, 160, 22)
        {
            text = $"{(int)(AudioEngine.Instance.Volume * 100)}%",
            fontSize = 16,
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Center
        };
        panel.AddStackChild(volumePercentLabel);

        volSlider = new Slider(0, 0, 140, 24, Orientation.Horizontal)
        {
            Minimum = 0,
            Maximum = 100,
            Value = AudioEngine.Instance.Volume * 100f,
            horizontalAlignment = HorizontalAlignment.Center
        };
        volSlider.ValueChanged += val =>
        {
            AudioEngine.Instance.Volume = val / 100f;
            volumePercentLabel.text = $"{(int)val}%";
            volumePercentLabel.MarkDirty();
        };
        panel.AddStackChild(volSlider);

        muteCheck = new Checkbox(0, 0)
        {
            text = " Mute Audio",
            Checked = AudioEngine.Instance.IsMuted,
            horizontalAlignment = HorizontalAlignment.Center
        };
        muteCheck.CheckedChanged += isMuted =>
        {
            AudioEngine.Instance.IsMuted = isMuted;
        };
        panel.AddStackChild(muteCheck);

        Button testButton = new Button("Test Sound", 0, 0, 120, 26)
        {
            horizontalAlignment = HorizontalAlignment.Center,
            leftClickAction = () => AudioEngine.Instance.PlayAlertSound()
        };
        panel.AddStackChild(testButton);
    }

    public static void Toggle()
    {
        if (instance == null)
        {
            instance = new VolumeFlyout();
            WindowManager.Register(instance);
        }

        int h = 210;
        int taskbarHeight = Explorer.taskbar != null ? Explorer.taskbar.Height : 30;
        int y = Math.Max(0, Global.screenHeight - taskbarHeight - h - 4);
        int x = Math.Max(0, Global.screenWidth - 200);
        instance.ApplyBounds(new Rectangle(x, y, 180, h));
        instance.Visible = !instance.Visible;
        if (instance.Visible)
        {
            instance.volSlider.Value = AudioEngine.Instance.Volume * 100f;
            instance.volumePercentLabel.text = $"{(int)(AudioEngine.Instance.Volume * 100)}%";
            instance.muteCheck.Checked = AudioEngine.Instance.IsMuted;
            instance.MarkDirty();
        }
    }

    public override void OnLoseFocus()
    {
        Visible = false;
    }

    public override string GetComponentName() => "VolumeFlyout";
}
