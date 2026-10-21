using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using System.ComponentModel;
using System.Drawing;
using Windose;
using Windose.Drivers;
using Windose.System.System_Calls;

public partial class WindowManager : SingleThreadedProcess
{
    public static List<Window> windows = new List<Window>();

    private static Dictionary<Window, Button> taskbarButtons = new Dictionary<Window, Button>();

    private static List<Rectangle> dirtyRects = new List<Rectangle>();
    private static readonly object dirtyRectsLock = new object();
    private static List<UiMessage> messageQueue = new List<UiMessage>();

    private static readonly object messageQueueLock = new object();

    private static readonly HashSet<Window> failedWindows = new HashSet<Window>();
    private static bool fullComposePending = true;

    private readonly List<ApplicationFailure> pendingFailures = new List<ApplicationFailure>();

    private static bool hasPreviewRect;

    private static Rectangle previewRect;
    private static bool drawingDebugEnabled;
    private static readonly List<Rectangle> drawingDebugRects = new List<Rectangle>();

    public static bool DrawingDebugEnabled => drawingDebugEnabled;

    private static Window? capturedWindow;
    private static Component? capturedComponent;
    public static Window? focusedWindow;
    public static Window? lastFocusedWindow;

    private MouseState mouseState;

    private static int nextZIndex = 1;
    private static bool zOrderDirty = true;
    private static bool frameComposed;
    private static Rectangle lastOpenBounds = new Rectangle(-1, -1, 0, 0);
    private static int lastInputTick = Environment.TickCount;
    private static int lastIdleSettingTick;
    private static int idleLockMinutes;

    internal static void MarkZOrderDirty() => zOrderDirty = true;

    /// <summary>True if the last compose pass drew anything. Resets on read.</summary>
    internal static bool TakeFrameComposed()
    {
        bool composed = frameComposed;
        frameComposed = false;
        return composed;
    }

    private static readonly Comparison<Window> zIndexCompare = (a, b) =>
    {
        int layer = a.zLayer.CompareTo(b.zLayer);
        return layer != 0 ? layer : a.zIndex.CompareTo(b.zIndex);
    };
    private int mx, my;

    public WindowManager() : base("Desktop Window Manager", ProcessType.System)
    {
        components = Component.components;
        canTerminate = false;
        canOverridePriority = false;
    }

    private List<Component> components;

    public override void Update()
    {
        try
        {
            UpdateDesktop();
        }
        catch (Exception exception)
        {
            Window likelyOwner = focusedWindow;
            capturedWindow = null;
            capturedComponent = null;
            FailApplication(likelyOwner, "running", exception);
        }
    }


    private static void SetFocusedWindow(Window window)
    {
        if (focusedWindow == window)
        {
            return;
        }

        if (focusedWindow != null)
        {
            lastFocusedWindow = focusedWindow;
            focusedWindow.SetFocused(false);
        }

        focusedWindow = window;
        focusedWindow.SetFocused(true);
        RefreshTaskbarButtons();
    }

    private static void RefreshTaskbarButtons()
    {
        foreach (KeyValuePair<Window, Button> entry in taskbarButtons)
        {
            bool onDesktop = entry.Key.desktop < 0 || entry.Key.desktop == CurrentDesktop;
            entry.Value.Visible = onDesktop;
            entry.Value.SetSelected(onDesktop && entry.Key == focusedWindow && entry.Key.Visible && !entry.Key.isMinimized);
        }
    }

    internal static void FocusWindowImmediate(Window window)
    {
        if (window == null) return;
        SetFocusedWindow(window);
    }

    internal static Window[] WindowsSnapshot()
    {
        lock (windows) return windows.ToArray();
    }

    /// <summary>Minutes of no input before auto-lock. 0 disables. From System/Security/IdleLockMinutes.</summary>
    private static void RefreshIdleSetting()
    {
        int now = Environment.TickCount;
        if (unchecked(now - lastIdleSettingTick) < 5000 && lastIdleSettingTick != 0) return;
        lastIdleSettingTick = now;
        try { idleLockMinutes = Math.Max(0, (int)Registry.GetInteger("System/Security/IdleLockMinutes", 15)); }
        catch { idleLockMinutes = 0; }
    }

