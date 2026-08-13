using System;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Mouse;
using Windose.System.Kernel;
using Windose.System.System_Calls;
using Sys = Cosmos.Kernel.System;

namespace Windose.System.GUI.Components
{
    internal class Viewport : Component
    {

        private uint context;

        private SVGA3dSurfaceImageId ColorSurface, DepthSurface;

        private uint vao, ebo;

        private Matrix4x4 proj, view;

        private SVGA3dRect viewrect = new(0, 0, 1280, 720);

        private SVGA3dVertexDecl[] vertexListDefinition = [];
        private SVGA3dPrimitiveRange[] batchList = [];

        private float roty = 0;


        private SVGAII3DCanvas canvas;
        public Viewport(int width, int height) : base(0, 0, width, height)
        {
            canvas = Windose.Kernel.Instance.displayDriver.Canvas;



            SystemLogger.WriteLine("Viewport", "Checking driver");

            if (canvas.Driver == null)
            {
                canvas.Clear(Color.Gray);
                canvas.DrawString("3D driver not available. Please ensure that the SVGAII driver is loaded and the device supports 3D acceleration.", SystemFonts.spleen6x12, Color.White, 0, 0);
                canvas.Display();
                SystemLogger.WriteLine("Viewport", "3D driver not available. Please ensure that the SVGAII driver is loaded and the device supports 3D acceleration.", ConsoleMessageType.Fatal);
                return;
            }

            if (!canvas.Driver.Is3DEnabled)
            {
                canvas.Clear(Color.Gray);
                canvas.DrawString("3D driver is not enabled. Please ensure that the SVGAII driver is loaded and the device supports 3D acceleration.", SystemFonts.spleen6x12, Color.White, 0, 0);
                canvas.Display();
                SystemLogger.WriteLine("Viewport", "3D driver is not enabled. Please ensure that the SVGAII driver is loaded and the device supports 3D acceleration.", ConsoleMessageType.Fatal);
                return;
            }
            canvas.Clear(canvas.Driver.Is3DEnabled ? Color.Green : Color.Gray);


            SystemLogger.WriteLine("Viewport", "3D " + canvas.Driver.Is3DEnabled);

            //canvas.DrawString("3D is " + canvas.Driver.Is3DEnabled, SystemFonts.spleen6x12, Color.Black, 0, 0);
            //canvas.DrawString("3D driver is null? " + (canvas.Driver3D == null), SystemFonts.spleen6x12, Color.Black, 0, 0);

            canvas.Display();

            ColorSurface = canvas.Driver3D!.DefineSurface(viewrect.w, viewrect.h, SVGA3dSurfaceFormat.SVGA3D_X8R8G8B8);
            DepthSurface = canvas.Driver3D.DefineSurface(viewrect.w, viewrect.h, SVGA3dSurfaceFormat.SVGA3D_Z_D16);

            context = canvas.Driver3D.DefineContext();

            canvas.Driver3D.SetRenderTarget(context, SVGA3dRenderTargetType.Color, ColorSurface);
            canvas.Driver3D.SetRenderTarget(context, SVGA3dRenderTargetType.Depth, DepthSurface);

            canvas.Driver3D.SetViewport(context, viewrect);
            canvas.Driver3D.SetDepthRange(context, 0, 1);

            ColorVertex[] verticies = [
                new(new(-1,-1,-1), 0xFFFFFF),
            new(new(-1,-1, 1), 0xFFFF00),
            new(new(-1, 1,-1), 0xFF00FF),
            new(new(-1, 1, 1), 0xFF0000),
            new(new( 1,-1,-1), 0x00FFFF),
            new(new( 1,-1, 1), 0x00FF00),
            new(new( 1, 1,-1), 0x0000FF),
            new(new( 1, 1, 1), 0x000000),
        ];

            ushort[] indicies = [
                0, 1, 3, 3, 2, 0, // -X
            4, 5, 7, 7, 6, 4, // +X
            0, 1, 5, 5, 4, 0, // -Y
            2, 3, 7, 7, 6, 2, // +Y
            0, 2, 6, 6, 4, 0, // -Z
            1, 3, 7, 7, 5, 1, // +Z
        ];

            vao = canvas.Driver3D.CreateStaticArrayBuffer(verticies);
            ebo = canvas.Driver3D.CreateStaticArrayBuffer(indicies);

            proj = Matrix4x4.CreatePerspectiveFieldOfView(
                60f * (MathF.PI / 180f),
                viewrect.w / (float)viewrect.h,
                0.1f, 100.0f
            );

            vertexListDefinition = ColorVertex.GetVertexDeclarations(vao);
            batchList = [
                new() {
                primType = SVGA3dPrimitiveType.SVGA3D_PRIMITIVE_TRIANGLELIST,
                primitiveCount = (uint)indicies.Length / 3,
                indexArray = new() {
                    surfaceId = ebo,
                    stride = sizeof(ushort)
                },
                indexWidth = sizeof(ushort)
            }
            ];

            canvas.Driver3D.SetRenderState(context, [
                new(SVGA3dRenderStateName.SVGA3D_RS_SHADEMODE, 2), // smooth
            new(SVGA3dRenderStateName.SVGA3D_RS_LIGHTINGENABLE, 0), // false
            new(SVGA3dRenderStateName.SVGA3D_RS_BLENDENABLE, 0), // false
            new(SVGA3dRenderStateName.SVGA3D_RS_ZENABLE, 1), // true
            new(SVGA3dRenderStateName.SVGA3D_RS_ZWRITEENABLE, 1), // true
            new(SVGA3dRenderStateName.SVGA3D_RS_ZFUNC, 2), // less
            new(SVGA3dRenderStateName.SVGA3D_RS_CULLMODE, 1), // none
        ]);

            canvas.Driver3D.SetTextureState(context, [
                new(SVGA3dTextureStateName.SVGA3D_TS_BIND_TEXTURE, -1), // no texture bound
            new(SVGA3dTextureStateName.SVGA3D_TS_COLOROP, 2), // SELECTARG1
            new(SVGA3dTextureStateName.SVGA3D_TS_COLORARG1, 3), // diffuse
            new(SVGA3dTextureStateName.SVGA3D_TS_ALPHAARG1, 3), // diffuse
        ]);

            canvas.Driver3D.SetTransform(
                context,
                SVGA3dTransformType.SVGA3D_TRANSFORM_WORLD, Matrix4x4.Identity
            );
            canvas.Driver3D.SetTransform(
                context,
                SVGA3dTransformType.SVGA3D_TRANSFORM_PROJECTION, proj
            );
        }

