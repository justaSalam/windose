using System.Drawing;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;

public class Window : Component
{
    private Canvas canvas; //Fullscreen buffer
    public Rectangle bounds; //Window viewport, relative to the screen
    public Rectangle content; //Content rect, relative to the window
    public int zIndex;
    public int resizeMargin = 10;

    private bool inFocus;

    private bool dragging;
    private bool resizing;
    private bool isCached;
    private Point offset;
    private Point resizeStart;
    private Rectangle original;

    public Window(int x, int y, int width, int height) : base(x, y, width, height)
    {

    }

    public void Start()
    {
        content = new Rectangle(0, 0, bounds.Width, bounds.Height);
    }

    public void Update() //Window Logic
    {
        Compose();
    }
    private void Compose()
    {
        try
        {

        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }


    public void Stop() //TODO Dispose, GC wont collect it without proper disposal first
    {

    }


    public void HandleInput(int mouseX, int mouseY, MouseState mouseState)
    {
        if (mouseState.left == MouseEvents.Press)
        {
            inFocus = HitTest(mouseX, mouseY);
        }
        DragWindow(mouseState, mouseX, mouseY);
        ResizeWindow(mouseState, mouseX, mouseY);

        if (Mouse.state.right == MouseEvents.Release)
        {
            Stop();
        }
    }

    public bool FocusCheck(int mouseX, int mouseY, MouseState mouseState)
    {
        return HitTest(mouseX, mouseY) && mouseState.left == MouseEvents.Press;
    }

    private void DragWindow(MouseState mouse, int mouseX, int mouseY)
    {
        if (resizing) return;

        if (mouse.left == MouseEvents.Hold && !dragging && TitleHitTest(mouseX, mouseY))
        {
            dragging = true;
            offset = new Point(mouseX - bounds.X, mouseY - bounds.Y);
        }
        else if (mouse.left == MouseEvents.None && dragging)
        {
            dragging = false;
        }

        if (dragging)
        {
            bounds = new Rectangle(
                mouseX - offset.X,
                mouseY - offset.Y,
                bounds.Width,
                bounds.Height);
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
        bounds = new Rectangle(x, y, width, height);
    }


}