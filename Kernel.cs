using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.Core.Memory.Heap;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.HAL;
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
    private const int gcrate = 150;
    public static Kernel Instance = null!;
    public static DirectBitmap mainBuffer;
    public static Canvas canvas = null!;

    private WindowManager windowManager = null!;
    private const string versionString = $"Windose NativeAOT {VersionString}";
    int tick;

    protected override void BeforeRun()
    {
        Instance = this;
        GarbageCollector.Initialize();
        Console.WriteLine("Cosmos booted successfully!");

        canvas = Canvas.GetFullScreen();
        mainBuffer = new DirectBitmap(canvas.Width, canvas.Height);

        MouseManager.Initialize();
        MouseManager.SetScreenSize(canvas.Width, canvas.Height);
        Global.screenHeight = canvas.Height;
        Global.screenWidth = canvas.Width;

        Explorer explorer = new Explorer(canvas);




        ProcessManger.Start(new WindowManager());
        ProcessManger.Start(explorer);
    }

    protected override void Run()
    {
        try
        {
            Mouse.Update();
            //canvas.Clear(Color.Black);


            ProcessManger.Update();



            mainBuffer.DrawString(versionString, PCScreenFont.DefaultFont, Color.White, 10, 10);
            canvas.DrawArray(mainBuffer.GetBuffer(), 0, 0, canvas.Width, canvas.Height);

            canvas.DrawFilledCircle(Color.White, MouseManager.X, MouseManager.Y, 2);
            canvas.Display();

            tick++;

            if (tick % gcrate == 0)
            {
                GarbageCollector.Collect();
            }

        }
        catch (Exception ex)
        {
            canvas.Disable();
            Console.WriteLine(ex.Message);
        }

    }


    private int collections, freed, timeInGC;
    private ulong heap;
    private void GCINFO()
    {
        GarbageCollector.GetStats(out collections, out freed);
        heap = GarbageCollector.GetHeapSizeBytes();
        timeInGC = GarbageCollector.GetLastGCPercentTimeInGC();
    }
}
