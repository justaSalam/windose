using System.Drawing;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;

public class Window : Component
{
    public Rectangle bounds; //Window viewport, relative to the screen
    public Rectangle content; //Content rect, relative to the window
    private bool isCached;

    private bool inFocus;

    private bool dragging;
    private bool resizing;

    private bool canMaximize = true;
    private bool canMinimize = true;
    private bool canResize = true;




    public int resizeMargin = 10;

    private Point offset;
    private Point resizeStart;
    private Rectangle original;
    private Rectangle previewBounds;

    private Component? focusedComponent;

    private Panel titlebar;

    public Window(int x, int y, int width, int height, string title, bool useTitleBar = false) : base(x, y, width, height)
    {
        bounds = new Rectangle(x, y, width, height);
        zLayer = DrawLayer.Window;

        if (useTitleBar)
        {
            titlebar = new Panel(Color.FromArgb(123, 126, 121), 2, 2, width - 4, 25)
            {
                useBorders = false,
                clampSize = false,
                text = title,
                fontSize = 16,
                horizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(2, 2, 2, 2)
            };
            AddChild(titlebar);


            AddChild(new Button(Color.LightGray, 0, 0, 20, 20)
            {
                text = "X",
                verticalAlignment = VerticalAlignment.Top,
                horizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(3, 3, 3, 3),
                useBorders = true,
                borderColor = Color.White,
                leftMouseRelease = () =>
                {
                    Serial.WriteString("[TITLE] Closing window\n");
                }
            });

            if (canMaximize)
            {
                AddChild(new Button(Color.LightGray, 25, 0, 20, 20)
                {
                    text = "O",
                    verticalAlignment = VerticalAlignment.Top,
                    horizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(3, 3, 23, 3),
                    useBorders = true,
                    borderColor = Color.White,
                    leftMouseRelease = () =>
                    {
                        Serial.WriteString("[TITLE] Maximizing window.\n");
                    }
                });

            }
            if (canMinimize)
            {
                AddChild(new Button(Color.LightGray, 50, 0, 20, 20)
                {
                    text = "_",
                    verticalAlignment = VerticalAlignment.Top,
                    horizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(3, 3, 46, 3),
                    useBorders = true,
                    borderColor = Color.White,
                    leftMouseRelease = () =>
                    {
                        Serial.WriteString("[TITLE] Minimizing window\n");
                    }
                });
            }
        }



        AddChild(new Button(Color.Gray, 0, 0, 100, 30)
        {
            text = "Button",
            useBorders = true,
            horizontalAlignment = HorizontalAlignment.Center,
            verticalAlignment = VerticalAlignment.Center,
            leftMouseRelease = () =>
            {
                Serial.WriteString("button pressed\n");
            }
        });

        AddChild(new TextField(Color.Gray, 0, 0, 250, 30)
        {
            useBorders = true,
            fontSize = 16,
            Margin = new Thickness(35, 0, 0, 0),
            horizontalAlignment = HorizontalAlignment.Center,
            verticalAlignment = VerticalAlignment.Center,
        });

        AddChild(new Checkbox(0, 0)
        {
            text = "Checkbox",
            fontSize = 16,
            Margin = new Thickness(70, 0, 0, 0),
            horizontalAlignment = HorizontalAlignment.Center,
            verticalAlignment = VerticalAlignment.Center,
        });
    }


    public void Start()
    {
    }

    public override void Update()
    {
        // Window movement is handled by the compositor; hover state is for child controls.
    }

    /// <summary>
    /// Coordinates relative to the screen
    /// </summary>
    public override void Draw()
    {
        DrawLocal();
        base.Draw();
    }

    /// <summary>
    /// Component rendering, coordinates relative to the window
    /// </summary>
    public override void DrawLocal()
    {
        DrawFilledRectangle(Color.FromArgb(190, 190, 190), 0, 0, Width, Height);



        DrawRectangle(Color.White, 0, 0, Width, Height);

        foreach (Component child in children)
        {
            if (!child.Visible) continue;


            child.DrawLocal();
            buffer.DrawImageAlpha(child.GetBuffer(), child.X, child.Y);
            child.MarkCleaned();
        }
    }






