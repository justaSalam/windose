using System.Drawing;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Mouse;
using Windose;

public class WindowManager : SingleThreadedProcess
{
    public List<Window> windows = new List<Window>();
    private static List<Rectangle> dirtyRects = new List<Rectangle>();
    private static bool hasPreviewRect;
    private static Rectangle previewRect;
    private Window? capturedWindow;
    private Window? focusedWindow;
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

        //Sort Components based on zLayer
        components.Sort((component1, component2) =>
        {
            int zLayer = component1.zLayer.CompareTo(component2.zLayer);
            if (zLayer != 0) return zLayer;

            return component1.zIndex.CompareTo(component2.zIndex);
        });
        windows.Sort(zIndexCompare);

        for (int i = 0; i < windows.Count; i++) //General window update, called on every window
        {
            Window win = windows[i];
            if (win == null) continue;
            win.Update();
        }

        if (capturedWindow != null) //Handling a captured window
        {
            capturedWindow.HandleInput(mx, my, mouseState);

            if (mouseState.left == MouseEvents.Release || mouseState.left == MouseEvents.None)
                capturedWindow = null;

            HandleKeyboardInput();
            ComposeDirtyRegions();
            DrawPreviewRect();
            return;
        }

        HandleKeyboardInput();

        for (int i = windows.Count - 1; i >= 0; i--)//Window Capturing
        {
            Window win = windows[i];

            if (win == null || !win.Visible) continue;
            if (!win.HitTest(mx, my)) continue;

            if (mouseState.left == MouseEvents.Press)
            {
                BringToFront(win);
                focusedWindow = win;
                capturedWindow = win;
            }
            if (win.HandleInput(mx, my, mouseState)) break;

        }

        ComposeDirtyRegions();
        DrawPreviewRect();
    }

    private void HandleKeyboardInput()
    {
        if (!KeyboardManager.KeyAvailable) return;

        KeyEvent keyEvent = KeyboardManager.ReadKey();

        if (focusedWindow != null)
            focusedWindow.HandleKeyboard(keyEvent);
    }

    private void ComposeDirtyRegions()
    {
        if (dirtyRects.Count == 0) return;

        for (int i = 0; i < dirtyRects.Count; i++)
        {
            Rectangle dirtyRect = dirtyRects[i];

            foreach (Component component in components)
            {
                if (!component.Visible) continue;
                if (!component.rectangle.IntersectsWith(dirtyRect)) continue;

                if (component.IsDirty() || component.forceDirty)
                {
                    component.Draw();
                    component.MarkCleaned();
                }
                else
                {
                    component.DrawToScreen(dirtyRect);
                }
            }
        }

        dirtyRects.Clear();
    }

    public static void ShowPreviewRect(Rectangle rect)
    {
        if (hasPreviewRect)
            InvalidatePreviewRect(previewRect);

        previewRect = rect;
        hasPreviewRect = true;
        InvalidatePreviewRect(previewRect);
    }

    public static void ClearPreviewRect()
    {
        if (!hasPreviewRect) return;

        InvalidatePreviewRect(previewRect);
        hasPreviewRect = false;
    }

    private static void InvalidatePreviewRect(Rectangle rect)
    {
        Invalidate(new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2));
    }

    private static void DrawPreviewRect()
    {
        if (!hasPreviewRect) return;

        Kernel.mainBuffer.DrawDottedRectangle(Color.White, previewRect.X, previewRect.Y, previewRect.Width, previewRect.Height);
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
        Invalidate(dirty.rectangle);
    }

    public static void Invalidate(Rectangle dirtyRect)
    {
        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0) return;

        for (int i = 0; i < dirtyRects.Count; i++)
        {
            if (dirtyRects[i].Equals(dirtyRect)) return;
            if (!dirtyRects[i].IntersectsWith(dirtyRect)) continue;

            dirtyRect = Rectangle.Union(dirtyRects[i], dirtyRect);
            dirtyRects.RemoveAt(i);
            i--;
        }

        dirtyRects.Add(dirtyRect);
    }

    public void BringToFront(Window window)
    {
        window.zIndex = nextZIndex++;
        Invalidate(window);
    }
    public override void Dispose()
    {
        windows = null;
        base.Dispose();
    }
}
