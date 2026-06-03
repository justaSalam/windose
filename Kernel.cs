using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.Core.Memory.Heap;
using Cosmos.Kernel.Core.Runtime;
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

    int tick;

    protected override void BeforeRun()
    {
        Instance = this;
        GarbageCollector.Initialize();
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
        //ProcessManger.Start(taskbar);

        //ProcessManger.Start(windowManager);

        //windowManager.Register(new Window(canvas) { bounds = new Rectangle(100, 100, 320, 240) });

    }

    protected override void Run()
    {
        try
        {
            canvas.Clear(Color.Black);
            MouseEventHandler.Update();

            tick++;
            compositor.Flush();
            canvas.DrawString($"Windose NativeAOT {VersionString}", PCScreenFont.DefaultFont, Color.White, 10, 10);


            canvas.DrawString(tick.ToString(), PCScreenFont.DefaultFont, Color.White, 10, 80);

            canvas.DrawFilledCircle(Color.White, MouseManager.X, MouseManager.Y, 2);

            canvas.Display();

            GarbageCollector.GetStats(out int collections, out int freed);
            ulong heap = GarbageCollector.GetHeapSizeBytes();
            int timeInGC = GarbageCollector.GetLastGCPercentTimeInGC();

            Serial.WriteString($"[KERNEL GC] GC collections: {collections}\n");
            Serial.WriteString($"[KERNEL GC] GC freed: {freed} objects\n");
            Serial.WriteString($"[KERNEL GC] Heap: {heap} bytes\n");
            Serial.WriteString($"[KERNEL GC] Time in GC: {timeInGC}%\n");


        }
        catch (Exception ex)
        {
            canvas.Disable();
            Console.WriteLine(ex.Message);
        }

    }
}
