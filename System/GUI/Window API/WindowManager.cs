using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Mouse;
using Windose;
using Windose.System.GUI.Components;
using Windose.System.System_Calls;

public class WindowManager : SingleThreadedProcess
{
    public static List<Window> windows = new List<Window>();

    private static Dictionary<Window, Button> taskbarButtons = new Dictionary<Window, Button>();

    private static List<Rectangle> dirtyRects = new List<Rectangle>();
    private static List<UiMessage> messageQueue = new List<UiMessage>();

    private static readonly object messageQueueLock = new object();

    private readonly HashSet<Window> failedWindows = new HashSet<Window>();
    private readonly HashSet<Component> renderedComponents = new HashSet<Component>();

    private readonly List<ApplicationFailure> pendingFailures = new List<ApplicationFailure>();

    private static bool hasPreviewRect;

    private static Rectangle previewRect;

    private Window? capturedWindow;
    private Component? capturedComponent;
    public static Window? focusedWindow;

    private MouseState mouseState;

    private static int nextZIndex = 1;

    private static readonly Comparison<Window> zIndexCompare = (a, b) =>
    {
        int layer = a.zLayer.CompareTo(b.zLayer);
        return layer != 0 ? layer : a.zIndex.CompareTo(b.zIndex);
    };
    private int mx, my;

    public WindowManager() : base("Desktop Window Manager", ProcessType.Kernel)
    {
        components = Component.components;
        canTerminate = false;
        canOverridePriority = false;
    }

    private List<Component> components;



    public override void Update()
    {
        try { UpdateDesktop(); }
        catch (Exception exception)
        {
            Window likelyOwner = focusedWindow;
            capturedWindow = null;
            capturedComponent = null;
            FailApplication(likelyOwner, "running", exception);
        }
    }

    private void UpdateDesktop()
    {
        long updateStartedAt = PerformanceMetrics.Now;
        mx = MouseManager.X;
        my = MouseManager.Y;
        mouseState = Mouse.state;


        DispatchMessages();
        ShowPendingFailures();
        DispatchMessages();

        //Sort Components based on zLayer

        ComponentZSort();

        GeneralWindowUpdate();

        UpdateComponents();

        bool flowControl = HandleCapturedWindow(updateStartedAt);
        if (!flowControl)
        {
            return;
        }

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

        if (menuHandled)
        {
            DispatchMessages();
            ComposeDirtyRegions();
            DrawPreviewRect();
            PerformanceMetrics.AddWindowManager(updateStartedAt);
            return;
        }

        HandleWindowCapture();

        DispatchMessages();
        ComposeDirtyRegions();
        DrawPreviewRect();
        PerformanceMetrics.AddWindowManager(updateStartedAt);
    }

    private bool HandleCapturedWindow(long updateStartedAt)
    {
        if (capturedWindow != null) //Handling a captured window
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

            HandleKeyboardInput();
            DispatchMessages();
            ComposeDirtyRegions();
            DrawPreviewRect();

            PerformanceMetrics.AddWindowManager(updateStartedAt);

            return false;
        }

        if (capturedComponent != null)
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

