namespace Windose.Drivers;

public enum WindoseDriverState
{
    Created,
    Started,
    Failed,
    Stopped,
}

public interface IWindoseDriver
{
    string Name { get; }
    WindoseDriverState State { get; }

    void Start();
    void Stop();
}
