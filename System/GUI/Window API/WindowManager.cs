using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.System.Mouse;
using Windose;

public class WindowManager : SingleThreadedProcess
{
    public List<Window> windows = new List<Window>();
    private Window? focused;
    private MouseState mouseState;
    private int nextZIndex = 1;

    private static readonly Comparison<Window> zIndexCompare = (a, b) => a.zIndex.CompareTo(b.zIndex);
    private int mx, my;

    public WindowManager() : base("Desktop Window Manager", ProcessType.Kernel)
    {

    }

    public override void Update()
    {
        mx = MouseManager.X;
        my = MouseManager.Y;
        mouseState = Mouse.state;


        //Draw Screen
        foreach (Component component in Component.components)
        {
            if (component.IsDirty() || component.forceDirty)
            {
                if (!component.Visible) return;

                component.Draw();
                component.MarkCleaned();
            }
        }

        //Draw Applications
        foreach (Window window in windows)
        {
            if (window.IsDirty() || window.forceDirty)
            {
                if (!window.Visible) return;

                window.Draw();
                window.MarkCleaned();
            }
        }

        /*

        windows.Sort(zIndexCompare);

        for (int i = 0; i < windows.Count; i++)
        {
            if (windows[i] == null) continue;
            windows[i].Update();
        }

        for (int i = windows.Count - 1; i >= 0; i--)
        {
            if (windows[i] == null) continue;

            windows[i].HandleInput(mx, my, mouseState);

            break;
        }

        for (int i = windows.Count - 1; i >= 0; i--)
        {
            if (windows[i] == null) continue;
            if (mouseState.left == MouseEvents.Press && windows[i].HitTest(mx, my))
            {
                BringToFront(windows[i]);
                break;
            }
        }

*/

    }
    public void Register(Window window)
    {
        try
        {
            window.zIndex = nextZIndex;
            window.Start();
            windows.Add(window);
            nextZIndex++;
        }
        catch (Exception ex)
        {
            Serial.WriteString(ex.Message);
        }


    }

    public void Close(Window window)
    {
        window.Stop();
        windows.Remove(window);
    }

    public void BringToFront(Window window)
    {
        window.zIndex = nextZIndex++;
    }
    public override void Dispose()
    {
        windows = null;
        base.Dispose();
    }
}