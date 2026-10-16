using System;
using System.Drawing;

/// <summary>
/// Alt+Tab switcher overlay. First press swaps to the last window (old behavior) and shows
/// the list; further presses advance; idle timeout or click confirms, Esc reverts.
/// </summary>
public class TaskSwitcher : Component
{
    private static TaskSwitcher instance;
    private static Window origin;
    private static int lastActivity;
    private const int IdleTimeoutMs = 2000;

    private List<Window> items = new List<Window>();
    private int selected;

    private TaskSwitcher() : base(0, 0, 320, 60)
    {
        zLayer = DrawLayer.Overlay;
        Visible = false;
        isRoot = true;
    }

    public static bool VisibleNow => instance != null && instance.Visible;

    public static void Cycle()
    {
        if (LockManager.IsLocked) return;
        if (instance == null) instance = new TaskSwitcher();
        if (!instance.Visible)
        {
            origin = WindowManager.focusedWindow;
            WindowManager.SwapFocusedWindow();
        }
        else
        {
            Advance(1);
        }
        instance.Refresh();
        if (instance.items.Count == 0)
        {
            instance.Visible = false;
            return;
        }
        instance.Visible = true;
        lastActivity = Environment.TickCount;
    }

    public static bool Cancel()
    {
        if (instance == null || !instance.Visible) return false;
        if (origin != null)
            WindowManager.FocusWindowImmediate(origin);
        instance.Visible = false;
        return true;
    }

    private static void Confirm()
    {
        if (instance == null || !instance.Visible) return;
        Window current = WindowManager.focusedWindow;
        if (current != null) WindowManager.BringToFront(current);
        origin = null;
        instance.Visible = false;
    }

    private static void Advance(int dir)
    {
        if (instance == null) return;
        if (instance.items.Count == 0) return;
        int index = 0;
        Window focused = WindowManager.focusedWindow;
        for (int i = 0; i < instance.items.Count; i++)
        {
            if (instance.items[i] == focused) { index = i; break; }
        }
        index = (index + dir + instance.items.Count) % instance.items.Count;
        WindowManager.FocusWindowImmediate(instance.items[index]);
    }

    private void Refresh()
    {
        items.Clear();
        Window[] all = WindowManager.WindowsSnapshot();
        for (int i = 0; i < all.Length; i++)
        {
            Window w = all[i];
            if (w == null || !w.Visible || !w.showInTaskbar || w.isMinimized) continue;
            if (!WindowManager.IsOnCurrentDesktop(w)) continue;
            items.Add(w);
        }
        items.Sort((a, b) => b.zIndex.CompareTo(a.zIndex));
        int width = 320;
        int height = Math.Max(54, 30 + items.Count * 24);
        Resize(width, height);
        X = (Global.screenWidth - width) / 2;
        Y = (Global.screenHeight - height) / 3;
        MarkDirty();
        WindowManager.Invalidate(new Rectangle(X - 2, Y - 2, width + 4, height + 4));
    }

    public override void Update()
    {
        base.Update();
        if (!Visible) return;
        if (unchecked(Environment.TickCount - lastActivity) >= IdleTimeoutMs)
            Confirm();
    }

    public override void DrawLocal()
    {
        DrawFilledRectangle(Color.FromArgb(240, 32, 32, 32), 0, 0, Width, Height);
        DrawRectangle(Color.White, 0, 0, Width, Height);
        Window focused = WindowManager.focusedWindow;
        for (int i = 0; i < items.Count; i++)
        {
            int y = 8 + i * 24;
            if (y + 24 > Height) break;
            bool sel = items[i] == focused;
            if (sel)
                DrawFilledRectangle(Palette.Highlight, 4, y, Width - 8, 22);
            DrawAlignedText(items[i].text, sel ? Palette.HighlightText : Color.White, 14,
                new Rectangle(10, y, Width - 20, 22),
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }
        if (items.Count == 0)
            DrawAlignedText("No windows", Color.White, 14,
                new Rectangle(10, 8, Width - 20, 22),
                HorizontalAlignment.Left, VerticalAlignment.Center);
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!Visible) return false;
        lastActivity = Environment.TickCount;
        if (mouse.left == MouseEvents.Press)
        {
            int row = (mouseY - AbsoluteY - 8) / 24;
            if (row >= 0 && row < items.Count)
                WindowManager.FocusWindowImmediate(items[row]);
            Confirm();
            return true;
        }
        return true;
    }

    public override string GetComponentName() => "TaskSwitcher";
}
