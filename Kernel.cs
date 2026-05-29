using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Mouse;
using Sys = Cosmos.Kernel.System;


namespace Windose;

/// <summary>
/// Main kernel class - inherits from Cosmos.Kernel.System.Kernel.
/// </summary>
public class Kernel : Sys.Kernel
{
    public static Kernel Instance;
    public Canvas canvas;

    public ShellExplorer shellExplorer;
    protected override void BeforeRun()
    {
        Instance = this;
        Console.WriteLine("Cosmos booted successfully!");

        canvas = Canvas.GetFullScreen();

        MouseManager.SetScreenSize(canvas.Width, canvas.Height);
        Global.screenHeight = canvas.Height;
        Global.screenWidth = canvas.Width;

        shellExplorer = new ShellExplorer();
        ProcessManger.Start(shellExplorer);
    }

    protected override void Run()
    {
        canvas.Clear(Color.Black);
        KernelTime.SystemTicks++;
        TimerManager.Update();


        ProcessManger.Tick();


        canvas.DrawString($"Windose NativeAOT {Kernel.VersionString}", PCScreenFont.DefaultFont, Color.White, 10, 10);
        canvas.DrawFilledCircle(Color.White, MouseManager.X, MouseManager.Y, 2);

        canvas.Display();
    }
}
