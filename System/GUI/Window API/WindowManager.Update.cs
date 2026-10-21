using Cosmos.Kernel.System.Input;
using Windose;

public partial class WindowManager
{
    private int lastMx = int.MinValue;
    private int lastMy = int.MinValue;
    private MouseState lastMouseButtons;

    private void UpdateDesktop()
    {
        long updateStartedAt = PerformanceMetrics.Now;
        mx = MouseManager.X;
        my = MouseManager.Y;
        mouseState = Mouse.state;

        if (mx != lastMx || my != lastMy || mouseState.left != lastMouseButtons.left ||
            mouseState.right != lastMouseButtons.right || mouseState.middle != lastMouseButtons.middle)
            NoteInput();
        lastMx = mx;
        lastMy = my;
        lastMouseButtons = mouseState;

        RefreshIdleSetting();
        if (!LockManager.IsLocked && idleLockMinutes > 0 &&
            unchecked(Environment.TickCount - lastInputTick) >= 0 &&
            unchecked(Environment.TickCount - lastInputTick) > idleLockMinutes * 60000)
        {
            LockManager.Lock();
        }

        DispatchMessages();
        ShowPendingFailures();

        ComponentZSort();
        UpdateComponents();

        if (LockManager.IsLocked)
        {
            LockManager.RouteLockedInput(mx, my, mouseState);
            ComposeDirtyRegions();
            DrawPreviewRect();
            PerformanceMetrics.AddWindowManager(updateStartedAt);
            return;
        }

        if (DragDropManager.IsDragging)
        {
            capturedWindow = null;
            capturedComponent = null;
            HandleKeyboardInput();
            HandleWindowCapture();
        }
        else if (capturedWindow != null || capturedComponent != null)
        {
            HandleCapturedInput();
        }
        else
        {
            HandleKeyboardInput();

            bool menuHandled = false;

            try
            {
                menuHandled = MenuPopup.HandleOpenMenuInput(mx, my, mouseState);
            }
            catch (Exception exception)
            {
                FailApplication(focusedWindow, "handling menu input", exception);
            }

            if (!menuHandled)
                HandleWindowCapture();
        }

        DispatchMessages();
        ComposeDirtyRegions();
        DrawPreviewRect();
        PerformanceMetrics.AddWindowManager(updateStartedAt);
    }

    private void HandleCapturedInput()
    {
        if (capturedWindow != null)
        {
            if (!failedWindows.Contains(capturedWindow))
            {
                try
                {
                    capturedWindow.HandleInput(mx, my, mouseState);
                }
                catch (Exception exception)
                {
                    FailApplication(capturedWindow, "handling mouse input", exception);
                }
            }

            if (mouseState.left == MouseEvents.Release || mouseState.left == MouseEvents.None)
                capturedWindow = null;
        }
        else if (capturedComponent != null)
        {
            try
            {
                capturedComponent.HandleInput(mx, my, mouseState);
            }
            catch (Exception exception)
            {
                FailApplication(capturedComponent.GetOwningWindow(), "handling mouse input", exception);
            }

            if (mouseState.left == MouseEvents.Release || mouseState.left == MouseEvents.None)
            {
                capturedComponent = null;
            }
        }

        HandleKeyboardInput();
    }

    private void HandleWindowCapture()
    {
        if (DragDropManager.IsDragging)
        {
            HandleDragUpdate();
            return;
        }
        lock (windows)
        {
            bool hitWindow = false;
            bool hitComponent = false;

            for (int i = windows.Count - 1; i >= 0; i--)
            {
                Window win = windows[i];

                if (win == null || !win.Visible || failedWindows.Contains(win)) continue;
                if (win.desktop >= 0 && win.desktop != CurrentDesktop) continue;
                if (!win.HitTest(mx, my)) continue;

                hitWindow = true;

                if (mouseState.left == MouseEvents.Press)
                {
                    BringToFront(win);
                    SetFocusedWindow(win);
                    capturedWindow = win;
                }
                try
                {
                    if (win.HandleInput(mx, my, mouseState)) break;
                }
                catch (Exception exception)
                {
                    FailApplication(win, "handling mouse input", exception);
                    break;
                }
            }

            if (!hitWindow)
                hitComponent = HandleRootComponentInput();

            if (!hitWindow && !hitComponent && mouseState.left == MouseEvents.Press)
                ClearFocusedWindow();
        }
    }

