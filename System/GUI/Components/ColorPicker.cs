using Cosmos.Kernel.System.Input;
using System.Drawing;

/// <summary>
/// Compact color picker: shows the current color, opens a swatch grid on click.
/// </summary>
public class ColorPicker : Component
{
    private Color value = Color.Red;
    private MenuPopup dropDown;
    private bool isDroppedDown;

    public static readonly Color[] DefaultPalette = new Color[]
    {
        Color.Black, Color.Gray, Color.White, Color.Red, Color.Green, Color.Blue,
        Color.Yellow, Color.Cyan, Color.Magenta, Color.Orange, Color.Purple, Color.Brown,
        Color.Pink, Color.Lime, Color.Teal, Color.Navy, Color.Olive, Color.Maroon,
        Color.Silver, Color.Gold, Color.Violet, Color.Indigo, Color.Turquoise, Color.Salmon,
    };

    public Color[] Swatches = DefaultPalette;
    public int Columns = 6;

    public event Action<Color> ValueChanged;

    public Color Value
    {
        get => value;
        set
        {
            if (value.ToArgb() == this.value.ToArgb()) return;
            this.value = value;
            MarkDirty();
            ValueChanged?.Invoke(this.value);
        }
    }

    public ColorPicker(int x, int y, int width = 60, int height = 24) : base(x, y, width, height)
    {
        clampSize = false;
        TabStop = true;
    }

    public override void DrawLocal()
    {
        DrawSunkenRectangle(0, 0, Width, Height);
        DrawFilledRectangle(value, 3, 3, Math.Max(1, Width - 6), Math.Max(1, Height - 6));
        DrawRectangle(Color.Black, 3, 3, Math.Max(1, Width - 6), Math.Max(1, Height - 6));
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInputEnabled()) return false;
        if (mouse.left == MouseEvents.Release)
        {
            if (isDroppedDown) CloseDropDown();
            else OpenDropDown();
            return true;
        }
        return true;
    }

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        if (!IsInputEnabled()) return;
        if (keyEvent.Key == Key.Enter)
        {
            if (isDroppedDown) CloseDropDown();
            else OpenDropDown();
        }
        else if (keyEvent.Key == Key.Escape && isDroppedDown)
            CloseDropDown();
    }

    private void OpenDropDown()
    {
        CloseDropDown();
        int cols = Math.Max(1, Columns);
        int rows = (Swatches.Length + cols - 1) / cols;
        int cell = 22;
        dropDown = new MenuPopup(cols * cell + 4, rows * cell + 4);
        SwatchGrid grid = new SwatchGrid(2, 2, cols * cell, rows * cell, this, cell);
        dropDown.AddChild(grid);
        dropDown.ShowAt(AbsoluteX, AbsoluteY + Height);
        isDroppedDown = true;
    }

    private void CloseDropDown()
    {
        if (dropDown != null)
        {
            dropDown.Hide();
            dropDown.Dispose();
            dropDown = null;
        }
        isDroppedDown = false;
        MarkDirty();
    }

    private sealed class SwatchGrid : Component
    {
        private readonly ColorPicker owner;
        private readonly int cell;

        public SwatchGrid(int x, int y, int w, int h, ColorPicker owner, int cell) : base(x, y, w, h)
        {
            this.owner = owner;
            this.cell = cell;
            clampSize = false;
        }

        public override void DrawLocal()
        {
            int cols = Math.Max(1, owner.Columns);
            for (int i = 0; i < owner.Swatches.Length; i++)
            {
                int cx = (i % cols) * cell;
                int cy = (i / cols) * cell;
                DrawFilledRectangle(owner.Swatches[i], cx + 1, cy + 1, cell - 2, cell - 2);
                DrawRectangle(Color.Black, cx + 1, cy + 1, cell - 2, cell - 2);
            }
        }

        public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
        {
            if (mouse.left != MouseEvents.Release) return true;
            int cols = Math.Max(1, owner.Columns);
            int lx = mouseX - AbsoluteX;
            int ly = mouseY - AbsoluteY;
            int index = (ly / cell) * cols + (lx / cell);
            if (index >= 0 && index < owner.Swatches.Length)
            {
                owner.Value = owner.Swatches[index];
                owner.CloseDropDown();
            }
            return true;
        }

        public override string GetComponentName() => "SwatchGrid";
    }

    public override string GetComponentName() => "ColorPicker";
}