        public override void DrawLocal()
        {
            if (stop)
            {
                return;
            }
            canvas.Driver3D!.Clear3D(context, ClearFlags.Color | ClearFlags.Depth, viewrect, 0x113366);

            roty += 0.001f;

            view =
                Matrix4x4.CreateScale(.5f) *
                Matrix4x4.CreateRotationX(30f * (MathF.PI / 180f)) *
                Matrix4x4.CreateRotationY(roty) *
                Matrix4x4.CreateTranslation(new(0, 0, -3))
            ;

            canvas.Driver3D.SetTransform(
                context,
                SVGA3dTransformType.SVGA3D_TRANSFORM_VIEW, view
            );

            canvas.Driver3D.DrawPrimitives(context, vertexListDefinition, batchList);

            canvas.Driver3D.Present(ColorSurface, viewrect);

        }
        private bool stop;
        public override void HandleKeyboard(KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                stop = true;
            }
        }
    }

}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ColorVertex(Vector3 position, uint color)
{
    public Vector3 Position { get; set; } = position;
    public uint Color { get; set; } = color;

    public static SVGA3dVertexDecl[] GetVertexDeclarations(uint vao) => [
        new() {
            identity = new() {
                type = SVGA3dDeclType.SVGA3D_DECLTYPE_FLOAT3,
                usage = SVGA3dDeclUsage.SVGA3D_DECLUSAGE_POSITION,
            },
            array = new() {
                surfaceId = vao,
                stride = Size,
                offset = 0
            }
        },
        new() {
            identity = new() {
                type = SVGA3dDeclType.SVGA3D_DECLTYPE_D3DCOLOR,
                usage = SVGA3dDeclUsage.SVGA3D_DECLUSAGE_COLOR,
            },
            array = new() {
                surfaceId = vao,
                stride = Size,
                offset = 3 * 4
            }
        }
    ];

    public static uint Size = (3 * 4) + (1 * 4);
}