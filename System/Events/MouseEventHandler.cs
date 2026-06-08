using Cosmos.Kernel.System.Mouse;

public static class Mouse
{
    public static MouseState state;


    public static void Update()
    {
        state.left = HandleButton(MouseManager.LeftButton, state.left);
        state.right = HandleButton(MouseManager.RightButton, state.right);
        state.middle = HandleButton(MouseManager.MiddleButton, state.middle);
    }

    private static MouseEvents HandleButton(bool down, MouseEvents current)
    {
        if (down && current == MouseEvents.None) return MouseEvents.Press;
        if (down && current == MouseEvents.Press) return MouseEvents.Hold;
        if (!down && (current == MouseEvents.Press || current == MouseEvents.Hold)) return MouseEvents.Release;
        if (!down && current == MouseEvents.Release) return MouseEvents.None;
        return current;
    }
}

public enum MouseEvents
{
    None, Press, Hold, Release
}

public struct MouseState
{
    public MouseEvents left;
    public MouseEvents right;
    public MouseEvents middle;
}