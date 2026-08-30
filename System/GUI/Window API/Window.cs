using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using Windose.System.System_Calls;

public class Window : Component
{
    public Rectangle bounds; //Window viewport, relative to the screen

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




    public int resizeMargin = 10;

    private Point offset;
    private Point resizeStart;
    private Rectangle original;
    private Rectangle previewBounds;

    private Component? focusedComponent;

    private Png? Icon;
    private StackPanel titlebar;
    private bool hasTitleBar;
    private bool windowFocused;


    protected SingleThreadedProcess process;

    //TODO Process manager should be responsible for every update instead of window manager being a separate subsystem
    public Window(int x, int y, int width, int height, string title, bool useTitleBar = false, Png? icon = null) : base(x, y, width, height)
    {
        text = title;
        Icon = icon;
        bounds = new Rectangle(x, y, width, height);
        zLayer = DrawLayer.Window;

        process = new SingleThreadedProcess(title, ProcessType.Program);

        process.onDispose += () => WindowManager.PostClose(this);
        process.onUpdate += Update;
        process.onStart += () => WindowManager.PostRegister(this);


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
    public override void Draw()
    {
        base.Draw();
    }

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
            DrawImageStretch(Icon, new(2, 2, 18, 18));
        }
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
                ProcessManger.QueueStop(process);
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



    public override bool HandleInput(int mouseX, int mouseY, MouseState mouseState)
    {
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
        if (focusedComponent != null) focusedComponent.HandleKeyboard(keyEvent);
        else base.HandleKeyboard(keyEvent);
    }

    public bool FocusCheck(int mouseX, int mouseY, MouseState mouseState)
    {
        return HitTest(mouseX, mouseY) && mouseState.left == MouseEvents.Press;
    }

    private void DragWindow(MouseState mouse, int mouseX, int mouseY)
    {
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

        ComputeAbsoluteCoordinates();


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

        ComputeAbsoluteCoordinates();

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

}
