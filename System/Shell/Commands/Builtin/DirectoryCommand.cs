

public sealed class DirectoryCommand : IShellCommand
{
    public string Name => "dir";
    public string Description => "Displays a list of files and subdirectories in a directory";
    public string Usage => "dir [path]";

    private readonly SubcommandDispatcher dispatcher;

    public DirectoryCommand()
    {
        dispatcher = new SubcommandDispatcher(Usage);
    }

    public void Execute(CommandContext context, string[] args)
    {
        if(args.Length > 0)
        {
            dispatcher.Dispatch(context, args);
        }
        else
        {
            string path = context.CurrentDirectory;
            FileInfo[] files = new DirectoryInfo(path).GetFiles();
            DirectoryInfo[] directories = new DirectoryInfo(path).GetDirectories();
            context.WriteLine($"Directory of {path}");
            context.WriteLine("");

            foreach (DirectoryInfo directory in directories)
            {
                context.WriteLine($"^cyan{directory.CreationTime.ToString("yyyy-MM-dd HH:mm")} ^yellow<DIR> ^white{directory.Name}");
            }

            foreach (FileInfo file in files)
            {
                context.WriteLine($"^cyan{file.CreationTime.ToString("yyyy-MM-dd HH:mm")} ^gray{ByteSize.Format(file.Length)} ^white{file.Name}");
            }
        }
    }

}