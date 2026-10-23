using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using Windose;
using Windose.System.Kernel;
using Windose.System.Kernel.Attributes;
using Windose.System.Kernel.Subsystem;
using Windose.System.Media;

[FileAssociation(".avi")]
[FileAssociation(".mp4")]
[FileAssociation(".webm")]
[FileAssociation(".mkv")]
[FileAssociation(".mov")]
[FileAssociation(".wmv")]
[FileAssociation(".flv")]
[FileAssociation(".gif")]
[FileAssociation(".wav")]
[FileAssociation(".mp3")]
[FileAssociation(".ogg")]
[FileAssociation(".wma")]
public class MediaPlayer : Window
{
    private class MediaScreen : Component
    {
        private readonly MediaPlayer player;

        public MediaScreen(MediaPlayer player, int x, int y, int width, int height) : base(x, y, width, height)
        {
            this.player = player;
            horizontalAlignment = HorizontalAlignment.Stretch;
            verticalAlignment = VerticalAlignment.Stretch;
            clampSize = false;
        }

        public override void DrawLocal()
        {
            Bitmap? frame = player.GetCurrentFrame();

            if (frame != null && (player.CurrentMedia?.HasVideo ?? false))
            {
                // Cinema Black letterbox backdrop
                buffer.DrawFilledRectangle(Color.Black, 0, 0, Width, Height);

                // Compute aspect ratio scaling
                int fw = (int)frame.Width;
                int fh = (int)frame.Height;
                if (fw > 0 && fh > 0 && Width > 0 && Height > 0)
                {
                    float scaleW = (float)Width / fw;
                    float scaleH = (float)Height / fh;
                    float scale = Math.Min(scaleW, scaleH);

                    int drawW = Math.Max(1, (int)(fw * scale));
                    int drawH = Math.Max(1, (int)(fh * scale));
                    int drawX = (Width - drawW) / 2;
                    int drawY = (Height - drawH) / 2;

                    Rectangle dest = new Rectangle(drawX, drawY, drawW, drawH);
                    DrawImageStretch(frame, dest);
                }
            }
            else
            {
                // Audio or Visualizer Mode
                string title = player.CurrentMedia?.Title ?? "Windows Media Player";
                string subtitle = player.CurrentMedia?.Format ?? "Ready";
                player.Visualizer.Draw(buffer, 0, 0, Width, Height, player.IsPlaying, Environment.TickCount, title, subtitle);
            }

            // Translucent pause indicator overlay when paused or stopped
            if (!player.IsPlaying && player.CurrentMedia != null)
            {
                int cx = Width / 2;
                int cy = Height / 2;
                // Draw rounded emblem badge
                buffer.DrawFilledRectangle(Color.FromArgb(140, 0, 0, 0), cx - 28, cy - 28, 56, 56);
                buffer.DrawString("||", SystemFonts.msSansSerif, 24, Color.White, cx - 8, cy - 14);
            }
        }

        public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
        {
            if (IsInsideAbsolute(mouseX, mouseY) && mouse.left == MouseEvents.Press)
            {
                player.TogglePlayPause();
                return true;
            }
            return base.HandleInput(mouseX, mouseY, mouse);
        }

        public override string GetComponentName() => "MediaScreen";
    }

    private readonly DockPanel root;
    private readonly MenuBar menuBar;
    private readonly MediaScreen screen;
    private readonly Panel scrubberPanel;
    private readonly Label lblTimeCurrent;
    private readonly Label lblTimeTotal;
    private readonly Slider seekSlider;
    private readonly Panel controlPanel;
    private readonly Button btnPlayPause;
    private readonly Button btnStop;
    private readonly Button btnPrev;
    private readonly Button btnNext;
    private readonly Button btnRewind;
    private readonly Button btnForward;
    private readonly Button btnRepeat;
    private readonly Button btnVisualizer;
    private readonly Button btnMute;
    private readonly Slider volumeSlider;
    private readonly Button btnFullscreen;
    private readonly StatusBar statusBar;
    private Panel? statusStatePanel;
    private Panel? statusFormatPanel;

