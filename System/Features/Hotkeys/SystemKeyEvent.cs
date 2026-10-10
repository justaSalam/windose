
using Cosmos.Kernel.System.Input;

public class SystemKeyEvent
{
    public KeyEvent KeyEvent { get; set; }
    public bool consumed { get; set; }

    public SystemKeyEvent(KeyEvent keyEvent, bool consumed)
    {
        KeyEvent = keyEvent;
        this.consumed = consumed;
    }
}

