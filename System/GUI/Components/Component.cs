using System.Drawing;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Mouse;
using Windose;


/// <summary>
/// A base for every GUI object
/// </summary>
public class Component : IDisposable
{

    public Bitmap GetBuffer() => buffer.GetBufferBitmap;
    public int[] GetRawBuffer() => buffer.GetBufferBitmap.RawData;
    public virtual string GetName() => "UNASSIGNED COMPONENT";

    public int Width => rectangle.Width;


    public int Height => rectangle.Height;


    public bool Visible
    {
        get
        {
            return visible;
        }
        set
        {
            if (visible != value)
            {
                visible = value;
                foreach (Component component in children)
                {
                    component.Visible = value;
                }
            }
        }
    }
    public int AbsoluteX
    {
        get
        {
            return _absoluteX;
        }
    }
    public int AbsoluteY
    {
        get
        {
            return _absoluteY;
        }
    }

    public int X
    {
        get
        {
            return rectangle.X;
        }
        set
        {
            rectangle.X = value;
            ComputeAbsoluteX();
        }
    }
    public int Y
    {
        get
        {
            return rectangle.Y;
        }
        set
        {
            rectangle.Y = value;
            ComputeAbsoluteY();
        }
    }

    public static List<Component> components = new List<Component>();
    public List<Component> children = new List<Component>();
    private Component parent;

    protected DirectBitmap buffer;
    private DirectBitmap cacheBuffer;
    protected Rectangle rectangle;
    public State state;

    protected bool dirty;
    public bool forceDirty { get; private set; }
    protected bool visible;
    public bool isRoot;

    protected Frame frame;
    protected Frame normalFrame;
    protected Frame highlightedFrame;
    protected Frame pressedFrame;

    protected int _absoluteX;
    protected int _absoluteY;
    protected int zIndex;

    protected Thickness Margin = new Thickness(5);
    protected Thickness Padding = new Thickness(5);

    public HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left;
    public VerticalAlignment verticalAlignment = VerticalAlignment.Top;

    public Component(int x, int y, int width, int height, Thickness margin = new Thickness(), Thickness padding = new Thickness(), HorizontalAlignment horizontal = HorizontalAlignment.Left, VerticalAlignment vertical = VerticalAlignment.Top)
    {
        Margin = margin;
        Padding = padding;
        horizontalAlignment = horizontal;
        verticalAlignment = vertical;

        Init(x, y, width, height);
    }

    private void Init(int x, int y, int width, int height)
    {
        rectangle = new Rectangle(x, y, width, height);
        buffer = new DirectBitmap(rectangle.Width, rectangle.Height);
        cacheBuffer = new DirectBitmap(rectangle.Width, rectangle.Height);

        children = new List<Component>();

        dirty = true;
        visible = true;
        isRoot = true;

        zIndex = 0;

        state = State.Normal;

        MarkDirty();
        components.Add(this);

        ComputeAbsoluteCoordinates();
    }
    public virtual void Update()
    {
        bool isInside = IsInsideAbsolute(MouseManager.X, MouseManager.Y);
        State prevState = state;

        if (isInside && state != State.Highlighted)
        {
            state = State.Highlighted;
            MarkDirty();
        }
        else if (!isInside && state != State.Normal)
        {
            state = State.Normal;
            MarkDirty();
        }

        if (prevState != state)
        {
            UpdateFrame();
        }
    }

    public void UpdateFrame()
    {
        switch (state)
        {
            case State.Normal:
                frame = normalFrame;
                break;
            case State.Highlighted:
                frame = highlightedFrame;
                break;
            case State.Pressed:
                frame = pressedFrame;
                break;
        }
    }
    /// <summary>
    /// -- Call last --
    /// Copies the component buffer into the main screen buffer
    /// </summary>
    public virtual void Draw()
    {
        Kernel.mainBuffer.DrawArray(buffer.GetBuffer(), X, Y, Width, Height);
    }

    public virtual void Draw(Component component)
    {
        Draw();
        component.buffer.DrawImageAlpha(GetBuffer(), X, Y);
    }

    public void DrawInParent()
    {
        if (!isRoot)
        {
            parent.buffer.DrawImageAlpha(GetBuffer(), X, Y);
        }
    }


    public void SaveCacheBuffer()
    {
        cacheBuffer.DrawImage(buffer.GetBufferBitmap, 0, 0);
    }

    public void DrawCacheBuffer()
    {
        buffer.DrawImage(cacheBuffer.GetBufferBitmap, 0, 0);
    }

    public bool IsInsideAbsolute(int x, int y)
    {
        return x >= AbsoluteX && x <= AbsoluteX + Width && y >= AbsoluteY && y <= AbsoluteY + Height;
    }

