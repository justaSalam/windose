using System.Drawing;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.System.Mouse;
using Windose;

public class WindowManager : SingleThreadedProcess
{
    public List<Window> windows = new List<Window>();
    private static List<Component> dirtyComponents = new List<Component>();
    private Window? focused;
    private MouseState mouseState;
    private int nextZIndex = 1;

    private static readonly Comparison<Window> zIndexCompare = (a, b) => a.zIndex.CompareTo(b.zIndex);
    private int mx, my;

    public WindowManager() : base("Desktop Window Manager", ProcessType.Kernel)
    {
        components = Component.components;
    }

    private List<Component> components;

    public override void Update()
    {
        mx = MouseManager.X;
        my = MouseManager.Y;
        mouseState = Mouse.state;

        //Sort Components 
        components.Sort((component1, component2) =>
        {
            int zLayer = component1.zLayer.CompareTo(component2.zLayer);
            if (zLayer != 0) return zLayer;

            return component1.zIndex.CompareTo(component2.zIndex);
        });


        foreach (Component component in components)
        {
            if (component.IsDirty() || component.forceDirty)
            {
                if (!component.Visible) continue;


                component.Draw();
                component.MarkCleaned();
            }

        }


        dirtyComponents.Clear();

        //Draw Screen

        for (int i = 0; i < windows.Count; i++)
        {
            Window win = windows[i];
            if (win == null) continue;
            win.Update();
        }

        for (int i = windows.Count - 1; i >= 0; i--)
        {
            Window win = windows[i];

            if (win == null || !win.Visible) continue;

            win.HandleInput(mx, my, mouseState);

            break;
        }
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

    public static void Invalidate(Component dirty)
    {
        if (!dirtyComponents.Contains(dirty)) dirtyComponents.Add(dirty);

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