

using Cosmos.Kernel.System.Network;
using Cosmos.Network.Ftp;

public sealed class FtpCommand : IShellCommand
{
    public string Name => "ftp";
    public string Description => "FTP client for transferring files";
    public string Usage => "ftp";
    private readonly SubcommandDispatcher dispatcher;
    private FtpServer ?server;

    public FtpCommand()
    {
        dispatcher = new SubcommandDispatcher(Usage).Add("stop", "stop", 1, StopListen);
    }

    private void StopListen(CommandContext context, string[] args)
    {
        if(server != null)
        {
            server.Close();
            context.WriteLine("FTP server stopped.");
        }
        else
        {
            context.WriteLine("No FTP server is running.");
        }
    }

    public void Execute(CommandContext context, string[] args)
    {
        if (args.Length > 0)
        {
            dispatcher.Dispatch(context, args);
        }
        else
        {
            context.WriteLine("Starting an FTP session...");
            server = new("/mnt")
            {               
                Authenticate = (user, password) => user == "cosmos" && password == "cosmos",
                Log = message => context.WriteLine(message + "\n"),
            };

            new Thread(server.Listen).Start();

        }
    }

}