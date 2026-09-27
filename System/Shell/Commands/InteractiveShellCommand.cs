// Interactive sub-shells (diskpart-style). Different beast from `uac`/
// `diskmgr`: these don't take a subcommand as an argument, they take over the
// prompt entirely and run their own REPL with their own persistent state
// until the user types "exit". Requires CommandContext to expose a blocking
// ReadLine(prompt) — the same primitive the top-level shell loop already
// needs for line input, just re-exposed to commands.
using Microsoft.CodeAnalysis;

public abstract class InteractiveShellCommand : IShellCommand
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string Usage { get; }
    protected abstract string Prompt { get; }
    protected abstract SubcommandDispatcher BuildDispatcher();

    public void Execute(CommandContext context, string[] args)
    {
        SubcommandDispatcher dispatcher = BuildDispatcher();
        dispatcher.Add("exit", "exit", 0, (context, args) => { });
        dispatcher.Add("clear", "clear", 0, (context, args) => { context.Clear(); });
        while (true)
        {
            string line = context.ReadLine(Prompt).Trim();
            if (line.Length == 0) continue;
            if (line.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

            string[] parts = CommandLineParser.SplitCommandLineIntoArguments(line, true).ToArray();
            dispatcher.Dispatch(context, parts);
        }
    }
}
