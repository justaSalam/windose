
using Cosmos.Kernel.System.Input;
using Windose.System.Features;
using Windose.System.System_Calls;


public class HotkeyManager : SingleThreadedProcess
{
    public static List<GlobalHotkey> Hotkeys = new List<GlobalHotkey>();

    public HotkeyManager() : base("System Hotkeys", ProcessType.Kernel)
    { 
        canOverridePriority = false;
        canTerminate = false;
    }

    /// <summary>
    /// Actions run synchronously on the window manager's input thread: they must
    /// return immediately. Never block (no ElfRunner.Run/Wait/Thread.Join/sleep loops);
    /// use ElfRunner.Spawn for programs. A throwing action is logged, not fatal.
    /// </summary>
    public static void RegisterHotkey(KeyEvent keyEvent, Action action)
    {
        Hotkeys.Add(new GlobalHotkey { keyEvent = keyEvent, action = action });
    }


    public static void UnregisterHotkey(KeyEvent keyEvent)
    {
        Hotkeys.RemoveAll(h => h.keyEvent.Key == keyEvent.Key && h.keyEvent.Modifiers == keyEvent.Modifiers);
    }

    

    public static bool HandleKeyEvent(KeyEvent keyEvent)
    {
        foreach (GlobalHotkey hotkey in Hotkeys)
        {
            if (hotkey.keyEvent.Key == keyEvent.Key && hotkey.keyEvent.Modifiers == keyEvent.Modifiers && hotkey.keyEvent.Type == keyEvent.Type)
            {
                try
                {
                    hotkey.action?.Invoke();
                }
                catch (Exception exception)
                {
                    try { SystemLogger.WriteLine("Hotkeys", "Hotkey action failed: " + exception.Message, ConsoleMessageType.Error); } catch { }
                }
                return true;
            }
        }
        return false;
    }

    public static void HandleKeyEvent()
    {
        // Peek-based pump kept for compatibility; the window manager matches
        // consumed keys directly (see below) so this must not fire actions.
    }


    public override void Update()
    {
        HandleKeyEvent();
    }
}