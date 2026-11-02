using System;
using System.Drawing;
using System.Threading;
using Cosmos.Kernel.System.Network;

public class NetworkFlyout : Window
{
    private static NetworkFlyout? instance;
    private readonly Label statusLabel;
    private readonly Label ipLabel;
    private readonly Label subnetLabel;
    private readonly Label gatewayLabel;

    private NetworkFlyout() : base(Global.screenWidth - 230, 0, 220, 230, "Network Status", false)
    {
        canResize = false;
        canMaximize = false;
        canMove = false;
        showInTaskbar = false;
        zLayer = DrawLayer.Popup;

        StackPanel panel = new StackPanel(Palette.ControlFace, 0, 0, Width, Height)
        {
            verticalAlignment = VerticalAlignment.Stretch,
            horizontalAlignment = HorizontalAlignment.Stretch,
            orientation = StackOrientation.Vertical,
            Padding = new Thickness(10),
            spacing = 6,
            clampSize = false
        };
        AddChild(panel);

        panel.AddStackChild(new Label(0, 0, 200, 20)
        {
            text = "Network Adapter",
            fontSize = 14,
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Left
        });

        statusLabel = new Label(0, 0, 200, 18)
        {
            text = "Status: Detecting...",
            fontSize = 12,
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Left
        };
        panel.AddStackChild(statusLabel);

        ipLabel = new Label(0, 0, 200, 18)
        {
            text = "IP: 0.0.0.0",
            fontSize = 12,
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Left
        };
        panel.AddStackChild(ipLabel);

        subnetLabel = new Label(0, 0, 200, 18)
        {
            text = "Subnet: 255.255.255.0",
            fontSize = 12,
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Left
        };
        panel.AddStackChild(subnetLabel);

        gatewayLabel = new Label(0, 0, 200, 18)
        {
            text = "Gateway: 0.0.0.0",
            fontSize = 12,
            useBackground = false,
            horizontalAlignment = HorizontalAlignment.Left
        };
        panel.AddStackChild(gatewayLabel);

        Button renewBtn = new Button("Renew DHCP", 0, 0, 140, 26)
        {
            horizontalAlignment = HorizontalAlignment.Center,
            leftClickAction = RefreshDhcp
        };
        panel.AddStackChild(renewBtn);

        UpdateNetworkInfo();
    }

    private void UpdateNetworkInfo()
    {
        try
        {
            IPConfig? config = NetworkManager.Primary.IPConfig;
            if (config != null)
            {
                statusLabel.text = "Status: Connected";
                ipLabel.text = "IP: " + (config.Address?.ToString() ?? "Not assigned");
                subnetLabel.text = "Subnet: " + (config.SubnetMask?.ToString() ?? "None");
                gatewayLabel.text = "Gateway: " + (config.DefaultGateway?.ToString() ?? "None");
            }
            else
            {
                statusLabel.text = "Status: No IP Assigned";
                ipLabel.text = "IP: 0.0.0.0";
                subnetLabel.text = "Subnet: None";
                gatewayLabel.text = "Gateway: None";
            }
        }
        catch (Exception ex)
        {
            statusLabel.text = "Status: " + ex.Message;
        }

        statusLabel.MarkDirty();
        ipLabel.MarkDirty();
        subnetLabel.MarkDirty();
        gatewayLabel.MarkDirty();
        MarkDirty();
    }

    private void RefreshDhcp()
    {
        statusLabel.text = "Requesting DHCP...";
        statusLabel.MarkDirty();

        new Thread(() =>
        {
            try
            {
                DhcpClient client = new DhcpClient();
                if (client.SendDiscoverPacket() != -1)
                {
                    UpdateNetworkInfo();
                }
                else
                {
                    statusLabel.text = "DHCP timed out";
                    statusLabel.MarkDirty();
                }
            }
            catch (Exception ex)
            {
                statusLabel.text = "DHCP failed: " + ex.Message;
                statusLabel.MarkDirty();
            }
        }).Start();
    }

    public static void Toggle()
    {
        if (instance == null)
        {
            instance = new NetworkFlyout();
            WindowManager.Register(instance);
        }

        int h = 230;
        int taskbarHeight = Explorer.taskbar != null ? Explorer.taskbar.Height : 30;
        int y = Math.Max(0, Global.screenHeight - taskbarHeight - h - 4);
        int x = Math.Max(0, Global.screenWidth - 240);
        instance.ApplyBounds(new Rectangle(x, y, 220, h));
        instance.Visible = !instance.Visible;
        if (instance.Visible)
        {
            instance.UpdateNetworkInfo();
        }
    }

    public override void OnLoseFocus()
    {
        Visible = false;
    }

    public override string GetComponentName() => "NetworkFlyout";
}
