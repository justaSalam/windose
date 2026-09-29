using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using System.Drawing;
using System.Numerics;
using Windose;

public class GraphicsEngine : Window
{
    private Canvas canvas;

    private Vector3 position;
    public GraphicsEngine(int x = 180, int y = 120, int width = 660, int height = 460) : base(x, y, width, height, "SVGA3D Graphics Viewport Test", true)
    {
        canvas = Canvas.GetFullScreen();
    }

    public override void Update()
    {
        base.Update();


        if (canvas is Canvas3D canvas3D)
        {
            canvas3D.Camera = new Camera3D(new Vector3(0f, 0f, 5f), Vector3.Zero);
            canvas3D.ClearScene(Color.Black);
            canvas3D.DrawCube(position, new Vector3(1f, 1f, 1f), Color.OrangeRed);

            

            canvas3D.DrawGrid(10, 1f, Color.DimGray);
            Kernel.mainBuffer.DrawArray(canvas3D.GetBuffer(), 0,0,canvas3D.Width, canvas3D.Height);

        }
        else
        {
            DrawString("3D not supported", Color.White, 0, 0, 16);
        }
    }


    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        base.HandleKeyboard(keyEvent);

        if(keyEvent.Key == ConsoleKeyEx.Escape)
        {
            ProcessManger.QueueStop(process);
        }

        switch(keyEvent.Key)
        {
            case ConsoleKeyEx.E:
                position.Y += 1;
                break;

            case ConsoleKeyEx.Q:
                position.Y -= 1;
                break;

            case ConsoleKeyEx.W:
                position.X += 1;
                break;

            case ConsoleKeyEx.S:
                position.X -= 1;
                break;

            case ConsoleKeyEx.D:
                position.Z += 1;
                break;

            case ConsoleKeyEx.A:
                position.Z -= 1;
                break;
        }
    }



}
