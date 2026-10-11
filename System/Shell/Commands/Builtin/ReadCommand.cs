

public sealed class ReadCommand : IShellCommand
{
    public string Name => "read";
    public string Description => "Reads input from the user";
    public string Usage => "read [file]";

    public ReadCommand()
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

            if(File.Exists(resolvedPath))
            {
                string[] content = File.ReadAllLines(resolvedPath);
                foreach (string line in content)
                {
                    context.WriteLine(line);
                }
            }
        }
    }

}