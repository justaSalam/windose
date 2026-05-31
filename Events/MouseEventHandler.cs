using Cosmos.Kernel.System.Mouse;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public static class MouseEventHandler
{
    public static MouseLeftEvent mouseLeft = MouseLeftEvent.None;
    public static MouseRightEvent mouseRight = MouseRightEvent.None;
    public static MouseMiddleEvent mouseMiddle = MouseMiddleEvent.None;


    private static bool wasLeftDown, wasMiddleDown, wasRightDown;


    public static void Update()
    {
        HandleMouseLeft();
        HandleMouseMiddle();
        HandleMouseRight();
    }

    private static void HandleMouseLeft()
    {
        bool leftDown = MouseManager.LeftButton;

        if (!leftDown && mouseLeft == MouseLeftEvent.Release)
        {
            mouseLeft = MouseLeftEvent.None;
        }
        else if (leftDown && mouseLeft == MouseLeftEvent.None)
        {
            mouseLeft = MouseLeftEvent.Press;
        }
        else if (leftDown && mouseLeft == MouseLeftEvent.Press)
        {
            mouseLeft = MouseLeftEvent.Hold;
        }
        else if (!leftDown && (mouseLeft == MouseLeftEvent.Press || mouseLeft == MouseLeftEvent.Hold))
        {
            mouseLeft = MouseLeftEvent.Release;
        }

        wasLeftDown = leftDown;
    }
    private static void HandleMouseRight()
    {
        bool rightDown = MouseManager.RightButton;

        if (!rightDown && mouseRight == MouseRightEvent.Release)
        {
            mouseRight = MouseRightEvent.None;
        }
        else if (rightDown && mouseRight == MouseRightEvent.None)
        {
            mouseRight = MouseRightEvent.Press;
        }
        else if (rightDown && mouseRight == MouseRightEvent.Press)
        {
            mouseRight = MouseRightEvent.Hold;
        }
        else if (!rightDown && (mouseRight == MouseRightEvent.Press || mouseRight == MouseRightEvent.Hold))
        {
            mouseRight = MouseRightEvent.Release;
        }

        wasRightDown = rightDown;
    }

    private static void HandleMouseMiddle()
    {
        bool middleDown = MouseManager.MiddleButton;

        if (!middleDown && mouseMiddle == MouseMiddleEvent.Release)
        {
            mouseMiddle = MouseMiddleEvent.None;
        }
        if (middleDown && mouseMiddle == MouseMiddleEvent.None)
        {
            mouseMiddle = MouseMiddleEvent.Press;
        }
        else if (middleDown && mouseMiddle == MouseMiddleEvent.Press)
        {
            mouseMiddle = MouseMiddleEvent.Hold;
        }
        else if (!middleDown && (mouseMiddle == MouseMiddleEvent.Press || mouseMiddle == MouseMiddleEvent.Hold)) //Left Release
        {
            mouseMiddle = MouseMiddleEvent.Release;
        }

        wasMiddleDown = middleDown;
    }
}

public enum MouseLeftEvent
{
    None, Press, Hold, Release
}
public enum MouseRightEvent
{
    None, Press, Hold, Release
}

public enum MouseMiddleEvent
{
    None, Press, Hold, Release
}