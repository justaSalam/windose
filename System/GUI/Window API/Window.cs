using System.Drawing;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;
using Windose;

public class Window : Component
{
    public Rectangle bounds; //Window viewport, relative to the screen
    public Rectangle content; //Content rect, relative to the window
    private bool isCached;

    private bool inFocus;

    private bool dragging;
    private bool resizing;
    private bool isMinimized;
    private bool isMaximized;
    private Rectangle restoreBounds;
    private Rectangle savedRestoreBounds;
    private bool isAnimatingBounds;

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

    private Panel titlebar;
    private bool hasTitleBar;
    private bool windowFocused;


    public Window(int x, int y, int width, int height, string title, bool useTitleBar = false) : base(x, y, width, height)
    {
        text = title;
        bounds = new Rectangle(x, y, width, height);
        zLayer = DrawLayer.Window;

        TitlebarSetup(useTitleBar, title);
    }


    public void Start()
    {

    }

    public override void Update()
    {
        // Window movement is handled by the compositor; hover state is for child controls.

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
        if (Palette.FlatControls)
        {
            DrawFilledRectangle(Palette.WindowBackground, 0, 0, Width, Height);
            DrawRectangle(Palette.WindowBorder, 0, 0, Width, Height);
        }
        else
        {
            DrawRaisedRectangle(0, 0, Width, Height);
        }

        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            DrawChild(child);
        }
    }


    private void TitlebarSetup(bool useTitleBar, string title)
    {
        if (useTitleBar)
        {
            int border = Palette.BorderSize;
            int titleHeight = Palette.TitleBarHeight;
            titlebar = new Panel(Palette.InactiveTitle, border, border, Width - border * 2, titleHeight)
            {
                useBackground = true,
                textColor = Palette.TitleTextInactive,
                clampSize = false,
                text = title,
                fontSize = 16,
                horizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(border, border, border, border)
            };
            AddChild(titlebar);
            hasTitleBar = true;

            int titleButtonSize = Math.Max(20, titleHeight - 5);
            int titleButtonTop = Math.Max(2, border + 1);
            Color chromeBorder = Palette.FlatControls ? Palette.WindowBorder : Palette.ControlHighlight;

            AddChild(new Button(0, 0, titleButtonSize, titleButtonSize)
            {
                text = "X",
                verticalAlignment = VerticalAlignment.Top,
                horizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(3, titleButtonTop, 3, 3),
                useBorders = true,
                borderColor = chromeBorder,
                textColor = Palette.FlatControls ? Palette.ControlBlack : Palette.ControlBlack,
                leftMouseRelease = () =>
                {
                    WindowManager.PostClose(this);
                }
            });

            if (canMaximize)
            {
                AddChild(new Button(25, 0, titleButtonSize, titleButtonSize)
                {
                    text = "O",
                    verticalAlignment = VerticalAlignment.Top,
                    horizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(3, titleButtonTop, 3 + titleButtonSize, 3),
                    useBorders = true,
                    borderColor = chromeBorder,
                    textColor = Palette.ControlBlack,
                    leftMouseRelease = () =>
                    {
                        WindowManager.ToggleMaximize(this);
                    }
                });

            }
            if (canMinimize)
            {
                AddChild(new Button(50, 0, titleButtonSize, titleButtonSize)
                {
                    text = "_",
                    verticalAlignment = VerticalAlignment.Top,
                    horizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(3, titleButtonTop, 3 + titleButtonSize * 2, 3),
                    useBorders = true,
                    borderColor = chromeBorder,
                    textColor = Palette.ControlBlack,
                    leftMouseRelease = () =>
                    {
                        WindowManager.Minimize(this);
                    }
                });
            }
        }

    }

    public void ApplyThemeStyle()
    {
        ApplyTitlebarTheme();
        MarkDirty();
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


            focusedComponent = GetChildAt(mouseX, mouseY);
        }

        if (focusedComponent != null && focusedComponent.HandleInput(mouseX, mouseY, mouseState))
            return true;

        DragWindow(mouseState, mouseX, mouseY);
        ResizeWindow(mouseState, mouseX, mouseY);

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
        if (isMinimized || IsAnimatingBounds) return false;

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
        if (isAnimatingBounds)
        {
            ApplyAnimatedBounds(new Rectangle(x, y, Width, Height));
            return;
        }

        Rectangle oldBounds = bounds;

        X = x;
        Y = y;
        bounds = new Rectangle(X, Y, Width, Height);

        WindowManager.Invalidate(oldBounds);
        WindowManager.Invalidate(bounds);
        MarkDirty();
    }

    private void Resize(int x, int y, int width, int height)
    {
        if (isAnimatingBounds)
        {
            ApplyAnimatedBounds(new Rectangle(x, y, width, height));
            return;
        }

        Rectangle oldBounds = bounds;

        X = x;
        Y = y;
        base.Resize(width, height);
        bounds = new Rectangle(X, Y, Width, Height);

        WindowManager.Invalidate(oldBounds);
        WindowManager.Invalidate(bounds);
        MarkDirty();
    }

    internal void ApplyAnimatedBounds(Rectangle rect)
    {
        Rectangle oldBounds = bounds;

        X = rect.X;
        Y = rect.Y;
        if (rect.Width != Width || rect.Height != Height)
            base.Resize(rect.Width, rect.Height);

        bounds = new Rectangle(X, Y, Width, Height);
        ComputeAbsoluteCoordinates();

        WindowManager.Invalidate(oldBounds);
        WindowManager.Invalidate(bounds);
        MarkDirty(false);
    }

    internal void RememberBoundsForRestore()
    {
        savedRestoreBounds = bounds;
    }

    internal Rectangle GetSavedRestoreBounds()
    {
        if (savedRestoreBounds.Width > 0 && savedRestoreBounds.Height > 0)
            return savedRestoreBounds;

        return bounds;
    }

    internal void BeginRestoreAnimation(Rectangle startBounds)
    {
        isMinimized = false;
        Visible = true;
        isAnimatingBounds = true;
        ApplyAnimatedBounds(startBounds);
    }

    internal void EndBoundsAnimation()
    {
        isAnimatingBounds = false;
    }

    internal void FinishMinimize()
    {
        isAnimatingBounds = false;
        MinimizeWindow();
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

    public bool IsMinimized => isMinimized;
    public bool IsMaximized => isMaximized;
    public bool IsAnimatingBounds => isAnimatingBounds || AnimationManager.IsAnimating(this);
    public void Stop() //TODO Dispose, GC wont collect it without proper disposal first
    {
        Visible = false;
        Dispose();
    }

    public override void Dispose()
    {
        base.Dispose();
    }

    public override string GetName() => "Window";

}
