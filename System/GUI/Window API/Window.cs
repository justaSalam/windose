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

    private Point offset;
    private Point resizeStart;
    private Rectangle original;
    private Rectangle previewBounds;
    private int lastTitlePress;

    protected Component? focusedComponent;

    public Png? Icon;
    private StackPanel titlebar;
    private bool hasTitleBar;
    private bool windowFocused;


    protected SingleThreadedProcess process;

    public Action? onClosed;                 // fired after the window's process is disposed
    public bool closeRequested;

    //TODO Process manager should be responsible for every update instead of window manager being a separate subsystem
    public Window(int x, int y, int width, int height, string title, bool useTitleBar = false, Png? icon = null) : base(x, y, width, height)
    {
        canRender = true;
        text = title;
        Icon = icon;
        bounds = new Rectangle(x, y, width, height);
        zLayer = DrawLayer.Window;

        process = new SingleThreadedProcess(title, ProcessType.Program);

        //process.onStart += () => WindowManager.PostRegister(this);
        process.onDispose += () => 
        {
            WindowManager.PostClose(this); 
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
        ProcessManger.QueueStart(process);
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
    public override void DrawLocal()
    {
        if (useGlass) DrawGlassFrame();
        else DrawRaisedRectangle(0, 0, Width, Height);


        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            DrawChild(child);
        }

        if (Icon != null)
        {
            DrawImageStretch(Icon, new(WindowFrameInset + 2, WindowFrameInset + 2, 16, 16));
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
                Margin = new Thickness(border, 0, border, 0),
                Padding = new Thickness(0),
                spacing = 1
            };

            if (Icon != null) titlebar.textOffsetX = 25;

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

        MarkDirty();
    }



    public override bool HandleInput(int mouseX, int mouseY, MouseState mouseState)
    {
        if (!IsInputEnabled()) return false;
        if (mouseState.right == MouseEvents.Press && TitleHitTest(mouseX, mouseY) && !(GetChildAt(mouseX, mouseY) is Button))
        {
            ShowSystemMenu(mouseX, mouseY);
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
        ResizeWindow(mouseState, mouseX, mouseY);

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

        if (resizing || !canMove || isMaximized) return;

        if (mouse.left == MouseEvents.Press && TitleHitTest(mouseX, mouseY))
        {
            dragging = true;
            offset = new Point(mouseX - bounds.X, mouseY - bounds.Y);
            previewBounds = bounds;
            WindowManager.ShowPreviewRect(previewBounds);
        }

        else if (mouse.left == MouseEvents.Release && dragging)
        {
            dragging = false;
            WindowManager.ClearPreviewRect();
            if (!TrySnapToEdge(previewBounds))
                Move(previewBounds.X, previewBounds.Y);
        }
        else if (mouse.left == MouseEvents.None && dragging)
        {
            dragging = false;
            WindowManager.ClearPreviewRect();
        }

        if (dragging && (mouse.left == MouseEvents.Hold || mouse.left == MouseEvents.Press))
        {
            previewBounds = new Rectangle(mouseX - offset.X, mouseY - offset.Y, Width, Height);
            WindowManager.ShowPreviewRect(previewBounds);
        }
    }

    private static Rectangle WorkArea()
    {
        int h = Explorer.taskbar != null ? Explorer.taskbar.Y : Global.screenHeight;
        return new Rectangle(0, 0, Global.screenWidth, h);
    }

    /// <summary>Snap a dropped window to a screen edge. True when snapped.</summary>
    private bool TrySnapToEdge(Rectangle dropped)
    {
        if (!canResize && !canMaximize) return false;
        const int zone = 8;
        Rectangle work = WorkArea();
        if (dropped.Y <= work.Y + zone && canMaximize)
        {
            WindowManager.ToggleMaximize(this);
            return true;
        }
        if (dropped.X <= work.X + zone && canResize)
        {
            ApplyBounds(new Rectangle(work.X, work.Y, work.Width / 2, work.Height));
            return true;
        }
        if (dropped.X + dropped.Width >= work.Right - zone && canResize)
        {
            ApplyBounds(new Rectangle(work.X + work.Width / 2, work.Y, work.Width - work.Width / 2, work.Height));
            return true;
        }
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
        Rectangle work = WorkArea();
        if (mode == 0)
            ApplyBounds(new Rectangle(work.X, work.Y, work.Width / 2, work.Height));
        else
            ApplyBounds(new Rectangle(work.X + work.Width / 2, work.Y, work.Width - work.Width / 2, work.Height));
    }

    private void ResizeWindow(MouseState mouseState, int mouseX, int mouseY)
    {
        if (dragging || !canResize || isMaximized) return;

        if (mouseState.left == MouseEvents.Press && ResizeHitTest(mouseX, mouseY))
        {
            resizing = true;
            resizeStart = new Point(mouseX, mouseY);
            original = bounds;
            previewBounds = bounds;
            WindowManager.ShowPreviewRect(previewBounds);
        }
        else if (mouseState.left == MouseEvents.Release && resizing)
        {
            resizing = false;
            WindowManager.ClearPreviewRect();
            Resize(previewBounds.X, previewBounds.Y, previewBounds.Width, previewBounds.Height);
        }
        else if (mouseState.left == MouseEvents.None && resizing)
        {
            resizing = false;
            WindowManager.ClearPreviewRect();
        }

        if (resizing && (mouseState.left == MouseEvents.Hold || mouseState.left == MouseEvents.Press))
        {
            int newWidth = original.Width + (mouseX - resizeStart.X);
            int newHeight = original.Height + (mouseY - resizeStart.Y);

            if (newWidth > 2 && newHeight > 2)
            {
                previewBounds = new Rectangle(original.X, original.Y, newWidth, newHeight);

                WindowManager.ShowPreviewRect(previewBounds);
            }
        }
    }

    private void ShowSystemMenu(int mouseX, int mouseY)
    {
        MenuPopup menu = new MenuPopup(180, 24);
        if (canMinimize)
        {
            MenuItem min = menu.AddItem("Minimize");
            min.click = () => WindowManager.Minimize(this);
        }
        if (canMaximize)
        {
            MenuItem max = menu.AddItem(isMaximized ? "Restore" : "Maximize");
            max.click = () => WindowManager.ToggleMaximize(this);
        }
        MenuItem close = menu.AddItem("Close");
        close.click = () => RequestClose();
        menu.ShowAt(mouseX, Math.Min(mouseY, Global.screenHeight - 120));
    }

    public virtual bool HitTest(int mouseX, int mouseY)
    {
        if (isMinimized) return false;

        return mouseX >= bounds.X && mouseX < bounds.X + bounds.Width && mouseY >= bounds.Y && mouseY < bounds.Y + bounds.Height;
    }

    public bool TitleHitTest(int mouseX, int mouseY)
    {
        int titleHeight = hasTitleBar && titlebar != null ? titlebar.Height : Palette.TitleBarHeight;
        return mouseX >= bounds.X && mouseX < bounds.X + bounds.Width && mouseY >= bounds.Y && mouseY < bounds.Y + titleHeight;
    }
    public bool ResizeHitTest(int mouseX, int mouseY)
    {
        return mouseX >= bounds.X + bounds.Width - resizeMargin && mouseX < bounds.X + bounds.Width && mouseY >= bounds.Y + bounds.Height - resizeMargin && mouseY < bounds.Y + bounds.Height;
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

    private void Resize(int x, int y, int width, int height)
    {
        Rectangle oldBounds = bounds;

        X = x;
        Y = y;
        base.Resize(width, height);

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
               width + Palette.BorderSize * 2,
               height + Palette.TitleBarHeight + Palette.BorderSize);
    }

    internal void ApplyBounds(Rectangle rect)
    {
        Rectangle oldBounds = bounds;

        X = rect.X;
        Y = rect.Y;
        if (rect.Width != Width || rect.Height != Height)
            base.Resize(rect.Width, rect.Height);

        bounds.X = X;
        bounds.Y = Y;
        bounds.Width = Width;
        bounds.Height = Height;

        WindowManager.Invalidate(oldBounds);
        WindowManager.Invalidate(bounds);
        MarkDirty(false);
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


        children.Clear();
        base.Dispose();
    }

    public override string GetComponentName() => "Window";

    public void RequestClose()
    {
        if (closeRequested) return;
        closeRequested = true;
        ProcessManger.QueueStop(process);
    }
}