    private void HandleDragUpdate()
    {
        if (mouseState.left == MouseEvents.Release)
        {
            try { DragDropManager.FinishDrop(mx, my); }
            catch (Exception exception)
            {
                FailApplication(focusedWindow, "finishing drag and drop", exception);
                DragDropManager.Cancel();
            }
            return;
        }

        if (mouseState.left == MouseEvents.None)
        {
            DragDropManager.Cancel();
            return;
        }

        try { DragDropManager.UpdateTarget(FindDropTarget(mx, my), mx, my); }
        catch (Exception exception)
        {
            FailApplication(focusedWindow, "routing drag and drop", exception);
            DragDropManager.Cancel();
        }
    }

    private Component FindDropTarget(int x, int y)
    {
        lock (windows)
        {
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                Window win = windows[i];
                if (win == null || !win.Visible || failedWindows.Contains(win)) continue;
                if (win.desktop >= 0 && win.desktop != CurrentDesktop) continue;
                if (!win.HitTest(x, y)) continue;
                Component deepest = win.GetChildAt(x, y) ?? (Component)win;
                for (Component c = deepest; c != null; c = c.Parent)
                {
                    if (c.AllowDrop && c.Visible && c.IsInsideAbsolute(x, y)) return c;
                }
            }
        }

        for (int i = components.Count - 1; i >= 0; i--)
        {
            Component root = components[i];
            if (root == null || !root.Visible || !root.isRoot || root is Window) continue;
            if (!root.IsInsideAbsolute(x, y)) continue;
            Component deepest = root.GetChildAt(x, y) ?? root;
            for (Component c = deepest; c != null; c = c.Parent)
            {
                if (c.AllowDrop && c.Visible && c.IsInsideAbsolute(x, y)) return c;
            }
        }

        return null;
    }

    private static readonly Comparison<Component> componentZIndexCompare = (a, b) =>
    {
        int layer = a.zLayer.CompareTo(b.zLayer);
        return layer != 0 ? layer : a.zIndex.CompareTo(b.zIndex);
    };

    private void ComponentZSort()
    {
        if (!zOrderDirty) return;
        zOrderDirty = false;
        lock (Component.components)
        {
            components.Sort(componentZIndexCompare);
            windows.Sort(zIndexCompare);
        }
    }

    private void UpdateComponents()
    {
        lock (Component.components)
        {
            for (int i = components.Count - 1; i >= 0; i--)
            {
                Component component = components[i];
                if (component == null || component is Window || !component.isRoot) continue;
                if (!component.Visible && component is not Tooltip) continue;
                try
                {
                    component.Update();
                }
                catch (Exception exception)
                {
                    FailApplication(component.GetOwningWindow(), "updating component", exception);
                }
            }
        }
    }

    private void HandleKeyboardInput()
    {
        lock (Component.components)
        {

            if (!KeyboardManager.TryReadKey(out KeyEvent? key))
            {
                return;
            }

            NoteInput();

            if (key.Key == Key.Escape)
            {
                if (TaskSwitcher.Cancel()) return;
                if (MenuPopup.HasOpenMenus)
                {
                    MenuPopup.HideAll();
                    return;
                }
            }

            if (HotkeyManager.HandleKeyEvent(key)) return;

            if (Explorer.desktop != null && Explorer.desktop.ConsumeKeyboardInput &&
                (focusedWindow == null || Explorer.desktop.IsRenaming))
            {
                Explorer.desktop.HandleKeyboard(key);
                return;
            }

            if (focusedWindow != null && !failedWindows.Contains(focusedWindow))
            {
                focusedWindow.HandleKeyboard(key);
                return;
            }

            for (int i = components.Count - 1; i >= 0; i--)
            {
                Component component = components[i];
                if (component == null || !component.Visible) continue;
                if (component is Window) continue;
                if (!component.isRoot) continue;

                component.HandleKeyboard(key);
                return;
            }
        }
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

            bool handled;
            handled = component.HandleInput(mx, my, mouseState);

            if (handled && mouseState.left == MouseEvents.Press)
                capturedComponent = component;

            return handled;
        }

        return false;
    }
}
