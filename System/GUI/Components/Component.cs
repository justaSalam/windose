using System.Drawing;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;
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

    public int Width
    {
        get
        {
            return rectangle.Width;
        }
        set
        {
            rectangle.Width = value;
        }

    }


    public int Height
    {
        get
        {
            return rectangle.Height;
        }
        set
        {
            rectangle.Height = value;
        }
    }


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

    public string text = "";


    protected DirectBitmap buffer;
    private DirectBitmap cacheBuffer;
    public Rectangle rectangle;
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

    public static int currentZIndex = 0;
    public int zIndex;
    public DrawLayer zLayer;

    public Thickness Margin = new Thickness(5);
    public Thickness Padding = new Thickness(5);

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

        dirty = false;
        visible = true;
        isRoot = true;

        zIndex = currentZIndex;

        state = State.Normal;

        components.Add(this);
        MarkDirty();
        currentZIndex++;

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
        DrawLocal();
        DrawToScreen();
    }

    public virtual void DrawLocal()
    {
    }
    public virtual bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        for (int i = children.Count - 1; i >= 0; i--)
        {
            Component child = children[i];

            if (!child.Visible)
                continue;

            if (!child.IsInsideAbsolute(mouseX, mouseY))
                continue;

            if (child.HandleInput(mouseX, mouseY, mouse))
                return true;
        }

        return false;
    }

    public virtual void HandleKeyboard(KeyEvent keyEvent)
    {
        Serial.WriteString($"[keyevent] | {GetName()} {keyEvent.Key}\n");

    }

    public void ResolveHorizontalAnchor()
    {
        if (isRoot || parent == null) return;

        Rectangle oldRectangle = rectangle;

        switch (horizontalAlignment)
        {
            case HorizontalAlignment.Left:
                X = Margin.left;
                break;

            case HorizontalAlignment.Center:
                X = (parent.Width - Width) / 2;
                break;

            case HorizontalAlignment.Right:
                X = parent.Width - Width - Margin.right;

                break;

            case HorizontalAlignment.Stretch:
                X = Margin.left;
                Resize(parent.Width - Margin.left - Margin.right, Height);
                break;
        }

        WindowManager.Invalidate(oldRectangle);
        WindowManager.Invalidate(rectangle);
    }
    public void ResolveVerticalAnchor()
    {
        if (isRoot || parent == null) return;

        Rectangle oldRectangle = rectangle;

        switch (verticalAlignment)
        {
            case VerticalAlignment.Top:
                Y = Margin.top;
                break;

            case VerticalAlignment.Center:
                Y = (parent.Height - Height) / 2;

                break;

            case VerticalAlignment.Bottom:
                Y = parent.Height - Height - Margin.bottom;

                break;

            case VerticalAlignment.Stretch:
                Y = Margin.top;
                Resize(Width, parent.Height - Margin.top - Margin.bottom);
                break;
        }

        WindowManager.Invalidate(oldRectangle);
        WindowManager.Invalidate(rectangle);
    }
    public void ResolveChildren()
    {
        foreach (Component child in children)
        {
            child.ResolveHorizontalAnchor();
            child.ResolveVerticalAnchor();

            child.MarkDirty();
        }
    }

    public void DrawToScreen()
    {
        Kernel.mainBuffer.DrawArrayClipped(buffer.GetBuffer(), buffer.Width, 0, 0, X, Y, Width, Height);
    }

    public void DrawToScreen(Rectangle dirtyRect)
    {
        Rectangle clipped = Rectangle.Intersect(rectangle, dirtyRect);
        if (clipped.Width <= 0 || clipped.Height <= 0) return;

        Kernel.mainBuffer.DrawArrayClipped(
            buffer.GetBuffer(),
            buffer.Width,
            clipped.X - X,
            clipped.Y - Y,
            clipped.X,
            clipped.Y,
            clipped.Width,
            clipped.Height);
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

    /// <summary>
    /// Screen space coordinates
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public bool IsInsideAbsolute(int x, int y)
    {
        return x >= AbsoluteX && x <= AbsoluteX + Width && y >= AbsoluteY && y <= AbsoluteY + Height;
    }

    /// <summary>
    /// Window Space coordinates
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public bool IsInsideLocal(int x, int y)
    {
        return x >= X && x <= X + Width && y >= Y && y <= Y + Height;
    }

    /// <summary>
    /// Returns a child component at given absolute coordinates
    /// </summary>
    /// <param name="mouseX"></param>
    /// <param name="mouseY"></param>
    /// <returns></returns>
    public Component? GetChildAt(int mouseX, int mouseY)
    {
        for (int i = children.Count - 1; i >= 0; i--)
        {
            Component child = children[i];

            if (!child.Visible)
                continue;

            if (!child.IsInsideAbsolute(mouseX, mouseY))
                continue;

            Component? nested = child.GetChildAt(mouseX, mouseY);
            if (nested != null)
                return nested;

            return child;
        }

        return null;
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

        Rectangle oldRectangle = rectangle;
        rectangle = new Rectangle(X, Y, width, height);

        if (width > buffer.Width || height > buffer.Height)
        {
            int newBufferWidth = RoundUpToChunk(Math.Max(width, buffer.Width), 64);
            int newBufferHeight = RoundUpToChunk(Math.Max(height, buffer.Height), 64);

            buffer = new DirectBitmap(newBufferWidth, newBufferHeight);
            cacheBuffer = new DirectBitmap(newBufferWidth, newBufferHeight);
        }

        WindowManager.Invalidate(oldRectangle);
        WindowManager.Invalidate(rectangle);
        ResolveChildren();
        MarkDirty();
    }



    private static int RoundUpToChunk(int value, int chunkSize)
    {
        return (value + chunkSize - 1) / chunkSize * chunkSize;
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

    public void AddChild(Component child)
    {
        child.isRoot = false;
        child.parent = this;

        child.ResolveHorizontalAnchor();
        child.ResolveVerticalAnchor();
        components.Remove(child);
        children.Add(child);
        MarkDirty();
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

    public void DrawString(string str, Color color, int x, int y, int fontSize)
    {
        buffer.DrawString(str, PCScreenFont.DefaultFont, color, x, y, fontSize);
    }

    public int MeasureStringWidth(string str, int fontSize)
    {
        return str.Length * Math.Max(1, PCScreenFont.DefaultFont.Width * fontSize / PCScreenFont.DefaultFont.Height);
    }

    public int MeasureStringHeight(int fontSize)
    {
        return fontSize;
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

    public void OverrideLabel(string text)
    {
        this.text = text;
        MarkDirty();
    }

    public virtual bool IsDirty() => dirty;

    public virtual void MarkDirty()
    {
        if (dirty) return;
        dirty = true;

        if (isRoot) WindowManager.Invalidate(this);
        else parent.MarkDirty();

    }
    public virtual void ForceDirty()
    {
        forceDirty = true;
        WindowManager.Invalidate(this);
    }
    public virtual void ClearForceDirty()
    {
        forceDirty = false;
    }

    public virtual void MarkCleaned()
    {
        dirty = false;
        forceDirty = false;
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
