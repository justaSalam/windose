using System.Drawing;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;

public class Window : Component
{
    private Canvas canvas; //Fullscreen buffer
    public Rectangle bounds; //Window viewport, relative to the screen
    public Rectangle content; //Content rect, relative to the window
    private bool isCached;

    private bool inFocus;

    private bool dragging;
    private bool resizing;
    public int resizeMargin = 10;

    private Point offset;
    private Point resizeStart;
    private Rectangle original;

    private Component? focusedComponent;

    public Window(int x, int y, int width, int height, string title, bool useTitleBar = false) : base(x, y, width, height)
    {
        bounds = new Rectangle(x, y, width, height);
        zLayer = DrawLayer.Window;

        if (useTitleBar)
        {
            AddChild(new Panel(Color.FromArgb(123, 126, 121), 2, 2, width - 4, 25)
            {
                useBorders = false,
                text = title,
                fontSize = 16,
                horizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(2, 2, 2, 2)
            });
        }

        AddChild(new Button(Color.Gray, 0, 0, 100, 30)
        {
            text = "Button",
            useBorders = true,
            horizontalAlignment = HorizontalAlignment.Center,
            verticalAlignment = VerticalAlignment.Center,
            leftMouseRelease = () =>
            {
                Serial.WriteString("button pressed\n");
            }
        });
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
        DrawLocal();
        base.Draw();
    }

    /// <summary>
    /// Component rendering, coordinates relative to the window
    /// </summary>
    public override void DrawLocal()
    {
        DrawFilledRectangle(Color.FromArgb(190, 190, 190), 0, 0, Width, Height);



        DrawRectangle(Color.White, 0, 0, Width, Height);

        foreach (Component child in children)
        {
            if (!child.Visible) continue;

            child.DrawLocal();
            buffer.DrawImageAlpha(child.GetBuffer(), child.X, child.Y);
            child.MarkCleaned();
        }
    }






    public override bool HandleInput(int mouseX, int mouseY, MouseState mouseState)
    {
        if (mouseState.left == MouseEvents.Press)
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
        if (resizing) return;

        if (mouse.left == MouseEvents.Press && TitleHitTest(mouseX, mouseY))
        {
            dragging = true;
            offset = new Point(mouseX - bounds.X, mouseY - bounds.Y);
        }
        else if ((mouse.left == MouseEvents.Release || mouse.left == MouseEvents.None) && dragging)
        {
            dragging = false;

        }

        if (dragging)
        {
            Rectangle oldBounds = bounds;

            X = mouseX - offset.X;
            Y = mouseY - offset.Y;
            bounds = new Rectangle(X, Y, Width, Height);

            WindowManager.Invalidate(oldBounds);
            WindowManager.Invalidate(bounds);
        }
    }

    private void ResizeWindow(MouseState mouseState, int mouseX, int mouseY)
    {
        if (dragging) return;

        if (mouseState.left == MouseEvents.Hold && !dragging && ResizeHitTest(mouseX, mouseY))
        {
            resizing = true;
            resizeStart = new Point(mouseX, mouseY);
            original.Width = bounds.Width;
            original.Height = bounds.Height;
        }
        else if (mouseState.left == MouseEvents.None && resizing)
        {
            resizing = false;
            ResolveChildren();
        }

        if (resizing)
        {
            int newWidth = original.Width + (mouseX - resizeStart.X);
            int newHeight = original.Height + (mouseY - resizeStart.Y);

            if (newWidth > 2 && newHeight > 2)
            {
                Resize(bounds.X, bounds.Y, newWidth, newHeight);
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
    public void Stop() //TODO Dispose, GC wont collect it without proper disposal first
    {

    }

    public override string GetName() => "Window";

}
