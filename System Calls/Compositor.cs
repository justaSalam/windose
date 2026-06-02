using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;
using Mutex = Cosmos.Kernel.Core.Scheduler.Mutex;

public class Compositor
{
    public static Compositor Instance;
    private Queue<Action<Canvas>> queue = new Queue<Action<Canvas>>();

    private Canvas canvas; //Main Fullscreen Canvas
    private Mutex mutex = new Mutex();
    public Compositor(Canvas canvas)
    {
        Instance = this;
        this.canvas = canvas;
    }

    public void Enqueue(Action<Canvas> action)
    {
        mutex.Acquire();
        queue.Enqueue(action);
        mutex.Release();
    }

    public void Flush()
    {
        Queue<Action<Canvas>> snapshot = null;
        using (mutex)
        {
            if (queue != null && queue.Count > 0)
            {
                snapshot = queue;
                queue = new Queue<Action<Canvas>>(); // fresh empty queue
            }

            if (snapshot == null) return;

            while (snapshot.Count > 0)
                snapshot.Dequeue().Invoke(canvas);

            canvas.Display();
        }
        /*
        mutex.Acquire();
        if (queue != null && queue.Count > 0)
        {
            snapshot = queue;
            queue = new Queue<Action<Canvas>>(); // fresh empty queue
        }
        mutex.Release();

        if (snapshot == null) return;

        while (snapshot.Count > 0)
            snapshot.Dequeue().Invoke(canvas);

        canvas.Display();*/
    }


    public void Update()
    {

    }

}