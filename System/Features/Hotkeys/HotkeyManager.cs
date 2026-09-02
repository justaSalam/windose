using Cosmos.Kernel.System.Keyboard;
using Windose.System.Drivers;
using Windose.System.Features;


public class HotkeyManager : SingleThreadedProcess
{
    public static List<GlobalHotkey> Hotkeys = new List<GlobalHotkey>();

    public HotkeyManager() : base("syshtks", ProcessType.Kernel)
    { 
        canOverridePriority = false;
        canTerminate = false;
        Priority = ProcessPriority.High;
    }

    public static void RegisterHotkey(KeyEvent keyEvent, Action action)
    {
        Hotkeys.Add(new GlobalHotkey { keyEvent = keyEvent, action = action });
    }


    public static void UnregisterHotkey(KeyEvent keyEvent)
    {
        Hotkeys.RemoveAll(h => h.keyEvent.Key == keyEvent.Key && h.keyEvent.Modifiers == keyEvent.Modifiers);
    }

    public static void HandleKeyEvent()
    {
        KeyEvent keyEvent = Keyboard.CurrentEvent();
        foreach (GlobalHotkey hotkey in Hotkeys)
        {
            if (hotkey.keyEvent.Key == keyEvent.Key && hotkey.keyEvent.Modifiers == keyEvent.Modifiers && hotkey.keyEvent.Type == keyEvent.Type)
            {
                hotkey.action?.Invoke();
            }
        }
    }

    public override void Update()
    {
        HandleKeyEvent();
    }
}

