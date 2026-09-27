public interface IShellCommand
{
    /// <summary>
    /// Used when calling a command from the shell
    /// </summary>
    string Name { get; }
    string Description { get; }
    string Usage { get; }
    void Execute(CommandContext context, string[] args);
}