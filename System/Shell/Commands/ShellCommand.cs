public interface IShellCommand
{
    string Name { get; }
    string Description { get; }
    string Usage { get; }
    void Execute(CommandContext context, string[] args);
}