public sealed class DrawingDebugCommand : IShellCommand
{
    public string Name => "drawdebug";
    public string Description => "Toggles compositor dirty-region visualization.";
    public string Usage => "drawdebug <on|off|status>";

    public void Execute(CommandContext context, string[] args)
    {
        if (args.Length != 1)
        {
            context.WriteLine("Usage: " + Usage);
            return;
        }

        if (string.Equals(args[0], "on", StringComparison.OrdinalIgnoreCase))
        {
            WindowManager.SetDrawingDebug(true);
            context.WriteLine("Compositor drawing debug enabled.");
            return;
        }

        if (string.Equals(args[0], "off", StringComparison.OrdinalIgnoreCase))
        {
            WindowManager.SetDrawingDebug(false);
            context.WriteLine("Compositor drawing debug disabled.");
            return;
        }

        if (string.Equals(args[0], "status", StringComparison.OrdinalIgnoreCase))
        {
            context.WriteLine(WindowManager.DrawingDebugEnabled
                ? "Compositor drawing debug is enabled."
                : "Compositor drawing debug is disabled.");
            return;
        }

        context.WriteLine("Usage: " + Usage);
    }
}