    public MediaVisualizer Visualizer { get; } = new MediaVisualizer();
    public MediaMetadata? CurrentMedia { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool IsLooping { get; private set; } = true;
    public bool IsMuted { get; private set; }

    // Video streams
    private AviDecoder? aviDecoder;
    private Gif? gifAnimation;
    private List<Bitmap>? demoFrames;

    private int currentFrameIndex;
    private int totalFrames;
    private float currentFps = 30f;
    private int lastFrameTick;
    private bool isScrubbing;

    public MediaPlayer(string? initialPath = null, int x = 140, int y = 100, int width = 720, int height = 520)
        : base(x, y, width, height, "Windows Media Player", true, new Png("/mnt/System/Icons/media_player.png"))
    {
        root = new DockPanel(0, 0, Width, Height)
        {
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(28, 6, 6, 6),
            Padding = new Thickness(0),
            useBackground = true
        };

        // --- 1. MenuBar (Top) ---
        menuBar = new MenuBar(0, 0, Width);
        MenuPage fileMenu = menuBar.AddMenuPage("File");
        fileMenu.AddItem("Open File...", OnOpenFile);
        fileMenu.AddItem("Play Demo Video", () => LoadDemoMedia());
        fileMenu.AddSeparator();
        fileMenu.AddItem("Properties", OnShowProperties);
        fileMenu.AddItem("Close", () => WindowManager.PostClose(this));

        MenuPage playMenu = menuBar.AddMenuPage("Play");
        playMenu.AddItem("Play / Pause", TogglePlayPause);
        playMenu.AddItem("Stop", Stop);
        playMenu.AddSeparator();
        playMenu.AddItem("Previous Frame / Track", StepBackward);
        playMenu.AddItem("Next Frame / Track", StepForward);
        playMenu.AddItem("Repeat / Loop", () => { IsLooping = !IsLooping; UpdateRepeatButton(); });
        playMenu.AddSeparator();
        playMenu.AddItem("Mute", ToggleMute);

        MenuPage viewMenu = menuBar.AddMenuPage("View");
        viewMenu.AddItem("Visualizer: Spectrum Bars", () => { Visualizer.Mode = VisualizerMode.Bars; screen.MarkDirty(); });
        viewMenu.AddItem("Visualizer: Oscilloscope", () => { Visualizer.Mode = VisualizerMode.Oscilloscope; screen.MarkDirty(); });
        viewMenu.AddItem("Visualizer: Ambience", () => { Visualizer.Mode = VisualizerMode.Ambience; screen.MarkDirty(); });
        viewMenu.AddItem("Visualizer: Off", () => { Visualizer.Mode = VisualizerMode.Off; screen.MarkDirty(); });
        viewMenu.AddSeparator();
        viewMenu.AddItem("Toggle Fullscreen", ToggleFullscreen);

        MenuPage helpMenu = menuBar.AddMenuPage("Help");
        helpMenu.AddItem("About Windows Media Player", OnAbout);

        root.AddDockChild(menuBar, Dock.Top);

        // --- 2. StatusBar (Bottom) ---
        statusBar = new StatusBar(0, 0, Width, 22)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0)
        };
        statusStatePanel = statusBar.AddPanel("Ready", 160);
        statusFormatPanel = statusBar.AddPanel("No media loaded", 320);
        root.AddDockChild(statusBar, Dock.Bottom);

