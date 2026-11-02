using System;
using System.IO;
using System.Drawing;
using Cosmos.Kernel.System;
using Cosmos.Kernel.System.Graphics;
using Windose;
using Windose.Programs;
using Windose.System.Kernel;
using Windose.System.Kernel.Subsystem;

namespace Windose.Programs
{
    /// <summary>
    /// Windows 7 About Windows dialog (winver).
    /// Displays OS edition, build number, RAM, and licensing details.
    /// </summary>
    public class AboutWindows : Window
    {
        private Png? winLogo;

        public AboutWindows(int x = -1, int y = -1)
            : base(x > 0 ? x : Math.Max(20, (Global.screenWidth - 440) / 2),
                   y > 0 ? y : Math.Max(20, (Global.screenHeight - 360) / 2),
                   440, 360, "About Windows", true)
        {
            canResize = false;
            canMaximize = false;

            try
            {
                if (File.Exists("/mnt/System/Icons/file_windows.png"))
                    winLogo = new Png("/mnt/System/Icons/file_windows.png");
                else if (File.Exists("/mnt/System/Icons/computermona.png"))
                    winLogo = new Png("/mnt/System/Icons/computermona.png");
            }
            catch { }

            if (Palette.GlassEnabled)
            {
                useGlass = true;
            }

            // Separator between banner and details
            AddChild(new Separator(14, 76, 412, 2)
            {
                orientation = LayoutOrientation.Horizontal
            });

            // Details labels
            AddChild(new Label(20, 90, 400, 20)
            {
                text = "Microsoft Windows [Version 6.1.7601]",
                fontSize = 14,
                textColor = Palette.ControlBlack,
                useBackground = false
            });

            AddChild(new Label(20, 114, 400, 20)
            {
                text = $"Windose NativeAOT Edition - Kernel {Kernel.VersionString}",
                fontSize = 14,
                textColor = Palette.ControlBlack,
                useBackground = false
            });

            long heapMb = GC.GetTotalMemory(false) / (1024 * 1024);
            AddChild(new Label(20, 142, 400, 20)
            {
                text = $"Physical Memory in use: {heapMb} MB heap (Cosmos x86_64)",
                fontSize = 12,
                textColor = Palette.ControlBlack,
                useBackground = false
            });

            string user = Session.CurrentUser != null ? Session.CurrentUser.username : "Guest";
            AddChild(new Label(20, 166, 400, 20)
            {
                text = $"Registered to: {user}",
                fontSize = 12,
                textColor = Palette.ControlBlack,
                useBackground = false
            });

            AddChild(new Label(20, 200, 400, 50)
            {
                text = "The Windose operating system and its user interface are protected by trademark and other pending or existing intellectual property rights in the United States and other countries/regions.",
                fontSize = 11,
                textColor = Palette.ControlShadow,
                useBackground = false,
                clampSize = false
            });

            // Separator above OK
            AddChild(new Separator(14, 280, 412, 2)
            {
                orientation = LayoutOrientation.Horizontal
            });

            // OK button
            Button okBtn = new Button("OK", 340, 292, 80, 26);
            okBtn.leftClickAction = () => WindowManager.PostClose(this);
            AddChild(okBtn);
        }

        public override void DrawLocal()
        {
            base.DrawLocal();

            // Header Aero banner
            DrawGradientVerticalRounded(Color.FromArgb(24, 60, 110), Color.FromArgb(12, 30, 65), 14, 10, 412, 60, 4);
            DrawRoundedRectangle(Color.FromArgb(40, 90, 160), 14, 10, 412, 60, 4);
            DrawHorizontalSpan(Color.FromArgb(120, 255, 255, 255), 11, 16, 420);

            if (winLogo != null)
            {
                try
                {
                    DrawImage(winLogo, 24, 22);
                }
                catch { }
            }

            DrawString("Windows 7", Color.White, 70, 20);
            DrawString("Ultimate", Color.FromArgb(200, 225, 255), 70, 40);
        }

        public override string GetComponentName() => "AboutWindows";
    }
}
