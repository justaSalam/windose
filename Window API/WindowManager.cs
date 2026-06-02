using Cosmos.Kernel.System.Mouse;
using Windose;

public class WindowManager : Process
{
    public List<Window> windows = new List<Window>();
    private int nextZIndex = 1;

    private int mx, my;
    public override void Start(int processId)
    {
        Name = "Desktop Window Manager";
        Description = "Window behavior manager";
        base.Start(processId);
    }

    public override void Update()
    {
        mx = MouseManager.X;
        my = MouseManager.Y;

        foreach (Window window in windows.OrderBy(w => w.zIndex))//Draw window
        {
            if (window == null) continue;

            window.Update();


            if (MouseEventHandler.mouseLeft == MouseLeftEvent.Press && window.HitTest(mx, my)) BringToFront(window);
        }
        foreach (Window window in windows.OrderBy(w => w.zIndex))//Pass input
        {
            if (window == null) continue;

            window.HandleInput(mx, my, MouseEventHandler.mouseLeft, MouseEventHandler.mouseRight, MouseEventHandler.mouseMiddle);
            break;
        }

    }
    public void Register(Window window)
    {
        window.zIndex = nextZIndex;
        windows.Add(window);
        nextZIndex++;
    }

    public void Close(Window window)
    {
        windows.Remove(window);
        window.Stop();
    }

    public void BringToFront(Window window)
    {
        window.zIndex = nextZIndex++;
    }
    public override void Stop()
    {

    }
}