    internal static void NoteInput()
    {
        lastInputTick = Environment.TickCount;
    }

    public const int DesktopCount = 4;
    public static int CurrentDesktop { get; private set; }

    public static bool IsOnCurrentDesktop(Window window)
    {
        return window == null || window.desktop < 0 || window.desktop == CurrentDesktop;
    }

    public static void SwitchDesktop(int index)
    {
        if (index < 0 || index >= DesktopCount || index == CurrentDesktop) return;
        lock (Component.components)
        {
            CurrentDesktop = index;
            capturedWindow = null;
            capturedComponent = null;
            fullComposePending = true;
            Invalidate(new Rectangle(0, 0, Global.screenWidth, Global.screenHeight));
            FocusTopVisibleWindow(null);
            RefreshTaskbarButtons();
        }
    }

    public static void NextDesktop() => SwitchDesktop((CurrentDesktop + 1) % DesktopCount);
    public static void PrevDesktop() => SwitchDesktop((CurrentDesktop + DesktopCount - 1) % DesktopCount);

    public static void ClearFocusedWindow()
    {
        if (focusedWindow == null) return;

        focusedWindow.SetFocused(false);
        focusedWindow = null;
        RefreshTaskbarButtons();
    }

    public static void SwapFocusedWindow()
    {
        if (lastFocusedWindow != null && !IsOnCurrentDesktop(lastFocusedWindow))
        {
            lastFocusedWindow = null;
        }
        if (focusedWindow == lastFocusedWindow || lastFocusedWindow == null)
        {
            return;
        }
        if (focusedWindow == null)
        {
            return;
        }

        focusedWindow.SetFocused(false);
        SetFocusedWindow(lastFocusedWindow);
        BringToFront(focusedWindow);


    }

    private void ComposeDirtyRegions()
    {
        lock (Component.components)
        {
            long startedAt = PerformanceMetrics.Now;
            Rectangle region;
            lock (dirtyRectsLock)
            {
                if (fullComposePending)
                {
                    region = new Rectangle(0, 0, Global.screenWidth, Global.screenHeight);
                    fullComposePending = false;
                }
                else if (dirtyRects.Count == 0)
                {
                    PerformanceMetrics.AddCompose(startedAt);
                    return;
                }
                else
                {
                    region = dirtyRects[0];
                    for (int i = 1; i < dirtyRects.Count; i++)
                        region = Rectangle.Union(region, dirtyRects[i]);
                }
                dirtyRects.Clear();
            }

            ExpandRegionForDirtyRoots(ref region);
            if (drawingDebugEnabled)
                TrackDrawingDebugRegion(region);
            Kernel.mainBuffer.DrawFilledRectangle(Color.Black, region.X, region.Y, region.Width, region.Height);
            frameComposed = true;

            for (int componentIndex = 0; componentIndex < components.Count; componentIndex++)
            {
                Component component = components[componentIndex];
                if (component == null || !component.Visible || !component.isRoot) continue;

                if (component is Window rooto && !IsOnCurrentDesktop(rooto)) continue;

                Rectangle componentBounds = component.AbsoluteRectangle;
                Rectangle clipped = Rectangle.Intersect(componentBounds, region);
                if (clipped.Width <= 0 || clipped.Height <= 0) continue;

                Window owner = component.GetOwningWindow();
                if (owner != null && failedWindows.Contains(owner)) continue;

                try
                {
                    if (component.HasDirtyTree())
                    {
                        component.DrawDirtyLocal(region);
                        component.MarkCleaned();
                    }

                    if (component.useGlass)
                    {
                        // Everything below the glass surface is already composited, so the
                        // backbuffer now holds its real backdrop: blur it, then blit the
                        // surface with alpha so its transparent pixels show through.
                        ComposeGlassBackdrop(component, region);
                        component.DrawToScreenAlpha(region);
                    }
                    else
                    {
                        component.DrawToScreen(region);
                    }
                }
                catch (Exception exception)
                {
                    lock (dirtyRectsLock)
                        fullComposePending = true;
                    FailApplication(owner, "Drawing", exception);
                }
            }

            if (drawingDebugEnabled)
                Kernel.mainBuffer.DrawRectangle(Color.Magenta, region.X, region.Y, region.Width, region.Height);

            PerformanceMetrics.AddCompose(startedAt);
        }
    }

