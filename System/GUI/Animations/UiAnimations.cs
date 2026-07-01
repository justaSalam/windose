using System.Drawing;

namespace Windose;

public static class UiAnimations
{
    private const double WindowTransitionMs = 220;
    private const double MenuTransitionMs = 180;
    private const int MenuSlideDistance = 36;

    public static void MinimizeWindow(Window window)
    {
        if (window == null || !window.canMinimize || window.IsMinimized || window.IsAnimatingBounds || AnimationManager.IsAnimating(window))
            return;

        Rectangle start = window.bounds;
        Rectangle target = WindowManager.GetTaskbarButtonBounds(window);
        if (target.Width <= 0 || target.Height <= 0)
        {
            WindowManager.MinimizeImmediate(window);
            return;
        }

        window.SetFocused(false);
        window.RememberBoundsForRestore();

        AnimationManager.Run(window, WindowTransitionMs, EaseType.EaseIn, t =>
        {
            window.ApplyAnimatedBounds(AnimationManager.Lerp(start, target, t));
        }, () =>
        {
            window.FinishMinimize();
            if (WindowManager.focusedWindow == window)
                WindowManager.focusedWindow = null;
            WindowManager.FocusTopVisibleWindowPublic(window);
            Explorer.taskbar?.MarkDirty();
        });
    }

    public static void RestoreWindow(Window window)
    {
        if (window == null || !window.IsMinimized || AnimationManager.IsAnimating(window))
            return;

        Rectangle end = window.GetSavedRestoreBounds();
        Rectangle start = WindowManager.GetTaskbarButtonBounds(window);
        if (start.Width <= 0 || start.Height <= 0)
            start = new Rectangle(end.X, end.Y, Math.Max(80, end.Width / 4), Math.Max(24, end.Height / 4));

        window.BeginRestoreAnimation(start);
        WindowManager.Activate(window);

        AnimationManager.Run(window, WindowTransitionMs, EaseType.EaseOut, t =>
        {
            window.ApplyAnimatedBounds(AnimationManager.Lerp(start, end, t));
        }, () =>
        {
            window.ApplyAnimatedBounds(end);
            window.EndBoundsAnimation();
            Explorer.taskbar?.MarkDirty();
        });
    }

    public static void ShowStartMenu(StartMenu menu)
    {
        if (menu == null || AnimationManager.IsAnimating(menu)) return;

        if (menu.Visible && menu.Opacity >= 255 && menu.AtHomePosition())
        {
            WindowManager.Activate(menu);
            return;
        }

        AnimationManager.Cancel(menu);
        Rectangle home = menu.HomeBounds;
        Rectangle start = new Rectangle(home.X, home.Y + MenuSlideDistance, home.Width, home.Height);

        menu.Visible = true;
        menu.Opacity = 0;
        menu.ApplyAnimatedBounds(start);

        AnimationManager.Run(menu, MenuTransitionMs, EaseType.EaseOut, t =>
        {
            menu.Opacity = (byte)(t * 255);
            menu.ApplyAnimatedBounds(AnimationManager.Lerp(start, home, t));
        }, () =>
        {
            menu.ApplyAnimatedBounds(home);
            menu.Opacity = 255;
            WindowManager.Activate(menu);
        });
    }

    public static void HideStartMenu(StartMenu menu)
    {
        if (menu == null || !menu.Visible || AnimationManager.IsAnimating(menu)) return;

        Rectangle home = menu.HomeBounds;
        Rectangle end = new Rectangle(home.X, home.Y + MenuSlideDistance, home.Width, home.Height);

        AnimationManager.Run(menu, MenuTransitionMs, EaseType.EaseIn, t =>
        {
            float fade = 1f - t;
            menu.Opacity = (byte)(fade * 255);
            menu.ApplyAnimatedBounds(AnimationManager.Lerp(home, end, t));
        }, () =>
        {
            menu.HideMenuImmediate();
            menu.ApplyAnimatedBounds(home);
            menu.Opacity = 255;
        });
    }
}
