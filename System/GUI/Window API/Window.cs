using System.Drawing;
using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using Windose.System.System_Calls;

public class Window : Component
{
    public Rectangle bounds; //Window viewport, relative to the screen
    public Canvas Canvas => RenderCanvas;

    private bool inFocus;

    private bool dragging;
    private bool resizing;
    public bool isMinimized { get; private set; }
    public bool isMaximized { get; private set; }
    private Rectangle restoreBounds;

    public bool canMaximize = true;
    public bool canMinimize = true;
    public bool canResize = true;
    public bool canMove = true;
    public bool showInTaskbar = true;

    /// <summary>When true, requesting window close hides the window to the system tray instead of terminating.</summary>
    public bool closeToTray;

    /// <summary>When true, minimizing the window hides it to the system tray instead of the taskbar.</summary>
    public bool minimizeToTray;

    /// <summary>When true, the window is created directly in the system tray without showing on screen or taskbar.</summary>
    public bool openToTray;

    /// <summary>True if the window is currently docked/hidden in the system tray.</summary>
    public bool isInTray { get; internal set; }

    protected Component? trayIconComponent;
    protected MenuPopup? trayIconMenu;

    /// <summary>
    /// Draws a Windows-7-style glass frame (blurred, tinted backdrop showing through the
    /// border and title bar) instead of the plain raised one. Opt-in per window.
    /// </summary>
    public bool glassFrame
    {
        get => useGlass;
        set => useGlass = value;
    }

    /// <summary>Virtual desktop index. -1 pins the window to all desktops (taskbar, menus).</summary>
    public int desktop;




    private const int WindowFrameInset = 2;
    public int resizeMargin = 10;