    private static void TrackDrawingDebugRegion(Rectangle region)
    {
        for (int i = 0; i < drawingDebugRects.Count; i++)
        {
            if (!drawingDebugRects[i].IntersectsWith(region)) continue;

            region = Rectangle.Union(drawingDebugRects[i], region);
            drawingDebugRects.RemoveAt(i);
            i--;
        }

        drawingDebugRects.Add(region);
    }

    public static void SetDrawingDebug(bool enabled)
    {
        lock (Component.components)
        {
            if (drawingDebugEnabled == enabled) return;

            drawingDebugEnabled = enabled;
            for (int i = 0; i < drawingDebugRects.Count; i++)
                Invalidate(drawingDebugRects[i]);

            drawingDebugRects.Clear();
            Invalidate(new Rectangle(0, 0, Global.screenWidth, Global.screenHeight));
        }
    }

    public static Window? GetTopWindowAt(int x, int y)
    {
        lock (windows)
        {
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                Window win = windows[i];
                if (win == null || !win.Visible || win.isMinimized || failedWindows.Contains(win)) continue;
                if (win.desktop >= 0 && win.desktop != CurrentDesktop) continue;
                if (win.HitTest(x, y)) return win;
            }
        }
        return null;
    }

    private void ExpandRegionForDirtyRoots(ref Rectangle region)
    {
        Rectangle screenBounds = new Rectangle(0, 0, Global.screenWidth, Global.screenHeight);
        bool expanded = true;
        while (expanded)
        {
            expanded = false;
            for (int i = 0; i < components.Count; i++)
            {
                Component component = components[i];
                if (component == null || !component.Visible || !component.isRoot) continue;
                if (component is Window hidden && !IsOnCurrentDesktop(hidden)) continue;

                Rectangle bounds = Rectangle.Intersect(component.AbsoluteRectangle, screenBounds);
                if (bounds.Width <= 0 || bounds.Height <= 0 || !bounds.IntersectsWith(region)) continue;

                // Expand if the root has dirty content, or if it uses glass transparency.
                // Glass surfaces (like window title bars) sample and blur what is already in the backbuffer.
                // If only a partial slice of a glass frame is composed, the blur clamps at the slice
                // edge and leaves stale pixels outside, causing the title blur to flicker/switch between
                // blurred and unblurred states.
                if (component.HasDirtyTree() || component.useGlass)
                {
                    Rectangle union = Rectangle.Union(region, bounds);
                    if (union.Width != region.Width || union.Height != region.Height || union.X != region.X || union.Y != region.Y)
                    {
                        region = union;
                        expanded = true;
                    }
                }
            }
        }
    }

    private static readonly List<Rectangle> glassRegions = new List<Rectangle>();

    /// <summary>
    /// Turns what is already in the backbuffer under a glass surface into its blurred,
    /// washed backdrop. Only the composed region is touched, and the blur clamps its samples
    /// to that region, so stale pixels outside it are never pulled in.
    /// </summary>
    private static void ComposeGlassBackdrop(Component component, Rectangle region)
    {
        if (Kernel.mainBuffer == null) return;

        glassRegions.Clear();
        component.GetGlassRegions(glassRegions);
        if (glassRegions.Count == 0) return;

        Rectangle bounds = component.AbsoluteRectangle;
        for (int i = 0; i < glassRegions.Count; i++)
        {
            Rectangle local = glassRegions[i];
            Rectangle absolute = new Rectangle(bounds.X + local.X, bounds.Y + local.Y, local.Width, local.Height);
            Rectangle clipped = Rectangle.Intersect(absolute, region);
            if (clipped.Width <= 0 || clipped.Height <= 0) continue;

            GlassRenderer.ComposeBackdrop(clipped, component.glassStyle);
        }
    }

    public static void ShowPreviewRect(Rectangle rect)
    {
        if (hasPreviewRect)
            InvalidatePreviewRect(previewRect);

        previewRect = rect;
        hasPreviewRect = true;
        InvalidatePreviewRect(previewRect);
    }

    public static void ClearPreviewRect()
    {
        if (!hasPreviewRect) return;

        InvalidatePreviewRect(previewRect);
        hasPreviewRect = false;
    }

    private static void InvalidatePreviewRect(Rectangle rect)
    {
        Invalidate(new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2));
    }

    private static void DrawPreviewRect()
    {
        if (!hasPreviewRect || focusedWindow == null) return;

        Kernel.mainBuffer.DrawDottedRectangle(Color.White, previewRect.X, previewRect.Y, previewRect.Width, previewRect.Height);
    }


    //TODO Potential self register through the constructor
    public static void Register(Window window)
    {
        PostRegister(window);
    }

    private static void RegisterNow(Window window)
    {
        lock (windows)
        {
            if (windows.Contains(window)) return;

            // Cascade windows opened at the exact same spot so they don't stack invisibly.
            if (window.bounds.X == lastOpenBounds.X && window.bounds.Y == lastOpenBounds.Y)
            {
                int nx = window.bounds.X + 28;
                int ny = window.bounds.Y + 28;
                if (nx + window.bounds.Width > Global.screenWidth) nx = 40;
                if (ny + window.bounds.Height > Global.screenHeight) ny = 40;
                window.ApplyBounds(new Rectangle(nx, ny, window.bounds.Width, window.bounds.Height));
            }
            lastOpenBounds = window.bounds;

            window.desktop = window.showInTaskbar ? CurrentDesktop : -1;
            window.zIndex = nextZIndex;
            window.Start();
            windows.Add(window);
            nextZIndex++;
            MarkZOrderDirty();
            SetFocusedWindow(window);

            // A newly registered window has no previous dirty rectangle to drive its
            // first composition pass. Explicitly schedule that first paint.
            window.ForceDirty();
            Invalidate(window.bounds);

            if (!window.showInTaskbar || Explorer.taskbar == null) return;

            int taskbarButtonWidth = Math.Max(84, window.text.Length * 7 + (window.Icon == null ? 18 : 38));
            taskbarButtonWidth = Math.Min(taskbarButtonWidth, 180);
            int taskbarButtonHeight = Math.Max(24, Explorer.taskbar.Height - 6);

            Button taskbarButton = new Button(window.text, 0, 0, taskbarButtonWidth, taskbarButtonHeight)
            {
                verticalAlignment = VerticalAlignment.Center,
                contentAlignment = HorizontalAlignment.Left,
                iconSize = 16,
                iconTextGap = 4,
                Padding = new Thickness(3),
                Margin = new Thickness(2, 1, 2, 1),
                useBorders = !Palette.FlatControls,
                image = window.Icon,

                leftClickAction = () =>
                {
                    if (window.isMinimized)
                    {
                        Restore(window);
                    }
                    else if (focusedWindow == window)
                    {
                        Minimize(window);
                    }
                    else
                    {
                        Activate(window);
                    }
                }
            };

            taskbarButtons[window] = taskbarButton;
            Explorer.taskbar.windows.Add(taskbarButton);
            Explorer.taskbar.bar.AddStackChild(taskbarButton);
            RefreshTaskbarButtons();
            Explorer.taskbar.ForceDirty();
            Invalidate(Explorer.taskbar.AbsoluteRectangle);
        }

    }

    public static void Close(Window window)
    {
        if (window == null) return;
        PostClose(window);
        Invalidate(window.bounds);
    }

    public static void Minimize(Window window)
    {
        MinimizeImmediate(window);
    }

    public static void MinimizeAllWindows()
    {
        Window[] windowsToMinimize = windows.ToArray();
        for (int i = 0; i < windowsToMinimize.Length; i++)
        {
            Window window = windowsToMinimize[i];
            if (window.showInTaskbar && window.Visible && !window.isMinimized)
                MinimizeImmediate(window);
        }
    }

    public static void SnapFocused(int mode)
    {
        if (focusedWindow != null && !failedWindows.Contains(focusedWindow))
            focusedWindow.SnapTo(mode);
    }

    private static readonly HashSet<Window> showDesktopHidden = new HashSet<Window>();

    /// <summary>Show-desktop toggle: minimizes everything, pressing again restores.</summary>
    public static void ToggleShowDesktop()
    {
        lock (windows)
        {
            bool anyVisible = false;
            for (int i = 0; i < windows.Count; i++)
            {
                Window window = windows[i];
                if (window.showInTaskbar && window.Visible && !window.isMinimized && IsOnCurrentDesktop(window))
                {
                    anyVisible = true;
                    break;
                }
            }

            if (anyVisible)
            {
                showDesktopHidden.Clear();
                Window[] snapshot = windows.ToArray();
                for (int i = 0; i < snapshot.Length; i++)
                {
                    Window window = snapshot[i];
                    if (window.showInTaskbar && window.Visible && !window.isMinimized && IsOnCurrentDesktop(window))
                    {
                        showDesktopHidden.Add(window);
                        MinimizeImmediate(window);
                    }
                }
            }
            else
            {
                Window[] snapshot = new Window[showDesktopHidden.Count];
                showDesktopHidden.CopyTo(snapshot);
                showDesktopHidden.Clear();
                for (int i = 0; i < snapshot.Length; i++)
                {
                    if (windows.Contains(snapshot[i]))
                        Activate(snapshot[i]);
                }
            }
        }
    }

    internal static void MinimizeImmediate(Window window)
    {
        if (window == null || !window.canMinimize || window.isMinimized) return;

        Invalidate(window.bounds);
        window.SetFocused(false);
        window.MinimizeWindow();

        if (focusedWindow == window)
            focusedWindow = null;

        FocusTopVisibleWindow(window);
        RefreshTaskbarButtons();
        Explorer.taskbar.MarkDirty();
    }

    public static void Restore(Window window)
    {
        if (window == null) return;
        Activate(window);
    }

    public static void ToggleMaximize(Window window)
    {
        if (window == null || !window.canMaximize) return;

        Rectangle oldBounds = window.bounds;
        int workAreaHeight = Explorer.taskbar != null
            ? Explorer.taskbar.Y
            : Global.screenHeight;

        window.ToggleMaximized(new Rectangle(0, 0, Global.screenWidth, workAreaHeight));
        Invalidate(oldBounds);
        Invalidate(window.bounds);
        Activate(window);
    }

    public static void SetWindowTitle(Window window, string title)
    {
        if (window == null) return;

        window.SetTitle(title);

        if (!taskbarButtons.TryGetValue(window, out Button taskbarButton)) return;

        taskbarButton.text = window.text;
        taskbarButton.MarkDirty();

        if (Explorer.taskbar == null) return;

        Explorer.taskbar.ForceDirty();
        Invalidate(Explorer.taskbar.AbsoluteRectangle);
    }

    public static void Activate(Window window)
    {
        if (window == null) return;

        if (window.isMinimized)
            window.RestoreFromTaskbar();

        window.zIndex = nextZIndex++;
        MarkZOrderDirty();
        SetFocusedWindow(window);
        Invalidate(window.bounds);
    }

    private static void FocusTopVisibleWindow(Window excludedWindow)
    {
        Window nextWindow = null;

        for (int i = 0; i < windows.Count; i++)
        {
            Window candidate = windows[i];
            if (candidate == excludedWindow || !candidate.Visible) continue;
            if (!IsOnCurrentDesktop(candidate)) continue;
            if (nextWindow == null || candidate.zIndex > nextWindow.zIndex)
                nextWindow = candidate;
        }

        if (nextWindow != null)
            SetFocusedWindow(nextWindow);
    }

    private void FailApplication(Window window, string operation, Exception exception)
    {
        if (window != null && failedWindows.Contains(window))
        {
            return;
        }
        string applicationName = window?.text;
        if (string.IsNullOrEmpty(applicationName))
        {
            applicationName = "Application";
        }

        string detail = exception?.Message;
        if (string.IsNullOrEmpty(detail))
        {
            detail = "Unknown error";
        }

        SystemLogger.WriteLine(applicationName, "failed while " + operation + ": " + detail + "\n", ConsoleMessageType.Error);
        pendingFailures.Add(new ApplicationFailure(applicationName, operation, detail));

        if (window == null)
        {
            return;
        }

        failedWindows.Add(window);
        if (focusedWindow == window)
        {
            focusedWindow = null;
        }

        if (capturedWindow == window)
        {
            capturedWindow = null;
        }

        if (capturedComponent != null && capturedComponent.GetOwningWindow() == window)
        {
            capturedComponent = null;
        }

        PostClose(window);
    }

    private void ShowPendingFailures()
    {
        if (pendingFailures.Count == 0) return;

        for (int i = 0; i < pendingFailures.Count; i++)
        {
            ApplicationFailure failure = pendingFailures[i];
            try
            {
                string message = failure.application + " crashed while " + failure.operation + ": " + failure.detail;


                Window error = new Window(140, 140, 680, 130, "Application Error", true)
                {
                    canMaximize = false,
                    canResize = false,
                };
                Panel text = new Panel(Palette.ControlFace, 8, 34, 664, 48)
                {
                    text = message,
                    fontSize = 16,
                    textColor = Palette.ControlBlack,
                    useBackground = true,
                    horizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(8, 34, 8, 8),

                };
                error.AddChild(text);
                RegisterNow(error);
            }
            catch (Exception reportException)
            {
                SystemLogger.WriteLine("DWM", "Could not display application error: " + reportException.Message + "\n", ConsoleMessageType.Error);
            }
        }

        pendingFailures.Clear();
    }

    private void CloseNow(Window window)
    {
        lock (windows)
        {

            if (window == null || !windows.Contains(window)) return;

            BreezeRuntime.NotifyWindowClosed(window);

            Invalidate(window.bounds);
            ClearPreviewRect();

            if (focusedWindow == window)
                focusedWindow = null;

            if (capturedWindow == window)
                capturedWindow = null;

            if (capturedComponent != null && capturedComponent.GetOwningWindow() == window)
                capturedComponent = null;

            if (taskbarButtons.ContainsKey(window))
            {
                Button taskbarButton = taskbarButtons[window];

                taskbarButtons.Remove(window);

                Explorer.taskbar.windows.Remove(taskbarButton);
                Explorer.taskbar.bar.RemoveStackChild(taskbarButton);
                Explorer.taskbar.bar.ForceDirty();
                Explorer.taskbar.ForceDirty();

                Invalidate(Explorer.taskbar.AbsoluteRectangle);

            }

            windows.Remove(window);
            failedWindows.Remove(window);
            showDesktopHidden.Remove(window);

            FocusTopVisibleWindow(window);
            RefreshTaskbarButtons();
            window.Stop();
        }
    }

    public static void Invalidate(Component dirty)
    {
        Invalidate(dirty.AbsoluteRectangle);

    }

    public static void Invalidate(Rectangle dirtyRect)
    {
        dirtyRect = Rectangle.Intersect(
            dirtyRect,
            new Rectangle(0, 0, Global.screenWidth, Global.screenHeight));

        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0) return;

        lock (dirtyRectsLock)
        {
            for (int i = 0; i < dirtyRects.Count; i++)
            {
                if (dirtyRects[i].Equals(dirtyRect)) return;
                if (!dirtyRects[i].IntersectsWith(dirtyRect)) continue;

                dirtyRect = Rectangle.Union(dirtyRects[i], dirtyRect);
                dirtyRects.RemoveAt(i);
                i--;
            }

            dirtyRects.Add(dirtyRect);
        }
    }

    public static void InvalidateAll()
    {
        for (int i = 0; i < Component.components.Count; i++)
        {
            Component component = Component.components[i];
            if (component == null || !component.Visible) continue;
            component.ForceDirty();
        }

        Invalidate(new Rectangle(0, 0, Global.screenWidth, Global.screenHeight));
    }


    public static void BringToFront(Window window)
    {
        window.zIndex = nextZIndex++;
        MarkZOrderDirty();
        Invalidate(window);
    }


    private readonly struct ApplicationFailure
    {
        public readonly string application;
        public readonly string operation;
        public readonly string detail;

        public ApplicationFailure(string application, string operation, string detail)
        {
            this.application = application;
            this.operation = operation;
            this.detail = detail;
        }
    }
}
