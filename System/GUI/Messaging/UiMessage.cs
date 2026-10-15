public struct UiMessage
{
    public UiMessageType Type;
    public Component Target;
    public Window Window;
    public string Command;
    public object Data;
    public Action Action;

    public static UiMessage ForCommand(string command, Action action = null, Component target = null, object data = null)
    {
        return new UiMessage
        {
            Type = UiMessageType.Command,
            Command = command,
            Action = action,
            Target = target,
            Data = data
        };
    }

    public static UiMessage ForWindow(UiMessageType type, Window window)
    {
        return new UiMessage
        {
            Type = type,
            Window = window,
            Target = window
        };
    }
}