        // --- 3. Playback Control Panel (Bottom) ---
        controlPanel = new Panel(Palette.ControlFace, 0, 0, Width, 36)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            useBackground = true,
            useBorders = true,
            Margin = new Thickness(0),
            Padding = new Thickness(2)
        };

        btnPrev = new Button("|<<", 6, 4, 32, 26) { leftClickAction = () => StepBackward() };
        btnRewind = new Button("<<", 42, 4, 32, 26) { leftClickAction = () => SeekRelative(-5000) };
        btnPlayPause = new Button(">", 78, 4, 40, 26) { leftClickAction = TogglePlayPause };
        btnStop = new Button("[]", 122, 4, 32, 26) { leftClickAction = Stop };
        btnForward = new Button(">>", 158, 4, 32, 26) { leftClickAction = () => SeekRelative(5000) };
        btnNext = new Button(">>|", 194, 4, 32, 26) { leftClickAction = () => StepForward() };

        btnRepeat = new Button("[R]", 234, 4, 34, 26) { leftClickAction = () => { IsLooping = !IsLooping; UpdateRepeatButton(); } };
        btnVisualizer = new Button("Viz", 272, 4, 34, 26) { leftClickAction = () => { Visualizer.CycleMode(); screen.MarkDirty(); } };
        btnMute = new Button("Vol", 314, 4, 36, 26) { leftClickAction = ToggleMute };

        volumeSlider = new Slider(354, 8, 80);
        volumeSlider.Minimum = 0f;
        volumeSlider.Maximum = 100f;
        volumeSlider.Value = 85f;
        volumeSlider.ValueChanged += (val) => { AudioEngine.Instance.Volume = val / 100f; };

        btnFullscreen = new Button("[ ]", 442, 4, 34, 26) { leftClickAction = ToggleFullscreen };

        controlPanel.AddChild(btnPrev);
        controlPanel.AddChild(btnRewind);
        controlPanel.AddChild(btnPlayPause);
        controlPanel.AddChild(btnStop);
        controlPanel.AddChild(btnForward);
        controlPanel.AddChild(btnNext);
        controlPanel.AddChild(btnRepeat);
        controlPanel.AddChild(btnVisualizer);
        controlPanel.AddChild(btnMute);
        controlPanel.AddChild(volumeSlider);
        controlPanel.AddChild(btnFullscreen);

        root.AddDockChild(controlPanel, Dock.Bottom);

        // --- 4. Scrubber & Timeline Panel (Bottom) ---
        scrubberPanel = new Panel(Palette.ControlFace, 0, 0, Width, 24)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            useBackground = true,
            useBorders = false,
            Margin = new Thickness(0),
            Padding = new Thickness(0)
        };

        lblTimeCurrent = new Label(6, 4, 50, 18)
        {
            text = "00:00",
            fontSize = 12,
            textColor = Palette.ControlBlack,
            useBackground = false
        };

        seekSlider = new Slider(60, 4, Width - 130);
        seekSlider.horizontalAlignment = HorizontalAlignment.Stretch;
        seekSlider.Minimum = 0f;
        seekSlider.Maximum = 1000f;
        seekSlider.Value = 0f;
        seekSlider.ValueChanged += OnSeekSliderChanged;

        lblTimeTotal = new Label(Width - 62, 4, 50, 18)
        {
            horizontalAlignment = HorizontalAlignment.Right,
            text = "00:00",
            fontSize = 12,
            textColor = Palette.ControlBlack,
            useBackground = false
        };

        scrubberPanel.AddChild(lblTimeCurrent);
        scrubberPanel.AddChild(seekSlider);
        scrubberPanel.AddChild(lblTimeTotal);

        root.AddDockChild(scrubberPanel, Dock.Bottom);

        // --- 5. Media Screen Viewport (Fill) ---
        screen = new MediaScreen(this, 0, 0, Width, Height - 120);
        root.AddDockChild(screen, Dock.Fill);

        AddChild(root);

        // Load initial media or demo
        if (!string.IsNullOrEmpty(initialPath))
        {
            Open(initialPath);
        }
        else
        {
            LoadDemoMedia();
        }
    }

    public void Open(string path)
    {
        Stop();
        CurrentMedia = MediaMetadata.FromPath(path);
        text = $"Windows Media Player - {CurrentMedia.Title}";

        string ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (ext == ".avi")
            {
                aviDecoder = new AviDecoder(path);
                gifAnimation = null;
                demoFrames = null;
                totalFrames = aviDecoder.FrameCount;
                currentFps = aviDecoder.Fps > 0 ? aviDecoder.Fps : 30f;
                CurrentMedia.Width = aviDecoder.Width;
                CurrentMedia.Height = aviDecoder.Height;
                CurrentMedia.DurationMs = aviDecoder.DurationMs;
                CurrentMedia.TotalFrames = totalFrames;
                CurrentMedia.FrameRate = currentFps;
                CurrentMedia.HasAudio = aviDecoder.HasAudio;

                byte[]? audio = aviDecoder.GetCombinedAudio();
                if (audio != null)
                {
                    AudioEngine.Instance.PlayWave(audio);
                }
            }
            else if (ext == ".gif")
            {
                gifAnimation = new Gif(path);
                aviDecoder = null;
                demoFrames = null;
                totalFrames = gifAnimation.Frames.Count;
                currentFps = 10f;
                CurrentMedia.Width = (int)gifAnimation.Width;
                CurrentMedia.Height = (int)gifAnimation.Height;
                CurrentMedia.TotalFrames = totalFrames;
                CurrentMedia.DurationMs = totalFrames * 100;
            }
            else if (ext == ".wav" || ext == ".mp3" || ext == ".ogg" || ext == ".wma")
            {
                aviDecoder = null;
                gifAnimation = null;
                demoFrames = null;
                totalFrames = 0;
                CurrentMedia.HasVideo = false;
                CurrentMedia.HasAudio = true;
                AudioEngine.Instance.PlayFile(path);
            }
            else
            {
                // Fallback / Container / MP4 / WebM
                LoadDemoMedia(CurrentMedia.Title);
            }
        }
        catch
        {
            LoadDemoMedia(CurrentMedia?.Title ?? "Windose Media");
        }

        currentFrameIndex = 0;
        lastFrameTick = Environment.TickCount;
        IsPlaying = true;
        UpdatePlayButton();
        UpdateTimeLabels();
        UpdateStatusBadge();
        screen.MarkDirty();
    }

    public void LoadDemoMedia(string? customTitle = null)
    {
        Stop();
        CurrentMedia = new MediaMetadata
        {
            Title = customTitle ?? "Windose Intro Video",
            Artist = "Windose Multimedia",
            Format = "Windose 30fps Video + Audio",
            Type = MediaType.Video,
            HasVideo = true,
            HasAudio = true,
            Width = 320,
            Height = 240,
            DurationMs = 2500,
            TotalFrames = 60,
            FrameRate = 30f
        };
        text = $"Windows Media Player - {CurrentMedia.Title}";

        demoFrames = DemoMediaGenerator.GetDemoFrames(320, 240, 60);
        aviDecoder = null;
        gifAnimation = null;
        totalFrames = demoFrames.Count;
        currentFps = 30f;
        currentFrameIndex = 0;

        byte[] audio = DemoMediaGenerator.GetDemoAudio();
        AudioEngine.Instance.PlayWave(audio);

        lastFrameTick = Environment.TickCount;
        IsPlaying = true;
        UpdatePlayButton();
        UpdateTimeLabels();
        UpdateStatusBadge();
        screen.MarkDirty();
    }

    public Bitmap? GetCurrentFrame()
    {
        if (aviDecoder != null)
        {
            return aviDecoder.GetFrame(currentFrameIndex);
        }
        if (gifAnimation != null && gifAnimation.Frames.Count > 0)
        {
            int idx = Math.Clamp(currentFrameIndex, 0, gifAnimation.Frames.Count - 1);
            return gifAnimation.Frames[idx].Image;
        }
        if (demoFrames != null && demoFrames.Count > 0)
        {
            int idx = Math.Clamp(currentFrameIndex, 0, demoFrames.Count - 1);
            return demoFrames[idx];
        }
        return null;
    }

    public override void Update()
    {
        base.Update();

        if (!IsPlaying) return;

        int now = Environment.TickCount;
        float frameIntervalMs = 1000f / (currentFps > 0 ? currentFps : 30f);

        if (gifAnimation != null && gifAnimation.Frames.Count > 0)
        {
            int idx = Math.Clamp(currentFrameIndex, 0, gifAnimation.Frames.Count - 1);
            frameIntervalMs = gifAnimation.Frames[idx].DelayMs;
        }

        if (now - lastFrameTick >= frameIntervalMs)
        {
            lastFrameTick = now;
            if (totalFrames > 0)
            {
                currentFrameIndex++;
                if (currentFrameIndex >= totalFrames)
                {
                    if (IsLooping)
                    {
                        currentFrameIndex = 0;
                        if (demoFrames != null)
                        {
                            AudioEngine.Instance.PlayWave(DemoMediaGenerator.GetDemoAudio());
                        }
                    }
                    else
                    {
                        currentFrameIndex = totalFrames - 1;
                        Stop();
                    }
                }
            }

            if (!isScrubbing)
            {
                UpdateScrubberPosition();
            }
            UpdateTimeLabels();
            screen.MarkDirty();
        }
    }

    public void TogglePlayPause()
    {
        if (IsPlaying)
        {
            IsPlaying = false;
            AudioEngine.Instance.Pause();
        }
        else
        {
            IsPlaying = true;
            lastFrameTick = Environment.TickCount;
            AudioEngine.Instance.Resume();
        }
        UpdatePlayButton();
        UpdateStatusBadge();
        screen.MarkDirty();
    }

    public new void Stop() => StopPlayback();

    public void StopPlayback()
    {
        IsPlaying = false;
        currentFrameIndex = 0;
        AudioEngine.Instance.Stop();
        UpdatePlayButton();
        UpdateScrubberPosition();
        UpdateTimeLabels();
        UpdateStatusBadge();
        screen.MarkDirty();
    }

    public void StepBackward()
    {
        if (currentFrameIndex > 0) currentFrameIndex--;
        else if (IsLooping && totalFrames > 0) currentFrameIndex = totalFrames - 1;
        UpdateScrubberPosition();
        UpdateTimeLabels();
        screen.MarkDirty();
    }

    public void StepForward()
    {
        if (totalFrames > 0 && currentFrameIndex < totalFrames - 1) currentFrameIndex++;
        else if (IsLooping) currentFrameIndex = 0;
        UpdateScrubberPosition();
        UpdateTimeLabels();
        screen.MarkDirty();
    }

    public void SeekRelative(int ms)
    {
        if (totalFrames <= 0) return;
        int deltaFrames = (int)(ms * (currentFps / 1000.0f));
        currentFrameIndex = Math.Clamp(currentFrameIndex + deltaFrames, 0, totalFrames - 1);
        UpdateScrubberPosition();
        UpdateTimeLabels();
        screen.MarkDirty();
    }

    private void OnSeekSliderChanged(float val)
    {
        if (totalFrames <= 0) return;
        float ratio = val / 1000f;
        currentFrameIndex = Math.Clamp((int)(ratio * totalFrames), 0, totalFrames - 1);
        UpdateTimeLabels();
        screen.MarkDirty();
    }

    private void UpdateScrubberPosition()
    {
        if (totalFrames <= 0)
        {
            seekSlider.Value = 0f;
            return;
        }
        float ratio = (float)currentFrameIndex / totalFrames;
        seekSlider.Value = Math.Clamp(ratio * 1000f, 0f, 1000f);
    }

    private void UpdateTimeLabels()
    {
        int curMs = (int)(currentFrameIndex * (1000f / (currentFps > 0 ? currentFps : 30f)));
        int totalMs = CurrentMedia?.DurationMs ?? (int)(totalFrames * (1000f / (currentFps > 0 ? currentFps : 30f)));
        lblTimeCurrent.text = MediaMetadata.FormatTime(curMs);
        lblTimeTotal.text = MediaMetadata.FormatTime(totalMs);
    }

    private void UpdatePlayButton()
    {
        btnPlayPause.text = IsPlaying ? "||" : ">";
    }

    private void UpdateRepeatButton()
    {
        btnRepeat.text = IsLooping ? "[R]" : " R ";
    }

    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        AudioEngine.Instance.Volume = IsMuted ? 0f : (volumeSlider.Value / 100f);
        btnMute.text = IsMuted ? "Mut" : "Vol";
    }

    private void ToggleFullscreen()
    {
        WindowManager.ToggleMaximize(this);
        screen.MarkDirty();
    }

    private void UpdateStatusBadge()
    {
        if (statusStatePanel != null)
        {
            statusStatePanel.text = IsPlaying ? $"Playing ({currentFps:F0} fps)" : "Paused";
        }
        if (statusFormatPanel != null && CurrentMedia != null)
        {
            if (CurrentMedia.HasVideo)
            {
                statusFormatPanel.text = $"{CurrentMedia.Format} - {CurrentMedia.Width}x{CurrentMedia.Height} ({totalFrames} frames)";
            }
            else
            {
                statusFormatPanel.text = $"{CurrentMedia.Format} - Audio Stream";
            }
        }
    }

    private void OnOpenFile()
    {
        var options = new FileDialogOptions
        {
            Title = "Open Media File",
            Mode = FileDialogMode.Open,
            InitialPath = "/mnt"
        };
        LaunchTracker.Start(() => new FileDialog(options, (path) =>
        {
            if (!string.IsNullOrEmpty(path))
            {
                Open(path);
            }
        }));
    }

    private void OnShowProperties()
    {
        if (CurrentMedia == null) return;
        string msg = $"Title: {CurrentMedia.Title}\n" +
                     $"Format: {CurrentMedia.Format}\n" +
                     $"Resolution: {CurrentMedia.Width}x{CurrentMedia.Height}\n" +
                     $"Duration: {CurrentMedia.DurationFormatted}\n" +
                     $"Framerate: {CurrentMedia.FrameRate:F1} fps\n" +
                     $"Total Frames: {CurrentMedia.TotalFrames}";
        LaunchTracker.Start(() => MessageBox.Show(msg, "Media Properties"));
    }

    private void OnAbout()
    {
        string msg = "Windows Media Player for Windose\n" +
                     "Version 11.0 Cinema Edition\n" +
                     "Supports AVI (MJPEG/DIB), Animated GIF, WAV, MP3, and YouTube Streams.";
        LaunchTracker.Start(() => MessageBox.Show(msg, "About Windows Media Player"));
    }

    public override void Dispose()
    {
        StopPlayback();
        base.Dispose();
    }
}
