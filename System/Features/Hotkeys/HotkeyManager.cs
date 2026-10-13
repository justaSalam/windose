
using Cosmos.Kernel.System.Input;
using Windose.System.Features;


public class HotkeyManager : SingleThreadedProcess
{
    public static List<GlobalHotkey> Hotkeys = new List<GlobalHotkey>();

    public HotkeyManager() : base("System Hotkeys", ProcessType.Kernel)
    { 
        canOverridePriority = false;
        canTerminate = false;
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
        KeyEvent keyEvent = KeyboardManager.Peek();

        foreach (GlobalHotkey hotkey in Hotkeys)
        {
            if (hotkey.keyEvent.Key == keyEvent.Key && hotkey.keyEvent.Modifiers == keyEvent.Modifiers && hotkey.keyEvent.Type == keyEvent.Type)
            {
                hotkey.action?.Invoke();
                break;
            }
        }
    }


    public override void Update()
    {
        HandleKeyEvent();
    }
}