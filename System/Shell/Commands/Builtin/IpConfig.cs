

using Cosmos.Kernel.System.Network;

public sealed class IpConfig : IShellCommand
{
    public string Name => "ipconfig";
    public string Description => "Displays TCP/IP configuration information";
    public string Usage => "ipconfig";

    private readonly SubcommandDispatcher dispatcher;

    public IpConfig()
    {
        dispatcher = new SubcommandDispatcher(Usage);
    }

    public void Execute(CommandContext context, string[] args)
    {
        if (args.Length > 0)
        {
            dispatcher.Dispatch(context, args);
        }
        else
        {
            new Thread(() =>
            {
                context.WriteLine("DHCP Requesting IP Address...");

                DhcpClient dhcpClient = new();

                if (dhcpClient.SendDiscoverPacket() != -1)
                {
                    IPConfig? config = NetworkManager.Primary.IPConfig;
                    
                    if (config is not null)
                    {
                        context.WriteLine("IP address: " + config.Address.ToString());
                        context.WriteLine("Subnet:     " + config.SubnetMask.ToString());
                        context.WriteLine("Gateway:    " + config.DefaultGateway.ToString());
                    }
                }
                else
                {
                    context.WriteLine("DHCP timed out");
                }
            }).Start();

        }
    }

}