

public sealed class ChangeDirectoryCommand : IShellCommand
{
    public string Name => "cd";
    public string Description => "Changes the current directory";
    public string Usage => "cd [path]";

    public ChangeDirectoryCommand()
    {
    }

    public void Execute(CommandContext context, string[] args)
    {
        if(args.Length < 1)
        {
            return;
        }
        else
        {
            string destination = args[0];
            string resolvedPath = context.ResolvePath(destination); 

            if(Directory.Exists(resolvedPath))
            {
                context.CurrentDirectory = resolvedPath;
            }
        }
    }

}