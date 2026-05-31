using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;

public class Window : Process
{
    private WindowManager windowManager;
    private Canvas canvas;
    public Rectangle bounds;
    private Canvas buffer;
    public int zIndex;
    public int resizeMargin = 10;

    private bool inFocus;
    public Window(Canvas canvas)
    {
        this.canvas = canvas;
    }

    private bool dragging;
    private bool resizing;
    private Point offset;
    private Point resizeStart;
    private Rectangle original;

    public void HandleInput(int mouseX, int mouseY, MouseLeftEvent mouseLeft, MouseRightEvent mouseRight, MouseMiddleEvent mouseMiddle)
    {
        if (mouseLeft == MouseLeftEvent.Press)
        {
            inFocus = HitTest(mouseX, mouseY);
        }
        DragWindow(mouseLeft, mouseX, mouseY);
        ResizeWindow(mouseLeft, mouseX, mouseY);



        if (mouseRight == MouseRightEvent.Release)
        {
            windowManager.Close(this);
        }
    }

    private void DragWindow(MouseLeftEvent mouseLeft, int mouseX, int mouseY)
    {
        if (mouseLeft == MouseLeftEvent.Hold && !dragging && TitleHitTest(mouseX, mouseY))
        {
            dragging = true;
            offset = new Point(mouseX - bounds.X, mouseY - bounds.Y);
        }
        else if (mouseLeft == MouseLeftEvent.None && dragging)
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

    private void ResizeWindow(MouseLeftEvent mouseLeft, int mouseX, int mouseY)
    {
        if (mouseLeft == MouseLeftEvent.Hold && !dragging && ResizeHitTest(mouseX, mouseY))
        {
            resizing = true;
            resizeStart = new Point(mouseX, mouseY);
            original.Width = bounds.Width;
            original.Height = bounds.Height;
        }
        else if (mouseLeft == MouseLeftEvent.None && resizing)
        {
            resizing = false;
        }

        if (resizing) //Resize from bottom right corner TODO: Add resizing from other corners and edges
        {
            int newWidth = original.Width + (mouseX - resizeStart.X);
            int newHeight = original.Height + (mouseY - resizeStart.Y);

            if (newWidth > 200 && newHeight > 100) //TODO: each window should have its own clamp size
            {
                bounds = new Rectangle(bounds.X, bounds.Y, newWidth, newHeight);
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



    public override void Start()
    {
        windowManager = ProcessManger.GetProcess<WindowManager>();
        if (windowManager == null)
        {
            //throw;
        }
        Name = "WND";
        base.Start();
    }

    public override void Update()
    {
        DrawWindow();
    }
    private void DrawWindow()
    {
        canvas.DrawFilledRectangle(Color.LightGray, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        canvas.DrawString("WINDOW", PCScreenFont.DefaultFont, Color.Black, bounds.X, bounds.Y);
    }

    public override void Stop() ///TODO Dispose
    {
        Running = false;
    }
}