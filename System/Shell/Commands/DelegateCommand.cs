// ============================================================================
// 5) Adapter for the trivial one-liner commands (echo, cls, pwd) so you don't
//    have to write a class for every single tiny command — keeps the delegate
//    style for the cases where it's genuinely simpler.
// ============================================================================

public sealed class DelegateCommand : IShellCommand
{
    public string Name { get; }
    public string Description { get; }
    public string Usage { get; }
    private readonly CommandHandler handler;

    public DelegateCommand(string name, string description, string usage, CommandHandler handler)
    {
        Name = name;
        Description = description;
        Usage = usage;
        this.handler = handler;
    }

    public void Execute(CommandContext context, string[] args) => handler(context, args);
}