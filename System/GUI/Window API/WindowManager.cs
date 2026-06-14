using System.Drawing;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Mouse;
using Windose;

public class WindowManager : SingleThreadedProcess
{
    public static List<Window> windows = new List<Window>();
    private static List<Rectangle> dirtyRects = new List<Rectangle>();
    private static bool hasPreviewRect;
    private static Rectangle previewRect;
    private Window? capturedWindow;
    private Component? capturedComponent;
    public static Window? focusedWindow;
    private MouseState mouseState;
    private static int nextZIndex = 1;

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

        if (capturedComponent != null)
        {
            capturedComponent.HandleInput(mx, my, mouseState);

            if (mouseState.left == MouseEvents.Release || mouseState.left == MouseEvents.None)
                capturedComponent = null;

            HandleKeyboardInput();
            ComposeDirtyRegions();
            DrawPreviewRect();
            return;
        }

        HandleKeyboardInput();

        bool hitWindow = false;
        bool hitComponent = false;

        for (int i = windows.Count - 1; i >= 0; i--)//Window Capturing
        {
            Window win = windows[i];

            if (win == null || !win.Visible) continue;
            if (!win.HitTest(mx, my)) continue;

            hitWindow = true;

            if (mouseState.left == MouseEvents.Press)
            {
                BringToFront(win);
                SetFocusedWindow(win);
                capturedWindow = win;
            }
            if (win.HandleInput(mx, my, mouseState)) break;

        }

        if (!hitWindow)
            hitComponent = HandleRootComponentInput();

        if (!hitWindow && !hitComponent && mouseState.left == MouseEvents.Press)
            ClearFocusedWindow();

        ComposeDirtyRegions();
        DrawPreviewRect();
    }

    private void HandleKeyboardInput()
    {
        if (!KeyboardManager.KeyAvailable) return;

        KeyEvent keyEvent = KeyboardManager.ReadKey();

        if (focusedWindow != null) focusedWindow.HandleKeyboard(keyEvent);

    }

    private bool HandleRootComponentInput()
    {
        for (int i = components.Count - 1; i >= 0; i--)
        {
            Component component = components[i];

            if (component == null || !component.Visible) continue;
            if (component is Window) continue;
            if (!component.isRoot) continue;
            if (!component.IsInsideAbsolute(mx, my)) continue;

            bool handled = component.HandleInput(mx, my, mouseState);

            if (handled && mouseState.left == MouseEvents.Press)
                capturedComponent = component;

            return handled;
        }

        return false;
    }

    private static void SetFocusedWindow(Window window)
    {
        if (focusedWindow == window) return;

        if (focusedWindow != null) focusedWindow.SetFocused(false);


        focusedWindow = window;
        focusedWindow.SetFocused(true);
    }

    private void ClearFocusedWindow()
    {
        if (focusedWindow == null) return;

        focusedWindow.SetFocused(false);
        focusedWindow = null;
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
                if (!component.AbsoluteRectangle.IntersectsWith(dirtyRect)) continue;

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

    public static void Register(Window window)
    {
        try
        {
            window.zIndex = nextZIndex;
            window.Start();
            windows.Add(window);
            nextZIndex++;
            SetFocusedWindow(window);

            Explorer.taskbar.bar.AddStackChild(new Button(0, 0, 75, 25)
            {
                text = window.text,
                fontSize = 14,
                verticalAlignment = VerticalAlignment.Center,
                useBorders = true,

                leftMouseRelease = () =>
                {
                    //window.Visible = !window.Visible;
                    //window.MarkDirty();
                }
            });
        }
        catch (Exception ex)
        {
            Serial.WriteString(ex.Message);
        }//


    }

    public static void Close(Window window)
    {
        Invalidate(window.bounds);
        ClearPreviewRect();

        if (focusedWindow == window)
            focusedWindow = null;

        window.Stop();
        windows.Remove(window);

        Explorer.taskbar.bar.RemoveStackChild(window);
    }

    public static void Invalidate(Component dirty)
    {
        Invalidate(dirty.AbsoluteRectangle);
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
