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
    public WindowManager windowManager;
    public Taskbar taskbar;
    private Compositor compositor;

    protected override void BeforeRun()
    {
        Instance = this;
        Console.WriteLine("Cosmos booted successfully!");

        canvas = Canvas.GetFullScreen();
        compositor = new Compositor(canvas);

        MouseManager.Initialize();
        MouseManager.SetScreenSize(canvas.Width, canvas.Height);
        Global.screenHeight = canvas.Height;
        Global.screenWidth = canvas.Width;

        shellExplorer = new ShellExplorer();
        taskbar = new Taskbar(canvas);
        windowManager = new WindowManager();

        ProcessManger.Start(shellExplorer);
        ProcessManger.Start(taskbar);

        ProcessManger.Start(windowManager);

        windowManager.Register(new Window(canvas) { bounds = new Rectangle(100, 100, 320, 240) });

    }

    protected override void Run()
    {
        try
        {
            canvas.Clear(Color.Black);
            MouseEventHandler.Update();

            ProcessManger.Update();


            canvas.DrawString($"Windose NativeAOT {VersionString}", PCScreenFont.DefaultFont, Color.White, 10, 10);

            canvas.DrawFilledCircle(Color.White, MouseManager.X, MouseManager.Y, 2);


            canvas.Display();
        }
        catch (Exception ex)
        {
            canvas.Disable();
            Console.WriteLine(ex.Message);
        }

    }
}
