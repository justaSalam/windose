using System;
using System.IO;

public sealed class DirectoryCommand : IShellCommand
{
    public string Name => "dir";
    public string Description => "Displays a list of files and subdirectories in a directory";
    public string Usage => "dir [path]";

    public DirectoryCommand()
    {
    }

    public void Execute(CommandContext context, string[] args)
    {
        string path = args.Length > 0 ? context.ResolvePath(args[0]) : context.CurrentDirectory;
        if (!Directory.Exists(path))
        {
            if (File.Exists(path))
            {
                FileInfo fi = new FileInfo(path);
                context.WriteLine($"^cyan{fi.CreationTime.ToString("yyyy-MM-dd HH:mm")} ^gray{ByteSize.Format(fi.Length)} ^white{fi.Name}");
                return;
            }
            context.WriteLine($"Directory not found: {path}");
            return;
        }

        try
        {
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
        catch (Exception ex)
        {
            context.WriteLine($"Error reading directory: {ex.Message}");
        }
    }
}