            HandleKeyboardInput();
            DispatchMessages();
            ComposeDirtyRegions();
            DrawPreviewRect();
            PerformanceMetrics.AddWindowManager(updateStartedAt);
            return false;
        }

        return true;
    }

    private void HandleWindowCapture()
    {
        bool hitWindow = false;
        bool hitComponent = false;

        for (int i = windows.Count - 1; i >= 0; i--)//Window Capturing
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



    private void GeneralWindowUpdate()
    {
        for (int i = windows.Count - 1; i >= 0; i--) //General window update, called on every window
        {
            Window win = windows[i];
            if (win == null || failedWindows.Contains(win)) continue;
            try
            {
                //TODO: Windows should be updated by the scheduler instead, wait for parallel threads
                win.Update();
            }
            catch (Exception exception)
            {
                FailApplication(win, "updating", exception);
            }
        }
    }

    private void ComponentZSort()
    {
        components.Sort((component1, component2) =>
        {
            int zLayer = component1.zLayer.CompareTo(component2.zLayer);
            if (zLayer != 0) return zLayer;

            return component1.zIndex.CompareTo(component2.zIndex);
        });
        windows.Sort(zIndexCompare);
    }

    private void UpdateComponents()
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

    public static void PostMessage(UiMessage message)
    {
        if (message.Type == UiMessageType.None) return;
        lock (messageQueueLock) messageQueue.Add(message);
    }

    public static void PostCommand(string command, Action action = null, Component target = null, object data = null)
    {
        PostMessage(UiMessage.ForCommand(command, action, target, data));
    }

    public static void PostRegister(Window window)
    {
        PostMessage(UiMessage.ForWindow(UiMessageType.RegisterWindow, window));
    }

    public static void PostClose(Window window)
    {
        PostMessage(UiMessage.ForWindow(UiMessageType.CloseWindow, window));
    }

    public static void PostInvalidate(Component component)
    {
        PostMessage(UiMessage.ForInvalidate(component));
    }

    public static void PostInvalidate(Rectangle rectangle)
    {
        PostMessage(UiMessage.ForInvalidate(rectangle));
    }

    public static void PostLayoutChanged(Component component)
    {
        PostMessage(new UiMessage
        {
            Type = UiMessageType.LayoutChanged,
            Target = component
        });
    }

    public static void PostFocus(Window window)
    {
        PostMessage(UiMessage.ForWindow(UiMessageType.FocusWindow, window));
    }

    private void DispatchMessages()
    {
        long startedAt = PerformanceMetrics.Now;
        List<UiMessage> pending;
        lock (messageQueueLock)
        {
            int count = Math.Min(128, messageQueue.Count);
            if (count == 0)
            {
                PerformanceMetrics.AddMessages(startedAt);
                return;
            }
            pending = messageQueue.GetRange(0, count);
            messageQueue.RemoveRange(0, count);
        }

        for (int i = 0; i < pending.Count; i++)
        {
            UiMessage message = pending[i];
            try { DispatchMessage(message); }
            catch (Exception exception)
            {
                Window owner = message.Window ?? message.Target?.GetOwningWindow();
                FailApplication(owner, "processing " + message.Type, exception);
            }
        }

        PerformanceMetrics.AddMessages(startedAt);
    }

    private void DispatchMessage(UiMessage message)
    {
        switch (message.Type)
        {
            case UiMessageType.Command:
                if (message.Target != null)
                    message.Target.HandleMessage(message);

                message.Action?.Invoke();
                if (message.Target != null)
                    message.Target.MarkDirty();
                break;

            case UiMessageType.RegisterWindow:
                RegisterNow(message.Window);
                break;

            case UiMessageType.CloseWindow:
                CloseNow(message.Window);
                break;

            case UiMessageType.InvalidateComponent:
                if (message.Target != null)
                    Invalidate(message.Target);
                break;

            case UiMessageType.InvalidateRectangle:
                Invalidate(message.Rectangle);
                break;

            case UiMessageType.LayoutChanged:
                if (message.Target != null)
                {
                    message.Target.ResolveChildren();
                    message.Target.MarkDirty();
                }
                break;

            case UiMessageType.FocusWindow:
                if (message.Window != null)
                {
                    BringToFront(message.Window);
                    SetFocusedWindow(message.Window);
                }
                break;
        }
    }
    public static T FindWindow<T>() where T : Window
    {
        for (int i = windows.Count - 1; i >= 0; i--)
        {
            if (windows[i] is T window)
                return window;
        }

        return null;
    }

    public static void PostCommand<T>(string command, object data = null) where T : Window
    {
        T window = FindWindow<T>();
        if (window == null) return;

        PostCommand(command, target: window, data: data);
    }

    private void HandleKeyboardInput()
    {
        if (!KeyboardManager.KeyAvailable) return;

        KeyEvent keyEvent = KeyboardManager.ReadKey();

        if (Explorer.desktop != null && Explorer.desktop.ConsumeKeyboardInput)
        {
            Explorer.desktop.HandleKeyboard(keyEvent);
            return;
        }

        if (focusedWindow != null && !failedWindows.Contains(focusedWindow))
        {
            focusedWindow.HandleKeyboard(keyEvent);

            return;
        }

        for (int i = components.Count - 1; i >= 0; i--)
        {
            Component component = components[i];
            if (component == null || !component.Visible) continue;
            if (component is Window) continue;
            if (!component.isRoot) continue;


            component.HandleKeyboard(keyEvent);

            return;
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

    private static void SetFocusedWindow(Window window)
    {
        if (focusedWindow == window) return;

        if (focusedWindow != null) focusedWindow.SetFocused(false);


        focusedWindow = window;
        focusedWindow.SetFocused(true);
    }

    public static void ClearFocusedWindow()
    {
        if (focusedWindow == null) return;

        focusedWindow.SetFocused(false);
        focusedWindow = null;
    }

    private void ComposeDirtyRegions()
    {
        if (dirtyRects.Count == 0) return;

        long startedAt = PerformanceMetrics.Now;
        renderedComponents.Clear();

        for (int i = 0; i < dirtyRects.Count; i++)
        {
            Rectangle dirtyRect = dirtyRects[i];

            for (int componentIndex = 0; componentIndex < components.Count; componentIndex++)
            {
                Component component = components[componentIndex];
                if (!component.Visible) continue;

                Window owner = component.GetOwningWindow();
                if (owner != null && failedWindows.Contains(owner)) continue;

                if (!component.AbsoluteRectangle.IntersectsWith(dirtyRect)) continue;

                try
                {
                    if (component.HasDirtyTree())
                    {
                        component.DrawDirtyLocal(dirtyRect);
                        component.DrawToScreen(dirtyRect);
                        renderedComponents.Add(component);
                    }
                    else
                    {
                        component.DrawToScreen(dirtyRect);
                    }
                }
                catch (Exception exception)
                {
                    FailApplication(owner, "drawing", exception);
                }
            }
        }

        foreach (Component component in renderedComponents)
            component.MarkCleaned();

        dirtyRects.Clear();
        PerformanceMetrics.AddCompose(startedAt);
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
        if (!hasPreviewRect || focusedWindow == null) return;

        Kernel.mainBuffer.DrawDottedRectangle(Color.White, previewRect.X, previewRect.Y, previewRect.Width, previewRect.Height);
    }

    public static void Register(Window window)
    {
        PostRegister(window);
    }

    private static void RegisterNow(Window window)
    {
        lock (windows)
        {
            if (windows.Contains(window)) return;

            window.zIndex = nextZIndex;
            window.Start();
            windows.Add(window);
            nextZIndex++;
            SetFocusedWindow(window);

            // A newly registered window has no previous dirty rectangle to drive its
            // first composition pass. Explicitly schedule that first paint.
            window.ForceDirty();
            Invalidate(window.bounds);

            if (!window.showInTaskbar || Explorer.taskbar == null) return;

            Button taskbarButton = new Button(window.text, 0, 0, 75, 25)
            {
                verticalAlignment = VerticalAlignment.Center,
                useBorders = true,

                leftClickAction = () =>
                {
                    if (window.isMinimized)
                    {
                        Restore(window);
                    }
                    else if (focusedWindow == window)
                    {
                        Minimize(window);
                    }
                    else
                    {
                        Activate(window);
                    }
                }
            };

            taskbarButtons[window] = taskbarButton;
            Explorer.taskbar.windows.Add(taskbarButton);
            Explorer.taskbar.bar.AddStackChild(taskbarButton);
            Explorer.taskbar.ForceDirty();
            Invalidate(Explorer.taskbar.AbsoluteRectangle);
        }

    }

    public static void Close(Window window)
    {
        PostClose(window);
        PostInvalidate(window);
    }

    public static void Minimize(Window window)
    {
        MinimizeImmediate(window);
    }

    internal static void MinimizeImmediate(Window window)
    {
        if (window == null || !window.canMinimize || window.isMinimized) return;

        Invalidate(window.bounds);
        window.SetFocused(false);
        window.MinimizeWindow();

        if (focusedWindow == window)
            focusedWindow = null;

        FocusTopVisibleWindow(window);
        Explorer.taskbar.MarkDirty();
    }

    public static void Restore(Window window)
    {
        if (window == null) return;

        if (window.isMinimized)
        {
            window.RestoreFromTaskbar();
            window.SetFocused(true);
            window.zIndex = nextZIndex++;
            Invalidate(window.bounds);
        }
        else
        {
            Activate(window);
        }
    }

    public static Rectangle GetTaskbarButtonBounds(Window window)
    {
        if (window == null || !taskbarButtons.TryGetValue(window, out Button taskbarButton))
            return Rectangle.Empty;

        return taskbarButton.AbsoluteRectangle;
    }

    internal static void FocusTopVisibleWindowPublic(Window excludedWindow)
    {
        FocusTopVisibleWindow(excludedWindow);
    }

    public static void ToggleMaximize(Window window)
    {
        if (window == null || !window.canMaximize) return;

        Rectangle oldBounds = window.bounds;
        int workAreaHeight = Explorer.taskbar != null
            ? Explorer.taskbar.Y
            : Global.screenHeight;

        window.ToggleMaximized(new Rectangle(0, 0, Global.screenWidth, workAreaHeight));
        Invalidate(oldBounds);
        Invalidate(window.bounds);
        Activate(window);
    }

    public static void Activate(Window window)
    {
        if (window == null) return;

        if (window.isMinimized)
            window.RestoreFromTaskbar();

        window.zIndex = nextZIndex++;
        SetFocusedWindow(window);
        Invalidate(window.bounds);
    }

    private static void FocusTopVisibleWindow(Window excludedWindow)
    {
        Window nextWindow = null;

        for (int i = 0; i < windows.Count; i++)
        {
            Window candidate = windows[i];
            if (candidate == excludedWindow || !candidate.Visible) continue;
            if (nextWindow == null || candidate.zIndex > nextWindow.zIndex)
                nextWindow = candidate;
        }

        if (nextWindow != null)
            SetFocusedWindow(nextWindow);
    }

    private void FailApplication(Window window, string operation, Exception exception)
    {
        if (window != null && failedWindows.Contains(window))
        {
            return;
        }
        string applicationName = window?.text;
        if (string.IsNullOrEmpty(applicationName))
        {
            applicationName = "Application";
        }

        string detail = exception?.Message;
        if (string.IsNullOrEmpty(detail))
        {
            detail = "Unknown error";
        }

        SystemLogger.WriteLine(applicationName, "failed while " + operation + ": " + detail + "\n", ConsoleMessageType.Error);
        pendingFailures.Add(new ApplicationFailure(applicationName, operation, detail));

        if (window == null)
        {
            return;
        }

        failedWindows.Add(window);
        if (focusedWindow == window)
        {
            focusedWindow = null;
        }

        if (capturedWindow == window)
        {
            capturedWindow = null;
        }

        if (capturedComponent != null && capturedComponent.GetOwningWindow() == window)
        {
            capturedComponent = null;
        }

        PostClose(window);
    }

    private void ShowPendingFailures()
    {
        if (pendingFailures.Count == 0) return;

        for (int i = 0; i < pendingFailures.Count; i++)
        {
            ApplicationFailure failure = pendingFailures[i];
            try
            {
                string message = failure.application + " crashed while " + failure.operation + ": " + failure.detail;


                Window error = new Window(140, 140, 680, 130, "Application Error", true)
                {
                    canMaximize = false,
                    canResize = false,
                };
                Panel text = new Panel(Palette.ControlFace, 8, 34, 664, 48)
                {
                    text = message,
                    fontSize = 16,
                    textColor = Palette.ControlBlack,
                    useBackground = true,
                    horizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(8, 34, 8, 8),

                };
                error.AddChild(text);
                Register(error);
            }
            catch (Exception reportException)
            {
                SystemLogger.WriteLine("DWM", "Could not display application error: " + reportException.Message + "\n", ConsoleMessageType.Error);
            }
        }

        pendingFailures.Clear();
    }

    private void CloseNow(Window window)
    {
        lock (windows)
        {

            if (window == null || !windows.Contains(window)) return;

            BreezeRuntime.NotifyWindowClosed(window);

            Invalidate(window.bounds);
            ClearPreviewRect();

            if (focusedWindow == window)
                focusedWindow = null;

            if (capturedWindow == window)
                capturedWindow = null;

            if (capturedComponent != null && capturedComponent.GetOwningWindow() == window)
                capturedComponent = null;

            if (taskbarButtons.ContainsKey(window))
            {
                Button taskbarButton = taskbarButtons[window];

                taskbarButtons.Remove(window);

                Explorer.taskbar.windows.Remove(taskbarButton);
                Explorer.taskbar.bar.RemoveStackChild(taskbarButton);
                Explorer.taskbar.bar.ForceDirty();
                Explorer.taskbar.ForceDirty();

                Invalidate(Explorer.taskbar.AbsoluteRectangle);

            }

            windows.Remove(window);
            failedWindows.Remove(window);

            FocusTopVisibleWindow(window);
            window.Stop();
        }
    }

    public static void Invalidate(Component dirty)
    {
        Invalidate(dirty.AbsoluteRectangle);
    }

    public static void Invalidate(Rectangle dirtyRect)
    {
        dirtyRect = Rectangle.Intersect(
            dirtyRect,
            new Rectangle(0, 0, Global.screenWidth, Global.screenHeight));

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

    public static void InvalidateAll()
    {
        for (int i = 0; i < Component.components.Count; i++)
        {
            Component component = Component.components[i];
            if (component == null || !component.Visible) continue;
            component.ForceDirty();
        }

        Invalidate(new Rectangle(0, 0, Global.screenWidth, Global.screenHeight));
    }


    public void BringToFront(Window window)
    {
        window.zIndex = nextZIndex++;
        Invalidate(window);
    }


    private readonly struct ApplicationFailure
    {
        public readonly string application;
        public readonly string operation;
        public readonly string detail;

        public ApplicationFailure(string application, string operation, string detail)
        {
            this.application = application;
            this.operation = operation;
            this.detail = detail;
        }
    }
}
