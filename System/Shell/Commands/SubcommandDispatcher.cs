// ============================================================================
// 2) Reusable subcommand dispatcher.
//    Every command that currently does "switch (arguments[0]) { case "x": ... }"
//    (uac, diskmgr, and future svga/ipconfig) can build one of these instead.
//    Benefits over hand-rolled switches:
//      - per-subcommand usage strings, auto-generated help
//      - per-subcommand minimum-arg checking (fixes the uac create crash)
//      - unknown subcommand handling written once
// ============================================================================

public sealed class SubcommandDispatcher
{
    private readonly struct Entry
    {
        public readonly string Usage;
        public readonly int MinArgs;
        public readonly Action<CommandContext, string[]> Handler;

        public Entry(string usage, int minArgs, Action<CommandContext, string[]> handler)
        {
            Usage = usage;
            MinArgs = minArgs;
            Handler = handler;
        }
    }

    private readonly Dictionary<string, Entry> subcommands =
        new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    private readonly string commandUsage;

    public SubcommandDispatcher(string commandUsage)
    {
        this.commandUsage = commandUsage;
    }

    /// <param name="minArgs">Args required AFTER the subcommand name itself.</param>
    public SubcommandDispatcher Add(string name, string usage, int minArgs, Action<CommandContext, string[]> handler)
    {
        subcommands[name] = new Entry(usage, minArgs, handler);
        return this;
    }

    public void Dispatch(CommandContext context, string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp(context);
            return;
        }

        if (!subcommands.TryGetValue(args[0], out Entry entry))
        {
            context.WriteLine("Unknown subcommand: " + args[0]);
            PrintHelp(context);
            return;
        }

        string[] rest = Tail(args);
        if (rest.Length < entry.MinArgs)
        {
            context.WriteLine("Usage: " + args[0] + " " + entry.Usage);
            return;
        }

        entry.Handler(context, rest);
    }

    private void PrintHelp(CommandContext context)
    {
        context.WriteLine("Usage: " + commandUsage);
        context.WriteLine("Subcommands:");
        foreach (KeyValuePair<string, Entry> kv in subcommands)
            context.WriteLine("  " + kv.Key.PadRight(10) + kv.Value.Usage);
    }

    private static string[] Tail(string[] args)
    {
        string[] rest = new string[args.Length - 1];
        Array.Copy(args, 1, rest, 0, rest.Length);
        return rest;
    }
}