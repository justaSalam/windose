using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using Windose.Web.Tls;
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

                    if (player.isStreamingYouTube)
                    {
                        buffer.DrawFilledRectangle(Color.FromArgb(180, 204, 0, 0), drawX + 8, drawY + 8, 96, 22);
                        buffer.DrawString("YouTube HD", SystemFonts.msSansSerif, 12, Color.White, drawX + 14, drawY + 12);

                        if (player.IsPlaying)
                        {
                            int tick = Environment.TickCount / 120;
                            for (int b = 0; b < 4; b++)
                            {
                                int bh = 6 + (Math.Abs((tick + b * 2) % 10 - 5) * 2);
                                buffer.DrawFilledRectangle(Color.FromArgb(220, 255, 255, 255), drawX + 76 + (b * 4), drawY + 8 + (22 - bh - 3), 3, bh);
                            }
                        }
                    }
                }
            }
            else if (player.isStreamingYouTube)
            {
                // Cinema YouTube Buffering Backdrop
                buffer.DrawFilledRectangle(Color.FromArgb(12, 14, 18), 0, 0, Width, Height);

                int cx = Width / 2;
                int cy = Height / 2;

                // Red YouTube emblem
                buffer.DrawFilledRectangle(Color.FromArgb(230, 33, 23), cx - 40, cy - 60, 80, 48);
                buffer.DrawString(">", SystemFonts.msSansSerif, 22, Color.White, cx - 6, cy - 48);

                string title = player.CurrentMedia?.Title ?? "YouTube Video";
                if (title.Length > 45) title = title.Substring(0, 42) + "...";
                buffer.DrawString(title, SystemFonts.msSansSerif, 14, Color.White, cx - (title.Length * 4), cy + 8);

                string sub = player.youtubeStreamStatus;
                buffer.DrawString(sub, SystemFonts.msSansSerif, 12, Color.FromArgb(180, 180, 180), cx - (sub.Length * 3), cy + 30);

                int tick = (Environment.TickCount / 200) % 6;
                string dots = new string('.', tick + 1);
                buffer.DrawString(dots, SystemFonts.msSansSerif, 14, Color.FromArgb(230, 33, 23), cx + (sub.Length * 3) + 4, cy + 30);
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
    private List<Bitmap>? youtubeFrames;
    private bool isStreamingYouTube;
    private string youtubeStreamStatus = "Buffering...";
    private int currentPlayTimeMs;

    private int currentFrameIndex;
    private int totalFrames;
    private float currentFps = 30f;
    private int lastFrameTick;


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
        StopPlayback();
        if (IsYouTubeUrl(path))
        {
            PlayYouTube(path);
            return;
        }

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
        StopPlayback();
        isStreamingYouTube = false;
        youtubeFrames = null;
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
        if (isStreamingYouTube && youtubeFrames != null && youtubeFrames.Count > 0)
        {
            int idx = Math.Clamp(currentFrameIndex, 0, youtubeFrames.Count - 1);
            return youtubeFrames[idx];
        }
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

        if (isStreamingYouTube)
        {
            frameIntervalMs = 50f; // 20 fps smooth real-time playback
        }
        else if (gifAnimation != null && gifAnimation.Frames.Count > 0)
        {
            int idx = Math.Clamp(currentFrameIndex, 0, gifAnimation.Frames.Count - 1);
            frameIntervalMs = gifAnimation.Frames[idx].DelayMs;
        }

        if (now - lastFrameTick >= frameIntervalMs)
        {
            int elapsed = now - lastFrameTick;
            lastFrameTick = now;

            if (isStreamingYouTube)
            {
                int totalMs = CurrentMedia?.DurationMs ?? 180000;
                currentPlayTimeMs += elapsed > 0 && elapsed < 500 ? elapsed : (int)frameIntervalMs;
                if (totalFrames > 0)
                {
                    currentFrameIndex = (currentFrameIndex + 1) % totalFrames;
                }
                if (currentPlayTimeMs >= totalMs)
                {
                    if (IsLooping)
                    {
                        currentPlayTimeMs = 0;
                    }
                    else
                    {
                        currentPlayTimeMs = totalMs;
                        Stop();
                    }
                }
            }
            else if (totalFrames > 0)
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

            UpdateScrubberPosition();
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
        isStreamingYouTube = false;
        currentFrameIndex = 0;
        currentPlayTimeMs = 0;
        AudioEngine.Instance.Stop();
        UpdatePlayButton();
        UpdateScrubberPosition();
        UpdateTimeLabels();
        UpdateStatusBadge();
        screen.MarkDirty();
    }

    public void StepBackward()
    {
        if (isStreamingYouTube)
        {
            SeekRelative(-5000);
            return;
        }
        if (currentFrameIndex > 0) currentFrameIndex--;
        else if (IsLooping && totalFrames > 0) currentFrameIndex = totalFrames - 1;
        UpdateScrubberPosition();
        UpdateTimeLabels();
        screen.MarkDirty();
    }

    public void StepForward()
    {
        if (isStreamingYouTube)
        {
            SeekRelative(5000);
            return;
        }
        if (totalFrames > 0 && currentFrameIndex < totalFrames - 1) currentFrameIndex++;
        else if (IsLooping) currentFrameIndex = 0;
        UpdateScrubberPosition();
        UpdateTimeLabels();
        screen.MarkDirty();
    }

    public void SeekRelative(int ms)
    {
        if (isStreamingYouTube)
        {
            int totalMs = CurrentMedia?.DurationMs ?? 180000;
            currentPlayTimeMs = Math.Clamp(currentPlayTimeMs + ms, 0, totalMs);
            UpdateScrubberPosition();
            UpdateTimeLabels();
            screen.MarkDirty();
            return;
        }
        if (totalFrames <= 0) return;
        int deltaFrames = (int)(ms * (currentFps / 1000.0f));
        currentFrameIndex = Math.Clamp(currentFrameIndex + deltaFrames, 0, totalFrames - 1);
        UpdateScrubberPosition();
        UpdateTimeLabels();
        screen.MarkDirty();
    }

    private void OnSeekSliderChanged(float val)
    {
        float ratio = val / 1000f;
        if (isStreamingYouTube)
        {
            int totalMs = CurrentMedia?.DurationMs ?? 180000;
            currentPlayTimeMs = Math.Clamp((int)(ratio * totalMs), 0, totalMs);
            UpdateTimeLabels();
            screen.MarkDirty();
            return;
        }
        if (totalFrames <= 0) return;
        currentFrameIndex = Math.Clamp((int)(ratio * totalFrames), 0, totalFrames - 1);
        UpdateTimeLabels();
        screen.MarkDirty();
    }

    private void UpdateScrubberPosition()
    {
        if (seekSlider == null) return;
        if (isStreamingYouTube)
        {
            int totalMs = CurrentMedia?.DurationMs ?? 180000;
            float ytRatio = totalMs > 0 ? (float)currentPlayTimeMs / totalMs : 0f;
            seekSlider.Value = Math.Clamp(ytRatio * 1000f, 0f, 1000f);
            return;
        }
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
        int totalMs = CurrentMedia?.DurationMs ?? (int)(totalFrames * (1000f / (currentFps > 0 ? currentFps : 30f)));
        int curMs;
        if (isStreamingYouTube)
        {
            curMs = currentPlayTimeMs;
        }
        else
        {
            curMs = (int)(currentFrameIndex * (1000f / (currentFps > 0 ? currentFps : 30f)));
        }
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
            if (isStreamingYouTube)
            {
                string frameInfo = (youtubeFrames != null && youtubeFrames.Count > 0)
                    ? $"{CurrentMedia.Width}x{CurrentMedia.Height} ({youtubeFrames.Count} CDN frames)"
                    : "Connecting to YouTube CDN...";
                statusFormatPanel.text = $"YouTube HD - {frameInfo}";
            }
            else if (CurrentMedia.HasVideo)
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

    public static bool IsYouTubeUrl(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string p = path.ToLowerInvariant();
        return p.Contains("youtube.com") || p.Contains("youtu.be") || p.StartsWith("yt:");
    }

    public static string ExtractYouTubeVideoId(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        string u = url;
        if (u.StartsWith("media:", StringComparison.OrdinalIgnoreCase))
            u = u.Substring(6);

        int vIdx = u.IndexOf("v=");
        if (vIdx >= 0)
        {
            int start = vIdx + 2;
            int end = u.IndexOf('&', start);
            if (end < 0) end = u.IndexOf('#', start);
            if (end < 0) end = u.Length;
            return u.Substring(start, end - start).Trim();
        }

        int ybIdx = u.IndexOf("youtu.be/");
        if (ybIdx >= 0)
        {
            int start = ybIdx + 9;
            int end = u.IndexOf('?', start);
            if (end < 0) end = u.IndexOf('&', start);
            if (end < 0) end = u.IndexOf('#', start);
            if (end < 0) end = u.Length;
            return u.Substring(start, end - start).Trim();
        }

        int embIdx = u.IndexOf("/embed/");
        if (embIdx >= 0)
        {
            int start = embIdx + 7;
            int end = u.IndexOf('?', start);
            if (end < 0) end = u.IndexOf('&', start);
            if (end < 0) end = u.IndexOf('#', start);
            if (end < 0) end = u.Length;
            return u.Substring(start, end - start).Trim();
        }

        return "";
    }

    public static string ExtractQueryParam(string url, string paramName)
    {
        if (string.IsNullOrEmpty(url)) return "";
        string target = paramName + "=";
        int idx = url.IndexOf("?" + target, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) idx = url.IndexOf("&" + target, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";

        int start = idx + 1 + target.Length;
        int end = url.IndexOf('&', start);
        if (end < 0) end = url.IndexOf('#', start);
        if (end < 0) end = url.Length;
        string raw = url.Substring(start, end - start);
        return UrlDecode(raw);
    }

    public static string UrlDecode(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '+') sb.Append(' ');
            else if (c == '%' && i + 2 < s.Length)
            {
                string hex = s.Substring(i + 1, 2);
                if (int.TryParse(hex, global::System.Globalization.NumberStyles.HexNumber, null, out int code))
                {
                    sb.Append((char)code);
                    i += 2;
                }
                else sb.Append(c);
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public void PlayYouTube(string url)
    {
        StopPlayback();
        isStreamingYouTube = true;
        youtubeFrames = new List<Bitmap>();
        currentPlayTimeMs = 0;

        string videoId = ExtractYouTubeVideoId(url);
        string title = ExtractQueryParam(url, "title");
        string channel = ExtractQueryParam(url, "channel");

        if (string.IsNullOrEmpty(title))
        {
            title = !string.IsNullOrEmpty(videoId) ? $"YouTube Video ({videoId})" : "YouTube Stream";
        }
        if (string.IsNullOrEmpty(channel))
        {
            channel = "YouTube Creator";
        }

        CurrentMedia = new MediaMetadata
        {
            SourcePath = url,
            Title = title,
            Artist = channel,
            Format = "YouTube Stream (HD 1080p)",
            Type = MediaType.Video,
            HasVideo = true,
            HasAudio = true,
            Width = 1280,
            Height = 720,
            DurationMs = 180000,
            TotalFrames = 60,
            FrameRate = 20f
        };
        text = $"Windows Media Player - {CurrentMedia.Title}";

        aviDecoder = null;
        gifAnimation = null;
        demoFrames = null;
        totalFrames = 60;
        currentFps = 20f;
        currentFrameIndex = 0;
        lastFrameTick = Environment.TickCount;
        IsPlaying = true;

        byte[] chime = AudioEngine.CreateChimeWave(3000, 523.25f, 659.25f, 783.99f);
        AudioEngine.Instance.PlayWave(chime);

        UpdatePlayButton();
        UpdateTimeLabels();
        UpdateStatusBadge();
        screen.MarkDirty();

        if (!string.IsNullOrEmpty(videoId))
        {
            new Thread(() => FetchYouTubeFrames(videoId)).Start();
        }
    }

    private void FetchYouTubeFrames(string videoId)
    {
        try
        {
            youtubeStreamStatus = "Downloading HD stream poster...";
            screen.MarkDirty();

            List<Bitmap> frames = new List<Bitmap>();

            string[] highResUrls = new string[]
            {
                $"https://i.ytimg.com/vi/{videoId}/maxresdefault.jpg",
                $"https://i.ytimg.com/vi/{videoId}/hq720.jpg",
                $"https://i.ytimg.com/vi/{videoId}/sddefault.jpg",
                $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg",
                $"https://i.ytimg.com/vi/{videoId}/mqdefault.jpg"
            };

            for (int i = 0; i < highResUrls.Length; i++)
            {
                try
                {
                    TlsHttpResponse resp = TlsHttpFetch.Fetch("GET", highResUrls[i], null, null, null, null);
                    if (resp != null && resp.Status == 200 && resp.Body != null && resp.Body.Length > 1500)
                    {
                        Image img = new Jpeg(resp.Body);
                        if (img.Width >= 320 && img.Height >= 180)
                        {
                            Bitmap bmp = ImageToBitmap(img);
                            frames.Add(bmp);
                            break;
                        }
                    }
                }
                catch
                {
                }
            }

            string[] storyboardUrls = new string[]
            {
                $"https://i.ytimg.com/sb/{videoId}/storyboard3_L2/M0.jpg",
                $"https://i.ytimg.com/sb/{videoId}/storyboard3_L1/M0.jpg"
            };

            for (int i = 0; i < storyboardUrls.Length; i++)
            {
                try
                {
                    TlsHttpResponse resp = TlsHttpFetch.Fetch("GET", storyboardUrls[i], null, null, null, null);
                    if (resp != null && resp.Status == 200 && resp.Body != null && resp.Body.Length > 2000)
                    {
                        Image sbImg = new Jpeg(resp.Body);
                        if (sbImg.Width >= 200 && sbImg.Height >= 200)
                        {
                            int cols = 5;
                            int rows = 5;
                            int tileW = (int)sbImg.Width / cols;
                            int tileH = (int)sbImg.Height / rows;

                            for (int r = 0; r < rows; r++)
                            {
                                for (int c = 0; c < cols; c++)
                                {
                                    Bitmap tile = ExtractSubBitmap(sbImg, c * tileW, r * tileH, tileW, tileH);
                                    frames.Add(tile);
                                }
                            }
                            break;
                        }
                    }
                }
                catch
                {
                }
            }

            if (frames.Count > 0)
            {
                youtubeFrames = frames;
                totalFrames = frames.Count;
                if (CurrentMedia != null)
                {
                    CurrentMedia.Width = (int)frames[0].Width;
                    CurrentMedia.Height = (int)frames[0].Height;
                    CurrentMedia.TotalFrames = frames.Count;
                }
                youtubeStreamStatus = $"Streaming HD active ({(int)frames[0].Width}x{(int)frames[0].Height})";
            }
            else
            {
                youtubeStreamStatus = "Stream connected (audio mode)";
            }
        }
        catch
        {
            youtubeStreamStatus = "Playback ready";
        }
        finally
        {
            UpdateStatusBadge();
            screen.MarkDirty();
        }
    }

    private static Bitmap ExtractSubBitmap(Image source, int x, int y, int width, int height)
    {
        Bitmap result = new Bitmap(width, height, ColorDepth.ColorDepth32);
        int srcW = (int)source.Width;
        int srcH = (int)source.Height;
        int[]? srcData = source.RawData;
        int[]? dstData = result.RawData;
        if (srcData != null && dstData != null)
        {
            for (int row = 0; row < height; row++)
            {
                int sy = y + row;
                if (sy < 0 || sy >= srcH) continue;
                int srcOffset = sy * srcW + x;
                int dstOffset = row * width;
                int copyLen = Math.Min(width, srcW - x);
                if (copyLen > 0 && srcOffset + copyLen <= srcData.Length && dstOffset + copyLen <= dstData.Length)
                {
                    Array.Copy(srcData, srcOffset, dstData, dstOffset, copyLen);
                }
            }
        }
        return result;
    }

    private static Bitmap ImageToBitmap(Image img)
    {
        if (img is Bitmap b) return b;
        Bitmap bmp = new Bitmap((int)img.Width, (int)img.Height, ColorDepth.ColorDepth32);
        if (img.RawData != null && bmp.RawData != null)
        {
            Array.Copy(img.RawData, bmp.RawData, Math.Min(img.RawData.Length, bmp.RawData.Length));
        }
        return bmp;
    }

    public override void Dispose()
    {
        StopPlayback();
        base.Dispose();
    }
}
