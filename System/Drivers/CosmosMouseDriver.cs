using Cosmos.Kernel.System.Mouse;

namespace Windose.Drivers;

public sealed class CosmosMouseDriver : IWindoseDriver
{
    public string Name => "Cosmos Mouse";
    public WindoseDriverState State { get; private set; } = WindoseDriverState.Created;

    public int X => MouseManager.X;
    public int Y => MouseManager.Y;
    public float ScrollDelta => Mouse.scroll;
    public MouseState Buttons => Mouse.state;

    private readonly int screenWidth;
    private readonly int screenHeight;

    public CosmosMouseDriver(int screenWidth, int screenHeight)
    {
        this.screenWidth = screenWidth;
        this.screenHeight = screenHeight;
    }

    public void Start()
    {
        MouseManager.Initialize();
        MouseManager.SetScreenSize(screenWidth, screenHeight);
        State = WindoseDriverState.Started;
    }

    public void Update()
    {
        if (State != WindoseDriverState.Started)
        {
            return;
        }

        Mouse.Update();

    }

    public void Stop()
    {
        State = WindoseDriverState.Stopped;
    }
}
