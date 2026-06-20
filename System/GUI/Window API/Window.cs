using System.Drawing;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;

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

    public bool canMaximize = true;
    public bool canMinimize = true;
    public bool canResize = true;
    public bool canMove = true;




    public int resizeMargin = 10;

    private Point offset;
    private Point resizeStart;
    private Rectangle original;
    private Rectangle previewBounds;

    private Component? focusedComponent;

    private Panel titlebar;
    private bool hasTitleBar;


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
        DrawRaisedRectangle(0, 0, Width, Height);



        //DrawRectangle(Palette.ControlHighlight, 0, 0, Width, Height);

        foreach (Component child in children)
        {
            if (!child.Visible) continue;


            child.DrawLocal();
            DrawChild(child);
            child.MarkCleaned();
        }
    }


    private void TitlebarSetup(bool useTitleBar, string title)
    {
        if (useTitleBar)
        {
            titlebar = new Panel(Palette.InactiveTitle, 2, 2, Width - 4, 25)
            {
                useBackground = true,
                textColor = Color.White,
                clampSize = false,
                text = title,
                fontSize = 16,
                horizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(2, 2, 2, 2)
            };
            AddChild(titlebar);
            hasTitleBar = true;

            AddChild(new Button(0, 0, 20, 20)
            {
                text = "X",
                verticalAlignment = VerticalAlignment.Top,
                horizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(3, 3, 3, 3),
                useBorders = true,
                borderColor = Color.White,
                leftMouseRelease = () =>
                {
                    WindowManager.PostClose(this);
                }
            });

            if (canMaximize)
            {
                AddChild(new Button(25, 0, 20, 20)
                {
                    text = "O",
                    verticalAlignment = VerticalAlignment.Top,
                    horizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(3, 3, 23, 3),
                    useBorders = true,
                    borderColor = Color.White,
                    leftMouseRelease = () =>
                    {
                        WindowManager.ToggleMaximize(this);
                    }
                });

            }
            if (canMinimize)
            {
                AddChild(new Button(50, 0, 20, 20)
                {
                    text = "_",
                    verticalAlignment = VerticalAlignment.Top,
                    horizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(3, 3, 46, 3),
                    useBorders = true,
                    borderColor = Color.White,
                    leftMouseRelease = () =>
                    {
                        WindowManager.Minimize(this);
                    }
                });
            }
        }

    }



    public override bool HandleInput(int mouseX, int mouseY, MouseState mouseState)
    {
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
        if (!focused) OnLoseFocus();

        if (!hasTitleBar) return;

        Color titlebarColor = focused ? Palette.ActiveTitle : Palette.InactiveTitle;
        if (titlebar.color1 == titlebarColor) return;

        titlebar.color1 = titlebarColor;
        titlebar.MarkDirty();

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
        bounds = new Rectangle(X, Y, Width, Height);

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
        bounds = new Rectangle(X, Y, Width, Height);

        WindowManager.Invalidate(oldBounds);
        WindowManager.Invalidate(bounds);
        MarkDirty();
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