    [Flags]
    public enum ResizeBorder
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8,
        TopLeft = Top | Left,
        TopRight = Top | Right,
        BottomLeft = Bottom | Left,
        BottomRight = Bottom | Right
    }

    private ResizeBorder activeResizeBorder = ResizeBorder.None;
    public bool isSnapped { get; private set; }
    public int snapMode { get; private set; }
    private Rectangle snapRestoreBounds;

    private Point offset;
    private Point resizeStart;
    private Rectangle original;
    private Rectangle previewBounds;
    private int lastTitlePress;

    protected Component? focusedComponent;
    public void SetFocusedComponent(Component? c)
    {
        focusedComponent = c;
        c?.RequestFocus();
    }

    public Png? Icon;

    public void SetIcon(Png? icon)
    {
        Icon = icon;
        if (hasTitleBar && titlebar != null)
        {
            titlebar.textOffsetX = (icon != null ? 26 : 0);
            titlebar.MarkDirty();
        }
        WindowManager.UpdateWindowIcon(this, icon);
        MarkDirty();
    }

    public void SetIcon(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            SetIcon((Png?)null);
            return;
        }

        try
        {
            string p = iconPath.Trim();
            if (File.Exists(p))
            {
                SetIcon(new Png(p));
                return;
            }

            string name = Path.GetFileName(p);
            string sys = Path.Combine("/mnt/System/Icons", name);
            if (File.Exists(sys))
            {
                SetIcon(new Png(sys));
                return;
            }

            if (!sys.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                sys += ".png";
                if (File.Exists(sys))
                {
                    SetIcon(new Png(sys));
                    return;
                }
            }
        }
        catch { }
    }

    protected StackPanel titlebar;
    private bool hasTitleBar;
    public bool HasTitleBar => hasTitleBar;
    private bool windowFocused;


    protected SingleThreadedProcess process;

    public Action? onClosed;                 // fired after the window's process is disposed
    public bool closeRequested;

    //TODO Process manager should be responsible for every update instead of window manager being a separate subsystem
    public Window(int x, int y, int width, int height, string title, bool useTitleBar = false, Png? icon = null) : base(x, y, width, height)
    {
        canRender = true;
        isRoot = true;
        EnsureRenderSurface();
        text = title;
        Icon = icon;
        bounds = new Rectangle(x, y, width, height);
        zLayer = DrawLayer.Window;

        process = new SingleThreadedProcess(title, ProcessType.Program);

        //process.onStart += () => WindowManager.PostRegister(this);
        process.onDispose += () => 
        {
            if (trayIconComponent != null)
            {
                Tray.RemoveTrayIcon(trayIconComponent);
                trayIconComponent = null;
            }
            if (!disposed)
            {
                WindowManager.PostClose(this);
            }
            onClosed?.Invoke(); 
        };
        process.onUpdate += Update;

        if (Palette.GlassEnabled && useTitleBar)
        {
            useGlass = true;
            glassStyle = GlassStyle.AeroDefault();
        }
        TitlebarSetup(useTitleBar, title);
    }


    public void Start()
    {
        ProcessManager.QueueStart(process);
    }

    public override void Update()
    {
        try
        {
            // Do not let Window itself toggle state on hover; only update visible children.
            for (int i = 0; i < children.Count; i++)
            {
                Component child = children[i];
                if (child.Visible)
                    child.Update();
            }
        }
        catch (Exception ex)
        {
            SystemLogger.WriteLine("Worker Thread", ex.Message, ConsoleMessageType.Error);
        }
    }

    /// <summary>
    /// Coordinates relative to the screen
    /// </summary>

    /// <summary>
    /// Component rendering, coordinates relative to the window
    /// </summary>
    public bool drawFrame = true;

    public override void DrawLocal()
    {
        if (drawFrame)
        {
            if (useGlass) DrawGlassFrame();
            else DrawRaisedRectangle(0, 0, Width, Height);
        }


        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            DrawChild(child);
        }

        if (Icon != null)
        {
            int iconY = hasTitleBar ? Math.Max(2, (Palette.TitleBarHeight - 16) / 2) : WindowFrameInset + 2;
            int iconX = hasTitleBar ? Palette.BorderSize + 4 : WindowFrameInset + 2;
            DrawImageStretch(Icon, new Rectangle(iconX, iconY, 16, 16));
        }

        //if (Width > WindowFrameInset * 2 && Height > WindowFrameInset * 2)
          //  DrawRaisedRectangle(WindowFrameInset, WindowFrameInset,
            //    Width - WindowFrameInset * 2, Height - WindowFrameInset * 2);
    }

    private static readonly List<Rectangle> frameBands = new List<Rectangle>();

    /// <summary>
    /// Paints the glass frame that replaces the plain raised rectangle. Override this to draw
    /// custom chrome (gloss sweeps, bevels, reflections, per-pixel opacity ramps): the frame
    /// bands just have to keep their alpha, and the compositor shows the blurred backdrop
    /// through them. The default punches the bands to transparent, keeps the client area
    /// opaque, lays the style's tint over the bands and adds a crisp bevel.
    /// </summary>
    protected virtual void DrawGlassFrame()
    {
        frameBands.Clear();
        GetGlassRegions(frameBands);

        Color tint = windowFocused ? glassStyle.Tint : Color.FromArgb(60, 110, 135, 160);
        for (int i = 0; i < frameBands.Count; i++)
        {
            Rectangle band = frameBands[i];
            FillRaw(Color.FromArgb(0, 0, 0, 0), band.X, band.Y, band.Width, band.Height);
            if (tint.A > 0)
                DrawOverRectangle(tint, band.X, band.Y, band.Width, band.Height);
        }

        int r = 5; // rounded top corners radius

        // Clear outer corner pixels at the top so desktop shows through rounded top corners
        for (int row = 0; row < r && row < Height; row++)
        {
            ComponentBuffer.GetRoundedTopSpan(row, Width, Height, r, out int sX, out int eX);
            if (sX > 0)
            {
                FillRaw(Color.FromArgb(0, 0, 0, 0), 0, row, sX, 1);
                FillRaw(Color.FromArgb(0, 0, 0, 0), eX, row, Width - eX, 1);
            }
        }

        // Windows 7 Aero glossy top reflection sweep across the titlebar
        int topHeight = Math.Min(Height, Palette.TitleBarHeight);
        if (topHeight > 4)
        {
            DrawOverGradientVertical(Color.FromArgb(90, 255, 255, 255), Color.FromArgb(15, 255, 255, 255), r, 1, Math.Max(1, Width - r * 2), topHeight / 2);
        }

        // The client body stays opaque so content remains readable on top of the glass.
        Rectangle client = ClientRectangle();
        FillRaw(Palette.WindowBackground, client.X, client.Y, client.Width, client.Height);

        // Crisp edges: dark outline outside, bright bevel inside, with rounded top corners
        DrawRoundedTopOutline(glassStyle.BorderDark, 0, 0, Width, Height, r);
        if (Width > 2 && Height > 2)
            DrawRoundedTopOutline(glassStyle.BorderBright, 1, 1, Width - 2, Height - 2, r - 1);

        // Subtle 1px client area inner border separating client area from glass
        if (client.Width > 0 && client.Height > 0)
        {
            DrawRectangle(Color.FromArgb(180, 195, 215), client.X - 1, client.Y - 1, client.Width + 2, client.Height + 2);
        }
    }

    /// <summary>Opaque client area of a titled window, in window-local coordinates.</summary>
    protected Rectangle ClientRectangle()
    {
        int border = Palette.BorderSize;
        int top = Math.Min(Height, Palette.TitleBarHeight);
        return new Rectangle(border, top,
            Math.Max(0, Width - border * 2),
            Math.Max(0, Height - top - border));
    }

    /// <summary>Only the frame is see-through, so only the frame band gets the backdrop blur.</summary>
    public override void GetGlassRegions(List<Rectangle> regions)
    {
        int border = Palette.BorderSize;
        int top = Math.Min(Height, Palette.TitleBarHeight);

        regions.Add(new Rectangle(0, 0, Width, top));
        if (border > 0)
        {
            regions.Add(new Rectangle(0, top, border, Height - top));
            regions.Add(new Rectangle(Width - border, top, border, Height - top));
            regions.Add(new Rectangle(0, Height - border, Width, border));
        }
    }


    private void TitlebarSetup(bool useTitleBar, string title)
    {
        if (useTitleBar)
        {
            int border = Palette.BorderSize;
            int titleHeight = Palette.TitleBarHeight;

            titlebar = new StackPanel(Palette.InactiveTitle, border, 0, Width - (border * 2), titleHeight)
            {
                useBackground = !useGlass,
                textColor = Palette.TitleTextInactive,
                clampSize = false,
                text = title,
                fontSize = 15,
                horizontalAlignment = HorizontalAlignment.Stretch,
                orientation = StackOrientation.Horizontal,
                Margin = Thickness.FromLTRB(border, 0, border, 0),
                Padding = new Thickness(0),
                spacing = 1
            };

            if (Icon != null) titlebar.textOffsetX = 26;

            AddChild(titlebar);
            hasTitleBar = true;

            int titleButtonSize = titleHeight - 4;
            int titleButtonTop = 0;

            CreateTitleControls(titleButtonSize, titleButtonTop);
        }
    }

    private void CreateTitleControls(int titleButtonSize, int titleButtonTop)
    {
        bool modern = useGlass || Palette.FlatControls;
        int closeWidth = modern ? 47 : titleButtonSize;
        int normWidth = modern ? 29 : titleButtonSize;
        int btnHeight = modern ? 21 : titleButtonSize;

        Button closeBtn = new Button("", 0, 0, closeWidth, btnHeight)
        {
            verticalAlignment = VerticalAlignment.Top,
            horizontalAlignment = HorizontalAlignment.Right,
            useBorders = false,
            useBackground = false,
            Margin = new Thickness(0, 1, 1, 0),
            Padding = new Thickness(0),
            leftClickAction = () =>
            {
                RequestClose();
            }
        };
        closeBtn.isCloseButton = true;
        titlebar.AddStackChild(closeBtn);

        Button maxBtn = new Button("", 0, 0, normWidth, btnHeight)
        {
            verticalAlignment = VerticalAlignment.Top,
            horizontalAlignment = HorizontalAlignment.Right,
            useBorders = false,
            useBackground = false,
            Margin = new Thickness(0, 1, 1, 0),
            Padding = new Thickness(0),
            leftClickAction = () =>
            {
                WindowManager.ToggleMaximize(this);
            }
        };
        maxBtn.isCaptionButton = true;
        maxBtn.isMaximizeButton = true;
        titlebar.AddStackChild(maxBtn);

        Button minBtn = new Button("", 0, 0, normWidth, btnHeight)
        {
            verticalAlignment = VerticalAlignment.Top,
            horizontalAlignment = HorizontalAlignment.Right,
            useBorders = false,
            useBackground = false,
            Margin = new Thickness(0, 1, 1, 0),
            Padding = new Thickness(0),
            leftClickAction = () =>
            {
                WindowManager.Minimize(this);
            }
        };
        minBtn.isCaptionButton = true;
        minBtn.isMinimizeButton = true;
        titlebar.AddStackChild(minBtn);
    }

    private void ApplyTitlebarTheme()
    {
        if (!hasTitleBar || titlebar == null) return;


        if (useGlass)
        {
            titlebar.blendBackground = true;
            titlebar.useBackground = false;
            titlebar.color1 = Color.FromArgb(0, 0, 0, 0);
        }
        else
        {
            titlebar.blendBackground = false;
            titlebar.color1 = windowFocused ? Palette.ActiveTitle : Palette.InactiveTitle;
        }

        titlebar.textColor = windowFocused ? Palette.TitleText : Palette.TitleTextInactive;
        titlebar.MarkDirty();
    }

    public void SetTitle(string title)
    {
        text = title ?? "";

        if (hasTitleBar && titlebar != null)
        {
            titlebar.text = text;
            titlebar.MarkDirty();
        }

        WindowManager.UpdateWindowTitle(this, text);
        MarkDirty();
    }



    public override bool HandleInput(int mouseX, int mouseY, MouseState mouseState)
    {
        if (!IsInputEnabled()) return false;

        // Active resize capture takes top priority
        if (resizing)
        {
            ResizeWindow(mouseState, mouseX, mouseY);
            return true;
        }

        // Active drag capture takes priority
        if (dragging)
        {
            DragWindow(mouseState, mouseX, mouseY);
            return true;
        }

        // Check for resize start before titlebar drag, menus, or child components intercept it
        if (mouseState.left == MouseEvents.Press && canResize && !isMaximized)
        {
            ResizeBorder border = HitTestResizeBorder(mouseX, mouseY);
            if (border != ResizeBorder.None)
            {
                ResizeWindow(mouseState, mouseX, mouseY);
                return true;
            }
        }

        if (mouseState.right == MouseEvents.Press && TitleHitTest(mouseX, mouseY) && !(GetChildAt(mouseX, mouseY) is Button))
        {
            ShowSystemMenu(mouseX, mouseY);
            return true;
        }
        if (mouseState.left == MouseEvents.Press && hasTitleBar && IconHitTest(mouseX, mouseY))
        {
            ShowSystemMenu(bounds.X, bounds.Y + Palette.TitleBarHeight);
            return true;
        }
        if (Mouse.scroll != 0)
        {

            Component wheelTarget = GetChildAt(mouseX, mouseY);
            while (wheelTarget != null && wheelTarget != this)
            {
                if (wheelTarget.HandlesMouseWheel)
                    return wheelTarget.HandleInput(mouseX, mouseY, mouseState);
                wheelTarget = wheelTarget.Parent;
            }
        }

        if (mouseState.left == MouseEvents.Press || mouseState.right == MouseEvents.Press)
        {
            inFocus = HitTest(mouseX, mouseY);

            Component? hit = GetChildAt(mouseX, mouseY);


            focusedComponent = (hit != this && hit != titlebar) ? hit : null;

            if (focusedComponent != null && focusedComponent.TabStop)
                focusedComponent.RequestFocus();
            else if (focusedComponent == null)
                Component.ClearFocus();
        }


        DragWindow(mouseState, mouseX, mouseY);
        if (dragging) return true;

        if (focusedComponent != null && focusedComponent.HandleInput(mouseX, mouseY, mouseState))
            return true;


        return HitTest(mouseX, mouseY);
    }

    public void SetFocused(bool focused)
    {
        if (windowFocused == focused) return;
        windowFocused = focused;
        if (!focused) OnLoseFocus();

        if (!hasTitleBar) return;

        ApplyTitlebarTheme();
        ForceDirty();
    }

    public virtual void OnLoseFocus()
    {
        if (resizing)
        {
            resizing = false;
            activeResizeBorder = ResizeBorder.None;
            WindowManager.ClearPreviewRect();
        }
        if (dragging)
        {
            dragging = false;
            WindowManager.ClearPreviewRect();
        }
    }

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        if (keyEvent.Key == Key.Tab)
        {
            FocusNextTabStop(IsShiftPressed(keyEvent));
            return;
        }

        Component target = focusedComponent;
        if (target == null && Component.FocusedComponent != null && Component.FocusedComponent.IsDescendantOf(this))
            target = Component.FocusedComponent;
        if (target != null) target.HandleKeyboard(keyEvent);
        else base.HandleKeyboard(keyEvent);
    }

    private void FocusNextTabStop(bool reverse)
    {
        List<Component> stops = new List<Component>();
        CollectTabStops(this, stops);
        if (stops.Count == 0) return;
        stops.Sort((a, b) =>
        {
            int t = a.TabIndex.CompareTo(b.TabIndex);
            return t != 0 ? t : a.zIndex.CompareTo(b.zIndex);
        });

        int current = -1;
        for (int i = 0; i < stops.Count; i++)
        {
            if (stops[i] == focusedComponent || stops[i] == Component.FocusedComponent)
            {
                current = i;
                break;
            }
        }

        int next = current < 0 ? (reverse ? stops.Count - 1 : 0)
                               : (current + (reverse ? stops.Count - 1 : 1)) % stops.Count;
        focusedComponent = stops[next];
        stops[next].RequestFocus();
    }

    private static void CollectTabStops(Component parent, List<Component> stops)
    {
        for (int i = 0; i < parent.children.Count; i++)
        {
            Component child = parent.children[i];
            if (child == null || !child.Visible || !child.IsInputEnabled()) continue;
            if (child.TabStop) stops.Add(child);
            CollectTabStops(child, stops);
        }
    }

    public bool FocusCheck(int mouseX, int mouseY, MouseState mouseState)
    {
        return HitTest(mouseX, mouseY) && mouseState.left == MouseEvents.Press;
    }

    private void DragWindow(MouseState mouse, int mouseX, int mouseY)
    {
        if (resizing || !canMove) return;

        // If clicking on resize border, do not initiate dragging or double-click maximize
        if (mouse.left == MouseEvents.Press && canResize && !isMaximized && HitTestResizeBorder(mouseX, mouseY) != ResizeBorder.None)
            return;

        if (mouse.left == MouseEvents.Press && TitleHitTest(mouseX, mouseY) && canMaximize)
        {
            int tick = Environment.TickCount;
            if (unchecked(tick - lastTitlePress) >= 0 && unchecked(tick - lastTitlePress) <= 500)
            {
                lastTitlePress = 0;
                dragging = false;
                WindowManager.ClearPreviewRect();
                WindowManager.ToggleMaximize(this);
                return;
            }
            lastTitlePress = tick;
        }

        // If maximized or snapped, dragging titlebar down un-maximizes/un-snaps to floating geometry!
        if ((isMaximized || isSnapped) && mouse.left == MouseEvents.Press && TitleHitTest(mouseX, mouseY))
        {
            Rectangle rest = isMaximized ? (restoreBounds.Width > 0 ? restoreBounds : bounds)
                                         : (snapRestoreBounds.Width > 0 ? snapRestoreBounds : bounds);
            if (isMaximized)
            {
                WindowManager.ToggleMaximize(this);
            }
            else if (isSnapped)
            {
                isSnapped = false;
                snapMode = 0;
                ApplyBounds(rest);
            }

            int targetW = rest.Width > 0 ? rest.Width : Width;
            int targetH = rest.Height > 0 ? rest.Height : Height;
            int newX = Math.Clamp(mouseX - targetW / 2, 0, Math.Max(0, Global.screenWidth - targetW));
            int newY = Math.Max(0, mouseY - 10);
            Move(newX, newY);
            dragging = true;
            offset = new Point(mouseX - bounds.X, mouseY - bounds.Y);
            previewBounds = bounds;
            WindowManager.ShowPreviewRect(previewBounds);
            return;
        }

        if (isMaximized) return;

        if (mouse.left == MouseEvents.Press && TitleHitTest(mouseX, mouseY))
        {
            dragging = true;
            offset = new Point(mouseX - bounds.X, mouseY - bounds.Y);
            previewBounds = bounds;
            WindowManager.ShowPreviewRect(previewBounds);
            return;
        }

        if (!dragging) return;

        // Update previewBounds with current/final drag coordinates
        const int zone = 8;
        Rectangle work = WorkArea();

        if (mouseY <= work.Y + zone && canMaximize)
        {
            // Top snap: maximize preview
            previewBounds = work;
        }
        else if (mouseX <= work.X + zone && canResize)
        {
            // Left snap: 50% left half preview
            previewBounds = new Rectangle(work.X, work.Y, work.Width / 2, work.Height);
        }
        else if (mouseX >= work.Right - zone && canResize)
        {
            // Right snap: 50% right half preview
            previewBounds = new Rectangle(work.X + work.Width / 2, work.Y, work.Width - work.Width / 2, work.Height);
        }
        else
        {
            previewBounds = new Rectangle(mouseX - offset.X, mouseY - offset.Y, Width, Height);
        }

        if (mouse.left == MouseEvents.Release || mouse.left == MouseEvents.None)
        {
            dragging = false;
            WindowManager.ClearPreviewRect();
            if (!TrySnapToEdge(previewBounds, mouseX, mouseY))
                Move(previewBounds.X, previewBounds.Y);
        }
        else
        {
            WindowManager.ShowPreviewRect(previewBounds);
        }
    }

    private static Rectangle WorkArea()
    {
        int h = Explorer.taskbar != null ? Explorer.taskbar.Y : Global.screenHeight;
        return new Rectangle(0, 0, Global.screenWidth, h);
    }

    /// <summary>Snap a dropped window to a screen edge. True when snapped.</summary>
    private bool TrySnapToEdge(Rectangle dropped, int mouseX, int mouseY)
    {
        if (!canResize && !canMaximize) return false;
        const int zone = 8;
        Rectangle work = WorkArea();

        if (mouseY <= work.Y + zone && canMaximize)
        {
            WindowManager.ToggleMaximize(this);
            return true;
        }

        if (mouseX <= work.X + zone && canResize)
        {
            if (!isSnapped) snapRestoreBounds = bounds;
            isSnapped = true;
            snapMode = 2; // Left
            ApplyBounds(new Rectangle(work.X, work.Y, work.Width / 2, work.Height));
            return true;
        }

        if (mouseX >= work.Right - zone && canResize)
        {
            if (!isSnapped) snapRestoreBounds = bounds;
            isSnapped = true;
            snapMode = 3; // Right
            ApplyBounds(new Rectangle(work.X + work.Width / 2, work.Y, work.Width - work.Width / 2, work.Height));
            return true;
        }

        isSnapped = false;
        snapMode = 0;
        return false;
    }

    /// <summary>Snap left half (0), right half (1), or maximize (2).</summary>
    public void SnapTo(int mode)
    {
        if (isMinimized) return;
        if (mode == 2)
        {
            if (!isMaximized && canMaximize) WindowManager.ToggleMaximize(this);
            return;
        }
        if (!canResize) return;
        if (isMaximized) ToggleMaximized(restoreBounds);
        if (!isSnapped) snapRestoreBounds = bounds;

        Rectangle work = WorkArea();
        if (mode == 0)
        {
            isSnapped = true;
            snapMode = 2; // Left
            ApplyBounds(new Rectangle(work.X, work.Y, work.Width / 2, work.Height));
        }
        else if (mode == 1)
        {
            isSnapped = true;
            snapMode = 3; // Right
            ApplyBounds(new Rectangle(work.X + work.Width / 2, work.Y, work.Width - work.Width / 2, work.Height));
        }
    }

    private void ResizeWindow(MouseState mouseState, int mouseX, int mouseY)
    {
        if (!canResize || isMaximized) return;

        if (mouseState.left == MouseEvents.Press)
        {
            ResizeBorder border = HitTestResizeBorder(mouseX, mouseY);
            if (border != ResizeBorder.None)
            {
                resizing = true;
                activeResizeBorder = border;
                resizeStart = new Point(mouseX, mouseY);
                original = bounds;
                previewBounds = bounds;
                if (isSnapped)
                {
                    isSnapped = false;
                    snapMode = 0;
                }
                WindowManager.ShowPreviewRect(previewBounds);
                Windose.Drivers.CursorManager.SetCursor(GetCursorForBorder(border));
            }
            return;
        }

        if (!resizing) return;

        int dx = mouseX - resizeStart.X;
        int dy = mouseY - resizeStart.Y;

        int newX = original.X;
        int newY = original.Y;
        int newWidth = original.Width;
        int newHeight = original.Height;

        const int minW = 120;
        const int minH = 60;

        if ((activeResizeBorder & ResizeBorder.Left) != 0)
        {
            int candidateW = original.Width - dx;
            if (candidateW >= minW)
            {
                newX = original.X + dx;
                newWidth = candidateW;
            }
            else
            {
                newX = original.Right - minW;
                newWidth = minW;
            }
        }
        else if ((activeResizeBorder & ResizeBorder.Right) != 0)
        {
            newWidth = Math.Max(minW, original.Width + dx);
        }

        if ((activeResizeBorder & ResizeBorder.Top) != 0)
        {
            int candidateH = original.Height - dy;
            if (candidateH >= minH)
            {
                newY = original.Y + dy;
                newHeight = candidateH;
            }
            else
            {
                newY = original.Bottom - minH;
                newHeight = minH;
            }
        }
        else if ((activeResizeBorder & ResizeBorder.Bottom) != 0)
        {
            newHeight = Math.Max(minH, original.Height + dy);
        }

        previewBounds = new Rectangle(newX, newY, newWidth, newHeight);

        if (mouseState.left == MouseEvents.Release || mouseState.left == MouseEvents.None)
        {
            resizing = false;
            activeResizeBorder = ResizeBorder.None;
            WindowManager.ClearPreviewRect();
            Resize(previewBounds.X, previewBounds.Y, previewBounds.Width, previewBounds.Height);
        }
        else
        {
            WindowManager.ShowPreviewRect(previewBounds);
        }
    }

    public void ShowSystemMenu(int mouseX, int mouseY)
    {
        MenuPopup menu = new MenuPopup(160, 22)
        {
            itemHeight = 22
        };

        bool canRestore = isMaximized || isSnapped;
        MenuItem restore = menu.AddItem("Restore", () =>
        {
            if (isMaximized) WindowManager.ToggleMaximize(this);
            else if (isSnapped)
            {
                isSnapped = false;
                snapMode = 0;
                ApplyBounds(snapRestoreBounds.Width > 0 ? snapRestoreBounds : bounds);
            }
        });
        restore.enabled = canRestore;

        MenuItem move = menu.AddItem("Move", () => { });
        move.enabled = canMove && !isMaximized;

        MenuItem size = menu.AddItem("Size", () => { });
        size.enabled = canResize && !isMaximized;

        if (canMinimize)
        {
            MenuItem min = menu.AddItem("Minimize", () => WindowManager.Minimize(this));
            min.enabled = !isMinimized;
        }

        if (canMaximize)
        {
            MenuItem max = menu.AddItem("Maximize", () => WindowManager.ToggleMaximize(this));
            max.enabled = !isMaximized;
        }

        menu.AddSeparator();
        menu.AddItem("Close\tAlt+F4", () => RequestClose());

        menu.ShowAt(mouseX, Math.Min(mouseY, Global.screenHeight - 160));
    }

    public virtual bool HitTest(int mouseX, int mouseY)
    {
        if (isMinimized || !Visible) return false;

        if (canResize && !isMaximized && HitTestResizeBorder(mouseX, mouseY) != ResizeBorder.None)
            return true;

        return mouseX >= bounds.X && mouseX < bounds.X + bounds.Width && mouseY >= bounds.Y && mouseY < bounds.Y + bounds.Height;
    }


    public bool IconHitTest(int mouseX, int mouseY)
    {
        if (!hasTitleBar || Icon == null) return false;
        int iconX = bounds.X + Palette.BorderSize + 2;
        int iconY = bounds.Y + Math.Max(2, (Palette.TitleBarHeight - 16) / 2);
        return mouseX >= iconX && mouseX <= iconX + 22 && mouseY >= iconY && mouseY <= iconY + 22;
    }

    public bool TitleHitTest(int mouseX, int mouseY)
    {
        int titleHeight = hasTitleBar && titlebar != null ? titlebar.Height : Palette.TitleBarHeight;
        return mouseX >= bounds.X && mouseX < bounds.X + bounds.Width && mouseY >= bounds.Y && mouseY < bounds.Y + titleHeight;
    }

    public ResizeBorder HitTestResizeBorder(int mouseX, int mouseY)
    {
        if (!canResize || isMaximized || isMinimized || !Visible) return ResizeBorder.None;

        int margin = Math.Max(6, resizeMargin);
        int left = bounds.X;
        int right = bounds.Right;
        int top = bounds.Y;
        int bottom = bounds.Bottom;

        if (mouseX < left - margin || mouseX > right + margin ||
            mouseY < top - margin || mouseY > bottom + margin)
            return ResizeBorder.None;

        bool nearLeft = Math.Abs(mouseX - left) <= margin;
        bool nearRight = Math.Abs(mouseX - right) <= margin;
        bool nearTop = Math.Abs(mouseX - top) <= margin;
        bool nearBottom = Math.Abs(mouseX - bottom) <= margin;

        ResizeBorder border = ResizeBorder.None;
        if (nearLeft) border |= ResizeBorder.Left;
        else if (nearRight) border |= ResizeBorder.Right;

        if (nearTop) border |= ResizeBorder.Top;
        else if (nearBottom) border |= ResizeBorder.Bottom;

        return border;
    }

    public bool ResizeHitTest(int mouseX, int mouseY)
    {
        return HitTestResizeBorder(mouseX, mouseY) != ResizeBorder.None;
    }

    public static Windose.Drivers.CursorType GetCursorForBorder(ResizeBorder border)
    {
        return border switch
        {
            ResizeBorder.Left or ResizeBorder.Right => Windose.Drivers.CursorType.SizeWE,
            ResizeBorder.Top or ResizeBorder.Bottom => Windose.Drivers.CursorType.SizeNS,
            ResizeBorder.TopLeft or ResizeBorder.BottomRight => Windose.Drivers.CursorType.SizeNWSE,
            ResizeBorder.TopRight or ResizeBorder.BottomLeft => Windose.Drivers.CursorType.SizeNESW,
            _ => Windose.Drivers.CursorType.Arrow
        };
    }


    private void Move(int x, int y)
    {
        Rectangle oldBounds = bounds;

        X = x;
        Y = y;

        bounds.X = X;
        bounds.Y = Y;
        bounds.Width = Width;
        bounds.Height = Height;


        WindowManager.Invalidate(oldBounds);
        WindowManager.Invalidate(bounds);
        MarkDirty();
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        bounds.Width = Width;
        bounds.Height = Height;
        if (hasTitleBar && titlebar != null)
        {
            int border = Palette.BorderSize;
            titlebar.SetBounds(border, 0, Math.Max(1, Width - border * 2), Palette.TitleBarHeight);
        }
        OnWindowResized();
    }

    protected virtual void OnWindowResized()
    {
    }

    private void Resize(int x, int y, int width, int height)
    {
        Rectangle oldBounds = bounds;

        X = x;
        Y = y;
        Resize(width, height);

        bounds.X = X;
        bounds.Y = Y;
        bounds.Width = Width;
        bounds.Height = Height;

        WindowManager.Invalidate(oldBounds);
        WindowManager.Invalidate(bounds);

        MarkDirty();
    }

    /// <summary>Resize the window so its client area becomes w x h (frame and title bar added around it).</summary>
    public void SetClientSize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        Resize(bounds.X, bounds.Y,
               width + (drawFrame ? Palette.BorderSize * 2 : 0),
               height + (hasTitleBar ? Palette.TitleBarHeight : 0) + (drawFrame ? Palette.BorderSize : 0));
    }

    internal void ApplyBounds(Rectangle rect)
    {
        Rectangle oldBounds = bounds;

        X = rect.X;
        Y = rect.Y;
        if (rect.Width != Width || rect.Height != Height)
            Resize(rect.Width, rect.Height);

        bounds.X = X;
        bounds.Y = Y;
        bounds.Width = Width;
        bounds.Height = Height;

        WindowManager.Invalidate(oldBounds);
        WindowManager.Invalidate(bounds);
        MarkDirty(false);
    }

    public void Minimize() => WindowManager.Minimize(this);
    public void Maximize()
    {
        if (!isMaximized && canMaximize)
            WindowManager.ToggleMaximize(this);
    }
    public void Restore()
    {
        if (isMinimized)
            WindowManager.Activate(this);
        else if (isMaximized)
            WindowManager.ToggleMaximize(this);
        else if (isSnapped)
            SnapTo(2);
    }
    public int State
    {
        get
        {
            if (isMinimized) return 1;
            if (isMaximized) return 2;
            if (isSnapped) return 3;
            return 0;
        }
    }

    internal void MinimizeWindow()
    {
        if (isMinimized) return;

        isMinimized = true;
        Visible = false;
    }

    internal void RestoreFromTaskbar()
    {
        if (!isMinimized) return;

        isMinimized = false;
        Visible = true;
        MarkDirty();
    }

    internal void ToggleMaximized(Rectangle workArea)
    {
        if (!canMaximize) return;

        if (isMinimized)
            RestoreFromTaskbar();

        if (isMaximized)
        {
            isMaximized = false;
            Resize(restoreBounds.X, restoreBounds.Y, restoreBounds.Width, restoreBounds.Height);
            return;
        }

        restoreBounds = bounds;
        isMaximized = true;
        Resize(workArea.X, workArea.Y, workArea.Width, workArea.Height);
    }


    public void Stop()
    {
        Visible = false;
        WindowManager.Invalidate(bounds);
        Dispose();
    }


    bool disposed = false;

    public override void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        dragging = false;
        resizing = false;

        focusedComponent = null;
        titlebar = null;

        if (process != null && process.Running)
        {
            process.Running = false;
            ProcessManager.QueueStop(process);
        }

        children.Clear();
        base.Dispose();
    }

    public override string GetComponentName() => "Window";

    public void RequestClose()
    {
        if (closeToTray)
        {
            HideToTray();
            return;
        }
        if (closeRequested) return;
        closeRequested = true;
        if (trayIconComponent != null)
        {
            Tray.RemoveTrayIcon(trayIconComponent);
            trayIconComponent = null;
        }
        ProcessManager.QueueStop(process);
    }

    public void HideToTray(string? tooltip = null, Image? customIcon = null)
    {
        isInTray = true;

        if (trayIconComponent == null)
        {
            Image? iconImg = customIcon ?? Icon;
            if (iconImg == null)
            {
                try
                {
                    if (File.Exists("/mnt/System/Icons/application_hammer_grouppol.png"))
                        iconImg = new Png("/mnt/System/Icons/application_hammer_grouppol.png");
                }
                catch { }
            }
            Button trayBtn = new Button(iconImg, 0, 0, 32, 32);

            trayIconMenu = new MenuPopup(130, 72);
            trayIconMenu.AddItem("Restore", () => RestoreFromTray());
            trayIconMenu.AddItem("Hide", () => HideToTray());
            trayIconMenu.AddSeparator();
            trayIconMenu.AddItem("Exit", () => ForceClose());

            trayBtn.leftClickAction = () =>
            {
                if (Visible && !isMinimized && WindowManager.focusedWindow == this)
                {
                    HideToTray();
                }
                else
                {
                    RestoreFromTray();
                }
            };

            string tip = tooltip ?? text ?? "Application";
            Tray.AddTrayIcon(trayBtn, trayIconMenu, null, tip);
            trayIconComponent = trayBtn;
        }

        Visible = false;
        WindowManager.Invalidate(bounds);
        WindowManager.SetTaskbarButtonVisible(this, false);

        if (WindowManager.focusedWindow == this)
        {
            WindowManager.ClearFocusedWindow();
            WindowManager.FocusTopVisibleWindow(this);
        }
    }

    public void RestoreFromTray()
    {
        isInTray = false;
        Visible = true;

        if (isMinimized)
        {
            RestoreFromTaskbar();
        }

        if (showInTaskbar)
        {
            WindowManager.SetTaskbarButtonVisible(this, true);
        }

        WindowManager.Activate(this);
        WindowManager.Invalidate(bounds);
        MarkDirty();
    }

    public void ForceClose()
    {
        closeToTray = false;
        if (trayIconComponent != null)
        {
            Tray.RemoveTrayIcon(trayIconComponent);
            trayIconComponent = null;
        }
        RequestClose();
    }
}