    public bool IsInsideLocal(int x, int y)
    {
        return x >= X && x <= X + Width && y >= Y && y <= Y + Height;
    }

    public virtual void Resize(int width, int height)
    {
        if (width <= 0 || width >= 1000 || height <= 0 || height >= 1000)
        {
            return;
        }

        if (width % 2 != 0)
        {
            width++;
        }

        foreach (Component child in children)
        {
            if (child is Button)
            {
                child.X += width - Width;
            }
        }

        rectangle = new Rectangle(Y, X, Y + height, X + width);
        buffer = new DirectBitmap(width, height);
        cacheBuffer = new DirectBitmap(width, height);

        Draw();
        foreach (Component child in children)
        {
            if (child is Button)
            {
                child.DrawInParent();
            }
        }
        SaveCacheBuffer();
    }


    public void ComputeAbsoluteCoordinates()
    {
        ComputeAbsoluteX();
        ComputeAbsoluteY();
    }

    public void ComputeAbsoluteX()
    {
        int absoluteX = 0;
        Component currentComponent = this;

        while (currentComponent != null)
        {
            absoluteX += currentComponent.X;

            if (currentComponent.isRoot) break;

            currentComponent = currentComponent.parent;
        }

        _absoluteX = absoluteX;

        for (int i = 0; i < children.Count; i++)
        {
            Component child = children[i];
            child.ComputeAbsoluteX();
        }
    }

    public void ComputeAbsoluteY()
    {
        int absoluteY = 0;
        Component currentComponent = this;

        while (currentComponent != null)
        {
            absoluteY += currentComponent.Y;

            if (currentComponent.isRoot) break;

            currentComponent = currentComponent.parent;
        }

        _absoluteY = absoluteY;

        for (int i = 0; i < children.Count; i++)
        {
            Component child = children[i];
            child.ComputeAbsoluteY();
        }
    }

    public void Clear(Color color)
    {
        buffer.Clear(color);
    }

    public void Clear()
    {
        buffer.Clear(Color.LightGray);
    }

    public void DrawString(string str, Color color, int x, int y)
    {
        buffer.DrawString(str, PCScreenFont.DefaultFont, color, x, y);
    }

    public void DrawString(string str, Font font, Color color, int x, int y)
    {
        buffer.DrawString(str, font, color, x, y);
    }

    public void DrawChar(char c, Font font, Color color, int x, int y)
    {
        buffer.DrawChar(c, font, color, x, y);
    }

    public void DrawString(string str, int x, int y)
    {
        buffer.DrawString(str, PCScreenFont.DefaultFont, Color.Black, x, y);
    }

    public void DrawFilledRectangle(Color color, int xStart, int yStart, int width, int height)
    {
        buffer.DrawFilledRectangle(color, xStart, yStart, width, height);
    }
    public void DrawRectangle(Color color, int x, int y, int width, int height)
    {
        buffer.DrawRectangle(color, x, y, width, height);
    }




    public void DrawLine(Color color, int xStart, int yStart, int width, int height)
    {
        buffer.DrawLine(color, xStart, yStart, width, height);
    }

    public void DrawImage(Bitmap image, int x, int y)
    {
        buffer.DrawImageAlpha(image, x, y);
    }

    public void DrawFilledCircle(Color color, int x, int y, int radius)
    {
        buffer.DrawFilledCircle(color, x, y, radius);
    }

    public void DrawGradient(Color color1, Color color2, int x, int y, int width, int height)
    {
        for (int i = 0; i < width; i++)
        {
            // Calculate the ratio of the current position relative to the total width
            float ratio = (float)i / width;

            // Interpolate the RGB values based on the ratio
            byte r = (byte)((color2.R - color1.R) * ratio + color1.R);
            byte g = (byte)((color2.G - color1.G) * ratio + color1.G);
            byte b = (byte)((color2.B - color1.B) * ratio + color1.B);

            int interpolatedColor = Color.FromArgb(0xff, r, g, b).ToArgb();

            for (int j = 0; j < height; j++)
            {
                buffer.SetPixelAlpha(x + i, y + j, interpolatedColor);
            }
        }
    }

    public virtual bool IsDirty()
    {
        return dirty;
    }

    public virtual void MarkDirty()
    {
        dirty = true;
    }
    public virtual void ForceDirty()
    {
        forceDirty = true;
    }
    public virtual void ClearForceDirty()
    {
        forceDirty = false;
    }

    public virtual void MarkCleaned()
    {
        dirty = false;
    }



    public void Dispose()
    {
        foreach (Component child in children)
        {
            child.Dispose();
        }

        components.Remove(this);
    }


}

public enum State
{
    Normal, Highlighted, Pressed
}