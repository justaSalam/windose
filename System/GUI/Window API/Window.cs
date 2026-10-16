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
            base.Update();
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
        DrawRaisedRectangle(0, 0, Width, Height);


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


    private void TitlebarSetup(bool useTitleBar, string title)
    {
        if (useTitleBar)
        {
            int border = Palette.BorderSize;
            int titleHeight = Palette.TitleBarHeight;

            titlebar = new StackPanel(Palette.InactiveTitle, border, border, Width - (border * 2), titleHeight - border)
            {
                useBackground = true,
                textColor = Palette.TitleTextInactive,
                clampSize = false,
                text = title,
                fontSize = 16,
                horizontalAlignment = HorizontalAlignment.Stretch,
                orientation = StackOrientation.Horizontal,
                Margin = new Thickness(border)
            };

            if (Icon != null) titlebar.textOffsetX = 25;

            AddChild(titlebar);
            hasTitleBar = true;

            int titleButtonSize = titleHeight - 4;
            int titleButtonTop = Math.Max(2, border + 2);

            CreateTitleControls(titleButtonSize, titleButtonTop);
        }

    }

    private void CreateTitleControls(int titleButtonSize, int titleButtonTop)
    {
        titlebar.AddStackChild(new Button("x", 0, 4, titleButtonSize, titleButtonSize)
        {
            verticalAlignment = VerticalAlignment.Center,
            horizontalAlignment = HorizontalAlignment.Right,
            useBorders = true,
            borderColor = Palette.ControlHighlight,
            textColor = Palette.ControlWhite,
            leftClickAction = () =>
            {
                RequestClose();
            }
        });

        titlebar.AddStackChild(new Button("o", 25, 4, titleButtonSize, titleButtonSize)
        {
            verticalAlignment = VerticalAlignment.Center,
            horizontalAlignment = HorizontalAlignment.Right,
            useBorders = true,
            borderColor = Palette.ControlHighlight,
            textColor = Palette.ControlWhite,
            leftClickAction = () =>
            {
                WindowManager.ToggleMaximize(this);
            }
        });

        titlebar.AddStackChild(new Button("_", 50, 4, titleButtonSize, titleButtonSize)
        {
            verticalAlignment = VerticalAlignment.Center,
            horizontalAlignment = HorizontalAlignment.Right,
            useBorders = true,
            borderColor = Palette.ControlHighlight,
            textColor = Palette.ControlWhite,
            leftClickAction = () =>
            {
                WindowManager.Minimize(this);
            }
        });
    }

    private void ApplyTitlebarTheme()
    {
        if (!hasTitleBar || titlebar == null) return;


        titlebar.color1 = windowFocused ? Palette.ActiveTitle : Palette.InactiveTitle;

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
        windowFocused = focused;
        if (!focused) OnLoseFocus();

        if (!hasTitleBar) return;

        ApplyTitlebarTheme();
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
        return mouseX >= bounds.X && mouseX < bounds.X + bounds.Width && mouseY >= bounds.Y && mouseY < bounds.Y + 20;
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
