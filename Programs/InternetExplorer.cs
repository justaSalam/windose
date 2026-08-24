using Cosmos.Kernel.System.Graphics;
using System;
using System.Collections.Generic;
using System.Text;

namespace Windose.Programs
{
    internal class InternetExplorer : Window
    {

        private DockPanel root;
        private MenuBar menuBar;
        private Toolbar toolbar;
        private AddressBar addressBar;
        private StatusBar statusBar;
        private DockPanel explorerBody;

        private WebView webView;



        public InternetExplorer(int x, int y) : base(x, y, 800, 600, "Internet Explorer", true, null)
        {
            root = new DockPanel(0, 0, Width, Height)
            {
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(28, 2, 2, 2),
                Padding = new Thickness(0),
                useBackground = true,
            };

            webView = new WebView(0, 0, Width, Height);
            menuBar = new MenuBar(0, 0, Width);

            menuBar.AddMenuPage("File");
            menuBar.AddMenuPage("Edit");
            menuBar.AddMenuPage("View");
            menuBar.AddMenuPage("Go");
            menuBar.AddMenuPage("Favourites");
            menuBar.AddMenuPage("Help");

            toolbar = new Toolbar(0, 0, Width, 28);

            toolbar.AddButton(new Png("/mnt/System/Icons/msg_error.png"));//back
            toolbar.AddButton(new Png("/mnt/System/Icons/msg_error.png"));//forward
            toolbar.AddButton(new Png("/mnt/System/Icons/msg_error.png"));//stop
            toolbar.AddButton(new Png("/mnt/System/Icons/msg_error.png"));//refresh
            toolbar.AddButton(new Png("/mnt/System/Icons/homepage_alt.png"));//home
            toolbar.AddSeparator();
            toolbar.AddButton(new Png("/mnt/System/Icons/filepack.png"));//favs
            toolbar.AddButton(new Png("/mnt/System/Icons/history.png"));//history

            addressBar = new AddressBar(0, 0, Width);
            statusBar = new StatusBar(0, 0, Width);
            explorerBody = new DockPanel(0, 0, Width, Height)
            {
                clampSize = false,
                useBackground = true,
                backgroundColor = Palette.ControlWhite,
                Padding = new Thickness(0),
            };


            root.AddDockChild(menuBar, Dock.Top);
            root.AddDockChild(toolbar, Dock.Top);
            root.AddDockChild(addressBar, Dock.Top);
            root.AddDockChild(statusBar, Dock.Bottom);
            root.AddDockChild(explorerBody, Dock.Fill);

            explorerBody.AddDockChild(webView, Dock.Fill);

            statusBar.AddPanel("Waiting for network");


            AddChild(root);
        }
    }
}
