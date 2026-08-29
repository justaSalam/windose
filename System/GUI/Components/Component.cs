using System.Drawing;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Runtime;
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

    public int[] GetBuffer() => buffer.GetBufferBitmap;
    public virtual string GetComponentName() => "UNASSIGNED COMPONENT";

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
                dirty = true;
                WindowManager.Invalidate(this);
                if (!isRoot && parent != null)
                    parent.MarkChildDirty();

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

    public Rectangle AbsoluteRectangle
    {
        get
        {
            return new Rectangle(AbsoluteX, AbsoluteY, Width, Height);
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
    public Component Parent => parent;
    public virtual bool HandlesMouseWheel => false;

    public Window? GetOwningWindow()
    {
        Component current = this;
        while (current != null)
        {
            if (current is Window window) return window;
            current = current.parent;
        }
        return null;
    }


    //TODO Replace text with label for proper component use
    public string text = string.Empty;


    protected DirectBitmap buffer;
    private DirectBitmap cacheBuffer;
    public Rectangle rectangle;
    public Rectangle clampedBounds = new Rectangle(0, 0, 50, 50);
    public State state;

    public bool capturesInput = true;
    protected bool dirty;
    protected bool childrenDirty;
    private bool disposed;
    public bool clampSize = true;
    public bool forceDirty { get; private set; }
    protected bool visible;
    private byte opacity = 255;
    public bool isRoot;

    public byte Opacity
    {
        get => opacity;
        set
        {
            byte clamped = value;
            if (opacity == clamped) return;
            opacity = clamped;
            MarkDirty();
        }
    }

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


    public Action leftClickAction;
    public Action rightClickAction;
    public Action middleClickAction;


    public MenuPopup popup;


    public bool useRightClick = false;


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

        children = new List<Component>();


        dirty = false;
        visible = true;
        isRoot = true;

        if (useRightClick)
        {
            popup = new MenuPopup(220, 28 * 8);
            popup.AddItem("Close", () =>
            {
                popup.Hide();
            });

            rightClickAction += () =>
            {
                popup.ShowAt(MouseManager.X, MouseManager.Y);
            };
        }

        zIndex = currentZIndex;

        state = State.Normal;

        ComputeAbsoluteCoordinates();

        components.Add(this);
        MarkDirty();
        currentZIndex++;


        //AddChild(label);
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

        for (int i = 0; i < children.Count; i++)
        {
            Component child = children[i];
            if (child.Visible)
                child.Update();
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
        if (IsInsideAbsolute(mouseX, mouseY) && mouse.left == MouseEvents.Release) leftClickAction?.Invoke();
        if (IsInsideAbsolute(mouseX, mouseY) && mouse.right == MouseEvents.Release) rightClickAction?.Invoke();
        if (IsInsideAbsolute(mouseX, mouseY) && mouse.middle == MouseEvents.Release) middleClickAction?.Invoke();


        for (int i = children.Count - 1; i >= 0; i--)
        {
            Component child = children[i];

            if (!child.Visible)
            {
                continue;
            }

            if (!child.IsInsideAbsolute(mouseX, mouseY))
            {
                continue;
            }


            return child.HandleInput(mouseX, mouseY, mouse);

        }


        return true;
    }

    public virtual void HandleKeyboard(KeyEvent keyEvent)
    {
    }

    protected static bool IsControlPressed(KeyEvent keyEvent)
        => (keyEvent.Modifiers & ConsoleModifiers.Control) != 0 || KeyboardManager.ControlPressed;

    protected static bool IsShiftPressed(KeyEvent keyEvent)
        => (keyEvent.Modifiers & ConsoleModifiers.Shift) != 0 || KeyboardManager.ShiftPressed;

    protected static char GetPrintableCharacter(KeyEvent keyEvent)
    {
        switch (keyEvent.Key)
        {
            case ConsoleKeyEx.LeftArrow:
            case ConsoleKeyEx.RightArrow:
            case ConsoleKeyEx.UpArrow:
            case ConsoleKeyEx.DownArrow:
            case ConsoleKeyEx.Home:
            case ConsoleKeyEx.End:
            case ConsoleKeyEx.PageUp:
            case ConsoleKeyEx.PageDown:
            case ConsoleKeyEx.Backspace:
            case ConsoleKeyEx.Delete:
            case ConsoleKeyEx.Enter:
            case ConsoleKeyEx.Tab:
            case ConsoleKeyEx.Escape:
                return '\0';
        }

        char value = keyEvent.KeyChar;
        if (value == '\0') return '\0';

        if (!IsShiftPressed(keyEvent)) return value;

        if (value >= 'a' && value <= 'z')
            return (char)(value - 32);

        switch (value)
        {
            case '1': return '!';
            case '2': return '@';
            case '3': return '#';
            case '4': return '$';
            case '5': return '%';
            case '6': return '^';
            case '7': return '&';
            case '8': return '*';
            case '9': return '(';
            case '0': return ')';
            case '-': return '_';
            case '=': return '+';
            case '[': return '{';
            case ']': return '}';
            case '\\': return '|';
            case ';': return ':';
            case '\'': return '"';
            case ',': return '<';
            case '.': return '>';
            case '/': return '?';
            case '`': return '~';
            default: return value;
        }
    }

    public virtual void HandleMessage(UiMessage message)
    {
    }


    public virtual void ResolveHorizontalAnchor()
    {
        if (isRoot || parent == null) return;

        Rectangle oldRectangle = ToAbsoluteRectangle(rectangle);

        switch (horizontalAlignment)
        {
            case HorizontalAlignment.Left:
                // Fixed-position controls keep the X supplied by their parent or constructor.
                // Layout containers apply margins when they position their children.
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
        WindowManager.Invalidate(this);
    }
    public virtual void ResolveVerticalAnchor()
    {
        if (isRoot || parent == null) return;

        Rectangle oldRectangle = ToAbsoluteRectangle(rectangle);

        switch (verticalAlignment)
        {
            case VerticalAlignment.Top:
                // Fixed-position controls keep the Y supplied by their parent or constructor.
                // Layout containers apply margins when they position their children.
                break;

            case VerticalAlignment.Center:
                Y = ((parent.Height - Height) / 2) + Margin.top;

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
        WindowManager.Invalidate(this);
    }
    public void ResolveChildren()
    {
        foreach (Component child in children)
        {
            child.PrepareLayout();
            child.ResolveHorizontalAnchor();
            child.ResolveVerticalAnchor();

            child.MarkDirty();
        }
    }

    public virtual void PrepareLayout()
    {
    }

    public void DrawToScreen()
    {
        // Always use alpha-blended copy so that pixels with alpha < 255
        // (e.g. glass/translucent theme colors) blend with the desktop
        // background behind this component.
        Kernel.mainBuffer.DrawArrayAlphaClipped(
            buffer.GetBuffer(),
            buffer.Width,
            0,
            0,
            AbsoluteX,
            AbsoluteY,
            Math.Min(Width, buffer.Width),
            Math.Min(Height, buffer.Height));
    }

    public void DrawToScreen(Rectangle dirtyRect)
    {
        Rectangle clipped = Rectangle.Intersect(AbsoluteRectangle, dirtyRect);
        if (clipped.Width <= 0 || clipped.Height <= 0) return;

        // Always use alpha-blended copy for proper transparency compositing.
        Kernel.mainBuffer.DrawArrayAlphaClipped(
            buffer.GetBuffer(),
            buffer.Width,
            clipped.X - AbsoluteX,
            clipped.Y - AbsoluteY,
            clipped.X,
            clipped.Y,
            Math.Min(clipped.Width, buffer.Width - (clipped.X - AbsoluteX)),
            Math.Min(clipped.Height, buffer.Height - (clipped.Y - AbsoluteY)));
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
    /// Checks for a point within reference
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="reference"></param>
    /// <returns>If a point in space is within reference</returns>
    public bool Contains(int x, int y, Rectangle reference)
    {
        return x >= reference.X && x <= reference.X + reference.Width && y >= reference.Y && y <= rectangle.Y + reference.Height;
    }

    /// <summary>
    /// Returns a child component at given absolute coordinates
    /// </summary>
    /// <param name="mouseX"></param>
    /// <param name="mouseY"></param>
    /// <returns>A child at a given point on screen</returns>
    public Component? GetChildAt(int mouseX, int mouseY)
    {
        for (int i = children.Count - 1; i >= 0; i--)
        {
            Component child = children[i];

            if (!child.Visible || !child.capturesInput)
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
        if (clampSize)
        {

            if (width <= clampedBounds.Width || width >= 2000 || height <= clampedBounds.Height || height >= 2000)
            {
                return;
            }
        }

        if (width % 2 != 0)
        {
            width++;
        }

        if (width == Width && height == Height)
            return;

        /*foreach (Component child in children)
        {
            if (child is Button)
            {
                child.X += width - Width;
            }
        }*/

        Rectangle oldRectangle = ToAbsoluteRectangle(rectangle);
        rectangle = new Rectangle(X, Y, width, height);

        if (isRoot && (width > buffer.Width || height > buffer.Height))
        {
            int newBufferWidth = RoundUpToChunk(Math.Max(width, buffer.Width), 64);
            int newBufferHeight = RoundUpToChunk(Math.Max(height, buffer.Height), 64);

            buffer = new DirectBitmap(newBufferWidth, newBufferHeight);

            for (int i = 0; i < children.Count; i++)
                children[i].BindRenderSurface(buffer);

            if (cacheBuffer != null)
                cacheBuffer = new DirectBitmap(newBufferWidth, newBufferHeight);
        }

        WindowManager.Invalidate(oldRectangle);
        WindowManager.Invalidate(this);
        ResolveChildren();
        MarkDirty();
    }

    public Rectangle ToAbsoluteRectangle(Rectangle localRectangle)
    {
        return new Rectangle(
            AbsoluteX - X + localRectangle.X,
            AbsoluteY - Y + localRectangle.Y,
            localRectangle.Width,
            localRectangle.Height);
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

    public virtual Component AddChild(Component child)
    {
        child.isRoot = false;
        child.parent = this;
        zIndex++;

        child.ResolveHorizontalAnchor();
        child.ResolveVerticalAnchor();
        child.BindRenderSurface(buffer);
        components.Remove(child);
        children.Add(child);
        MarkDirty();

        return child;
    }

    public virtual void RemoveChild(Component child)
    {
        if (!children.Remove(child)) return;

        WindowManager.Invalidate(child.AbsoluteRectangle);
        child.isRoot = true;
        components.Remove(child);
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
        buffer.DrawString(str, SystemFonts.spleen8x16, color, x, y);
    }

    //TODO Legacy method used with fontSize parameter, which is not used in the current implementation
    //TODO Implement TTF font rendering in the future to support different font sizes + FIX THIS IMPLEMENTATION OH MY FUCKING GOD
    public void DrawString(string str, Color color, int x, int y, int fontSize)
    {
        buffer.DrawString(str, SystemFonts.spleen8x16, color, x, y);
    }

    public void DrawString(string str, Font font, Color color, int x, int y)
    {
        buffer.DrawString(str, font, color, x, y);
    }

    public int MeasureStringWidth(string str, int fontSize)
    {
        return str.Length * Math.Max(1, PCScreenFont.DefaultFont.Width * fontSize / PCScreenFont.DefaultFont.Height);
    }

    public int MeasureStringWidth(string str, Font font)
    {
        return str.Length * font.Width;
    }

    public int MeasureStringHeight(int fontSize)
    {
        return fontSize;
    }

    public void DrawFilledRectangle(Color color, int xStart, int yStart, int width, int height)
    {
        buffer.DrawFilledRectangle(color, xStart, yStart, width, height);
    }
    public void DrawRectangle(Color color, int x, int y, int width, int height)
    {
        buffer.DrawRectangle(color, x, y, width, height);
    }

    public void DrawRaisedRectangle(int x, int y, int width, int height)
    {
        buffer.DrawRaisedRect(x, y, width, height);
    }

    public void DrawRaisedRectangle(int x, int y, int width, int height, Color face, Color highlight, Color shadow, Color darkShadow)
    {
        buffer.DrawRaisedRect(x, y, width, height, face, highlight, shadow, darkShadow);
    }

    public void DrawSunkenRectangle(int x, int y, int width, int height)
    {
        buffer.DrawSunkenRect(x, y, width, height);
    }

    public void DrawSunkenRectangle(int x, int y, int width, int height, Color face, Color darkShadow, Color shadow, Color highlight)
    {
        buffer.DrawSunkenRect(x, y, width, height, face, darkShadow, shadow, highlight);
    }

    public void DrawEtchedRectangle(int x, int y, int width, int height)
    {
        buffer.DrawEtchedRect(x, y, width, height);
    }

    public void DrawEtchedRectangle(int x, int y, int width, int height, Color shadow, Color highlight)
    {
        buffer.DrawEtchedRect(x, y, width, height, shadow, highlight);
    }

    public void DrawLine(Color color, int xStart, int yStart, int width, int height)
    {
        buffer.DrawLine(color, xStart, yStart, width, height);
    }

    public void DrawImage(Image image, int x, int y)
    {
        buffer.DrawImageAlpha(image, x, y);
    }
    public void DrawImageStretchAlpha(Image image, Rectangle sourceRect, Rectangle destRect)
    {
        buffer.DrawImageStretchAlpha(image, sourceRect, destRect);
    }

    public void DrawImageStretchAlpha(Image image, int x, int y, int width, int heigth)
    {
        buffer.DrawImageStretchAlpha(image, new Rectangle(x, y, (int)image.Width, (int)image.Height), new Rectangle(x, y, width, heigth));
    }
    public void DrawFilledCircle(Color color, int x, int y, int radius)
    {
        buffer.DrawFilledCircle(color, x, y, radius);
    }

    public void DrawCircle(Color color, int xCenter, int yCenter, int radius)
    {
        buffer.DrawCircle(color, xCenter, yCenter, radius);
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

    public virtual bool HasDirtyTree() => dirty || childrenDirty || forceDirty;
    public virtual bool IsOpaqueForCopy() => opacity >= 255;

    public virtual void MarkDirty(bool invalidate = true)
    {
        if (invalidate)
            WindowManager.Invalidate(this);

        dirty = true;

        if (!isRoot)
            parent.MarkChildDirty();

    }

    protected void InvalidateLocalRegion(Rectangle localRegion)
    {
        Rectangle bounds = new Rectangle(0, 0, Width, Height);
        Rectangle clipped = Rectangle.Intersect(bounds, localRegion);
        if (clipped.Width <= 0 || clipped.Height <= 0) return;

        WindowManager.Invalidate(new Rectangle(
            AbsoluteX + clipped.X,
            AbsoluteY + clipped.Y,
            clipped.Width,
            clipped.Height));

        if (!isRoot && parent != null)
            parent.MarkChildDirty();
    }

    protected virtual void MarkChildDirty()
    {
        childrenDirty = true;

        if (!isRoot)
            parent.MarkChildDirty();
    }

    public virtual void DrawDirtyLocal(Rectangle dirtyRect)
    {
        if (isRoot)
        {
            Rectangle localClip = new Rectangle(
                dirtyRect.X - AbsoluteX,
                dirtyRect.Y - AbsoluteY,
                dirtyRect.Width,
                dirtyRect.Height);
            localClip = Rectangle.Intersect(new Rectangle(0, 0, Width, Height), localClip);
            if (localClip.Width <= 0 || localClip.Height <= 0) return;

            buffer.ResetContext(localClip);
            try
            {
                DrawLocal();
            }
            finally
            {
                buffer.ResetContext();
            }
            return;
        }

        if (dirty || forceDirty)
        {
            DrawLocal();
            return;
        }

        if (!childrenDirty)
            return;

        for (int i = 0; i < children.Count; i++)
        {
            Component child = children[i];
            if (!child.Visible) continue;
            if (!child.AbsoluteRectangle.IntersectsWith(dirtyRect)) continue;

            if (child.HasDirtyTree())
                child.DrawDirtyLocal(dirtyRect);

            DrawChildClipped(child, dirtyRect);
            child.MarkCleaned();
        }
    }

    protected void DrawChildClipped(Component child, Rectangle dirtyRect)
    {
        if (ReferenceEquals(buffer, child.buffer))
        {
            DrawChild(child);
            return;
        }

        Rectangle clipped = Rectangle.Intersect(child.AbsoluteRectangle, dirtyRect);
        if (clipped.Width <= 0 || clipped.Height <= 0) return;

        DrawChildArea(child, clipped.X - child.AbsoluteX, clipped.Y - child.AbsoluteY, clipped.X - AbsoluteX, clipped.Y - AbsoluteY, clipped.Width, clipped.Height);
    }

    protected void DrawChild(Component child)
    {
        DrawChild(child, new Rectangle(0, 0, Width, Height));
    }

    protected void DrawChild(Component child, Rectangle parentLocalClip)
    {
        if (child == null || !child.Visible) return;

        if (ReferenceEquals(buffer, child.buffer))
        {
            bool pushed = buffer.PushContext(child.X, child.Y, child.Width, child.Height, parentLocalClip);
            try
            {
                if (pushed && buffer.HasVisibleClip)
                {
                    child.DrawLocal();
                    child.MarkCleaned();
                }
            }
            finally
            {
                if (pushed) buffer.PopContext();
            }
            return;
        }

        DrawChildArea(child, 0, 0, child.X, child.Y, child.Width, child.Height);
    }

    protected void DrawChildArea(Component child, int sourceX, int sourceY, int destinationX, int destinationY, int width, int height)
    {
        if (child.IsOpaqueForCopy())
        {
            buffer.DrawArrayClipped(
                child.GetBuffer(),
                child.buffer.Width,
                sourceX,
                sourceY,
                destinationX,
                destinationY,
                width,
                height);
            return;
        }

        buffer.DrawArrayAlphaClipped(
            child.GetBuffer(),
            child.buffer.Width,
            sourceX,
            sourceY,
            destinationX,
            destinationY,
            width,
            height);
    }

    public virtual void ForceDirty()
    {
        forceDirty = true;
        WindowManager.Invalidate(this);

        if (!isRoot)
            parent.MarkChildDirty();
    }


    public virtual void MarkCleaned()
    {
        dirty = false;
        childrenDirty = false;
        forceDirty = false;
    }

    private void BindRenderSurface(DirectBitmap surface)
    {
        buffer = surface;
        for (int i = 0; i < children.Count; i++)
            children[i].BindRenderSurface(surface);
    }

    public virtual void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        for (int i = children.Count - 1; i >= 0; i--)
            children[i].Dispose();

        children.Clear();

        buffer?.Dispose();
        cacheBuffer?.Dispose();
        components.Remove(this);
    }
}
public enum State
{
    Normal, Highlighted, Pressed
}
