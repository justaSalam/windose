using Cosmos.Kernel.System.Input;
using System.Drawing;
using Windose.System.Kernel.Subsystem;

/// <summary>
/// System lock: blocks all input routing except the lock overlay until unlocked.
/// </summary>
public static class LockManager
{
    public static bool IsLocked { get; private set; }
    private static LockOverlay overlay;

    public static void Lock()
    {
        if (IsLocked) return;
        IsLocked = true;
        bool loggedOut = Session.CurrentUser == null;
        if (overlay == null) overlay = new LockOverlay();
        overlay.Show(loggedOut);
        WindowManager.ClearFocusedWindow();
    }

    public static void Unlock()
    {
        if (!IsLocked) return;
        IsLocked = false;
        WindowManager.NoteInput();
        if (overlay != null) overlay.HideOverlay();
    }

    /// <summary>Routes one frame of input to the lock overlay. True when locked.</summary>
    public static bool RouteLockedInput(int mx, int my, MouseState mouse)
    {
        if (!IsLocked || overlay == null) return false;
        try { overlay.HandleInput(mx, my, mouse); } catch { }
        if (KeyboardManager.TryReadKey(out KeyEvent key))
        {
            try { overlay.HandleKeyboard(key); } catch { }
        }
        return true;
    }
}
