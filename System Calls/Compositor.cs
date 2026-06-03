using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;
using Mutex = Cosmos.Kernel.Core.Scheduler.Mutex;

public class Compositor
{
    public static Compositor Instance;
    // each process gets one slot, not a queue
    private Dictionary<int, Action<Canvas>> drawCalls = new Dictionary<int, Action<Canvas>>();

    private Canvas canvas; //Main Fullscreen Canvas
    public Compositor(Canvas canvas)
    {
        Instance = this;
        this.canvas = canvas;
    }




    public void SetDrawCall(int processId, Action<Canvas> action)
    {
        lock (drawCalls)
        {
            drawCalls[processId] = action; // replace, never accumulate
        }
    }

    public void Flush()
    {
        for (int i = 0; i < drawCalls.Count; i++)
            drawCalls[i]?.Invoke(canvas);

    }


}