using Cosmos.Kernel.System.Keyboard;



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

