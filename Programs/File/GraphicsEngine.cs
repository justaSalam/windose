using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Rendering3D;
using Cosmos.Kernel.System.Input;
using System.Diagnostics;
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

        // ── Step 1: Try to obtain a Canvas3D ───────────────────────────────
        Canvas3D? canvas3D = Canvas.GetFullScreen() as Canvas3D;

        if (canvas3D == null)
        {
            // The cached canvas is not 3D.  Check whether the primary display
            // actually supports 3D now (the VMware SVGA driver probes during
            // boot, so by the time a user opens this window it should be ready).
            var display = DisplayManager.Primary;
            if (display != null)
            {
                Console.WriteLine("[SVGA3D] Primary display: " + display.DriverName + " \"" + display.Name + "\"");
                Console.WriteLine("[SVGA3D] IsFirmware: " + display.IsFirmware);
            }

            if (display != null && display.TryGetFacet<ICanvas3DFactory>(out _))
            {
                // The display *does* support 3D, but the canvas was originally
                // created as a plain 2D Canvas (timing issue during boot).
                // Force re-creation so we get a Canvas3D this time.
                Console.WriteLine("[SVGA3D] ICanvas3DFactory available — forcing canvas re-creation...");

                Canvas.DisableFullScreen();
                var newCanvas = Canvas.GetFullScreen(new Mode(1920, 1080, ColorDepth.ColorDepth32));
                Kernel.canvas = newCanvas;
                canvas = newCanvas;
                canvas3D = newCanvas as Canvas3D;

                if (canvas3D != null)
                    Console.WriteLine("[SVGA3D] Canvas3D created successfully: " + newCanvas.Name);
            }

            if (canvas3D == null)
            {
                Console.WriteLine("[SVGA3D] 3D hardware acceleration is not available on this display.");
                Console.WriteLine("[SVGA3D] Ensure VMware settings have:  mks.enable3d = \"TRUE\"");
                Console.WriteLine("[SVGA3D] Check serial/debug output for the Cosmos SVGA driver probe log.");
                return;
            }
        }

        // ── Step 2: Set up scene (runs once, then enters the render loop) ──
        MouseManager.SetScreenSize(canvas3D.Width, canvas3D.Height);
        canvas3D.Camera = new Camera3D(new Vector3(0f, 2.6f, 4.6f), new Vector3(0f, 0.9f, 0f));

        /* One quad per face, four vertices each: the faces share no vertices, so a
           corner does not blend three colors into an unreadable rotation. */
        const float H = 0.5f;
        ReadOnlySpan<Vector3> positions =
        [
            new(H, -H, H), new(H, -H, -H), new(H, H, -H), new(H, H, H),         // +X
            new(-H, -H, -H), new(-H, -H, H), new(-H, H, H), new(-H, H, -H),     // -X
            new(-H, H, H), new(H, H, H), new(H, H, -H), new(-H, H, -H),         // +Y
            new(-H, -H, -H), new(H, -H, -H), new(H, -H, H), new(-H, -H, H),     // -Y
            new(-H, -H, H), new(H, -H, H), new(H, H, H), new(-H, H, H),         // +Z
            new(H, -H, -H), new(-H, -H, -H), new(-H, H, -H), new(H, H, -H),     // -Z
        ];

        ReadOnlySpan<Color> faceColors =
        [
            Color.Crimson, Color.MediumSeaGreen, Color.Gold,
            Color.DarkOrange, Color.DodgerBlue, Color.MediumOrchid,
        ];

        Span<uint> colors = stackalloc uint[positions.Length];
        Span<ushort> indices = stackalloc ushort[faceColors.Length * 6];

        for (int face = 0; face < faceColors.Length; face++)
        {
            uint argb = (uint)faceColors[face].ToArgb();
            int first = face * 4;

            for (int corner = 0; corner < 4; corner++)
            {
                colors[first + corner] = argb;
            }

            /* Two triangles per quad, sharing the 0-2 diagonal. */
            int index = face * 6;
            indices[index] = (ushort)first;
            indices[index + 1] = (ushort)(first + 1);
            indices[index + 2] = (ushort)(first + 2);
            indices[index + 3] = (ushort)(first + 2);
            indices[index + 4] = (ushort)(first + 3);
            indices[index + 5] = (ushort)first;
        }

        Mesh cube = canvas3D.CreateMesh(positions, colors, indices);

        Quaternion orientation = Quaternion.Identity;
        long previous = Stopwatch.GetTimestamp();

        // ── Step 3: Render loop ────────────────────────────────────────────
        while (true)
        {
            long now = Stopwatch.GetTimestamp();
            float elapsed = (float)(now - previous) / Stopwatch.Frequency;
            previous = now;

            /* Where the mouse points, read as a push on the ground plane: 0 at the
               center of the screen, 1 at the edges. */
            Vector3 drive = new(
                (MouseManager.X - canvas3D.Width * 0.5f) / (canvas3D.Width * 0.5f),
                0f,
                (MouseManager.Y - canvas3D.Height * 0.5f) / (canvas3D.Height * 0.5f));

            /* A cube rolling that way turns about the axis perpendicular to both the
               ground normal and the push, on top of a slow idle spin about Y. */
            Vector3 spin = (Vector3.Cross(Vector3.UnitY, drive) * 3.5f) + (Vector3.UnitY * 0.6f);
            orientation = Quaternion.Normalize(Quaternion.Concatenate(
                orientation,
                Quaternion.CreateFromAxisAngle(Vector3.Normalize(spin), spin.Length() * elapsed)));

            canvas3D.ClearScene(Color.FromArgb(0x10, 0x14, 0x20));
            canvas3D.DrawGrid(12, 0.5f, Color.FromArgb(0x30, 0x3A, 0x50));
            canvas3D.DrawMesh(
                cube,
                Matrix4x4.CreateFromQuaternion(orientation) * Matrix4x4.CreateTranslation(0f, 1f, 0f));
            canvas3D.Display();

            Thread.Sleep(16);
        }
    }


    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        base.HandleKeyboard(keyEvent);

        if(keyEvent.Key == Key.Escape)
        {
            ProcessManager.QueueStop(process);
        }

        switch(keyEvent.Key)
        {
            case Key.E:
                position.Y += 1;
                break;

            case Key.Q:
                position.Y -= 1;
                break;

            case Key.W:
                position.X += 1;
                break;

            case Key.S:
                position.X -= 1;
                break;

            case Key.D:
                position.Z += 1;
                break;

            case Key.A:
                position.Z -= 1;
                break;
        }
    }



}
