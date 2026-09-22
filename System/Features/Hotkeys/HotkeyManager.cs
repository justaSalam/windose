using Cosmos.Kernel.System.Keyboard;
using Windose.System.Drivers;
using Windose.System.Features;


public class HotkeyManager : SingleThreadedProcess
{
    public static List<GlobalHotkey> Hotkeys = new List<GlobalHotkey>();

    public static Queue<QueueCommand> CommandQueue = new Queue<QueueCommand>();

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
        SystemKeyEvent key;
        if (!Keyboard.CurrentEvent(out key))
            return;

        if (key.consumed)
            return;

        foreach (GlobalHotkey hotkey in Hotkeys)
        {
            if (hotkey.keyEvent.Key == key.KeyEvent.Key && hotkey.keyEvent.Modifiers == key.KeyEvent.Modifiers && hotkey.keyEvent.Type == key.KeyEvent.Type)
            {
                key.consumed = true;
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
public struct QueueCommand
{
    public GlobalHotkey hotkey;
}

