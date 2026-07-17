using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;

public class Logger
{
    private Canvas canvas;

    public Queue<string> logs = new Queue<string>();

    private int lastLogTimeout;
    public Logger(Canvas canvas)
    {
        this.canvas = canvas;
    }

    public void Enqueue(string log) => logs.Enqueue(log);



    public void Update()
    {

    }

}