    public override bool HandleInput(int mouseX, int mouseY, MouseState mouseState)
    {
        if (mouseState.left == MouseEvents.Press)
        {
            inFocus = HitTest(mouseX, mouseY);
            focusedComponent = GetChildAt(mouseX, mouseY);
        }

        if (focusedComponent != null && focusedComponent.HandleInput(mouseX, mouseY, mouseState))
            return true;

        DragWindow(mouseState, mouseX, mouseY);
        ResizeWindow(mouseState, mouseX, mouseY);

        return HitTest(mouseX, mouseY);
    }
    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        if (focusedComponent != null) focusedComponent.HandleKeyboard(keyEvent);
        else base.HandleKeyboard(keyEvent);
    }

    public bool FocusCheck(int mouseX, int mouseY, MouseState mouseState)
    {
        return HitTest(mouseX, mouseY) && mouseState.left == MouseEvents.Press;
    }

    private void DragWindow(MouseState mouse, int mouseX, int mouseY)
    {
        if (resizing) return;

        if (mouse.left == MouseEvents.Press && TitleHitTest(mouseX, mouseY))
        {
            dragging = true;
            offset = new Point(mouseX - bounds.X, mouseY - bounds.Y);
            previewBounds = bounds;
            WindowManager.ShowPreviewRect(previewBounds);
        }
        else if (mouse.left == MouseEvents.Release && dragging)
        {
            dragging = false;
            WindowManager.ClearPreviewRect();
            Move(previewBounds.X, previewBounds.Y);
        }
        else if (mouse.left == MouseEvents.None && dragging)
        {
            dragging = false;
            WindowManager.ClearPreviewRect();
        }

        if (dragging && (mouse.left == MouseEvents.Hold || mouse.left == MouseEvents.Press))
        {
            previewBounds = new Rectangle(mouseX - offset.X, mouseY - offset.Y, Width, Height);
            WindowManager.ShowPreviewRect(previewBounds);
        }
    }

    private void ResizeWindow(MouseState mouseState, int mouseX, int mouseY)
    {
        if (dragging || !canResize) return;

        if (mouseState.left == MouseEvents.Press && ResizeHitTest(mouseX, mouseY))
        {
            resizing = true;
            resizeStart = new Point(mouseX, mouseY);
            original = bounds;
            previewBounds = bounds;
            WindowManager.ShowPreviewRect(previewBounds);
        }
        else if (mouseState.left == MouseEvents.Release && resizing)
        {
            resizing = false;
            WindowManager.ClearPreviewRect();
            Resize(previewBounds.X, previewBounds.Y, previewBounds.Width, previewBounds.Height);
        }
        else if (mouseState.left == MouseEvents.None && resizing)
        {
            resizing = false;
            WindowManager.ClearPreviewRect();
        }

        if (resizing && (mouseState.left == MouseEvents.Hold || mouseState.left == MouseEvents.Press))
        {
            int newWidth = original.Width + (mouseX - resizeStart.X);
            int newHeight = original.Height + (mouseY - resizeStart.Y);

            if (newWidth > 2 && newHeight > 2)
            {
                previewBounds = new Rectangle(original.X, original.Y, newWidth, newHeight);
                WindowManager.ShowPreviewRect(previewBounds);
            }
        }
    }

    public virtual bool HitTest(int mouseX, int mouseY)
    {
        return mouseX >= bounds.X && mouseX < bounds.X + bounds.Width && mouseY >= bounds.Y && mouseY < bounds.Y + bounds.Height;
    }

    public bool TitleHitTest(int mouseX, int mouseY)
    {
        return mouseX >= bounds.X && mouseX < bounds.X + bounds.Width && mouseY >= bounds.Y && mouseY < bounds.Y + 20;
    }
    public bool ResizeHitTest(int mouseX, int mouseY)
    {
        return mouseX >= bounds.X + bounds.Width - resizeMargin && mouseX < bounds.X + bounds.Width && mouseY >= bounds.Y + bounds.Height - resizeMargin && mouseY < bounds.Y + bounds.Height;
    }


    private void Move(int x, int y)
    {
        Rectangle oldBounds = bounds;

        X = x;
        Y = y;
        bounds = new Rectangle(X, Y, Width, Height);

        WindowManager.Invalidate(oldBounds);
        WindowManager.Invalidate(bounds);
        MarkDirty();
    }

    private void Resize(int x, int y, int width, int height)
    {
        Rectangle oldBounds = bounds;

        X = x;
        Y = y;
        base.Resize(width, height);
        bounds = new Rectangle(X, Y, Width, Height);

        WindowManager.Invalidate(oldBounds);
        WindowManager.Invalidate(bounds);
        MarkDirty();
    }
    public void Stop() //TODO Dispose, GC wont collect it without proper disposal first
    {

    }

    public override string GetName() => "Window";

}
