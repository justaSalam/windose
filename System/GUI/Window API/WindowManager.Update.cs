using Cosmos.Kernel.System.Input;
using Windose;

public partial class WindowManager
{
    private void UpdateDesktop()
    {
        long updateStartedAt = PerformanceMetrics.Now;
        mx = MouseManager.X;
        my = MouseManager.Y;
        mouseState = Mouse.state;

        DispatchMessages();
        ShowPendingFailures();

        ComponentZSort();
        UpdateComponents();

        if (capturedWindow != null || capturedComponent != null)
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
        lock (windows)
        {
            bool hitWindow = false;
            bool hitComponent = false;

            for (int i = windows.Count - 1; i >= 0; i--)
            {
                Window win = windows[i];

                if (win == null || !win.Visible || failedWindows.Contains(win)) continue;
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

    private void ComponentZSort()
    {
        if (!zOrderDirty) return;
        zOrderDirty = false;
        lock (Component.components)
        {
            components.Sort((component1, component2) =>
            {
                int zLayer = component1.zLayer.CompareTo(component2.zLayer);
                if (zLayer != 0) return zLayer;

                return component1.zIndex.CompareTo(component2.zIndex);
            });
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

            HotkeyManager.HandleKeyEvent();

            if (Explorer.desktop != null && Explorer.desktop.ConsumeKeyboardInput)
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
