using System.Drawing;
using Cosmos.Kernel.System.Graphics;
public class ImageView : Component
{
    private Image Image;

    private Rectangle sourceRect;
    private Rectangle targetRect;

    public int scrollMultiplier = 6;
    public bool canResize = true;

    public ImageView(Image image, int x, int y, int width, int height) : base(x, y, width, height)
    {
        Image = image;
        sourceRect = new Rectangle(0, 0, (int)image.Width, (int)image.Height);
        targetRect = new Rectangle(width / 2 - (int)image.Width / 2, height / 2 - (int)image.Height / 2, (int)image.Width, (int)image.Height);
        horizontalAlignment = HorizontalAlignment.Center;
        verticalAlignment = VerticalAlignment.Center;
    }


    public override void DrawLocal()
    {
        //DrawRectangle(Color.Black, 0, 0, Width, Height);
        DrawEtchedRectangle(0, 0, Width, Height);
        DrawImageStretchAlpha(Image, sourceRect, targetRect);
        DrawString($"Scroll: {Mouse.scroll}", Color.White, 5, Height - 15);
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInsideAbsolute(mouseX, mouseY))
            return false;

        float scrollDelta = Mouse.scroll;
        if (scrollDelta != 0 && canResize)
        {
            targetRect.Width -= (int)scrollDelta * scrollMultiplier;
            targetRect.Height -= (int)scrollDelta * scrollMultiplier;
            targetRect.X = Width / 2 - targetRect.Width / 2;
            targetRect.Y = Height / 2 - targetRect.Height / 2;

            targetRect.Width = Math.Clamp(targetRect.Width, 16, Width);
            targetRect.Height = Math.Clamp(targetRect.Height, 16, Height);


            MarkDirty();
        }




        return base.HandleInput(mouseX, mouseY, mouse);
    }


    public override string GetComponentName() => "ImageView";
}
