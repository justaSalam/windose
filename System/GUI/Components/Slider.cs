using System.Drawing;
using Windose;

public class Slider : Component
{
    private float _value = 50f;
    private float _min;
    private float _max = 100f;
    private float _smallChange = 1f;
    private float _largeChange = 10f;
    private bool isDragging;
    private float hoverBlend;
    private bool showValue;

    public bool useBorders = true;
    public Color trackColor = Palette.ControlShadow;
    public Color trackActiveColor = Palette.Highlight;
    public Color thumbColor = Palette.ControlFace;
    public Color thumbBorderColor = Palette.ControlShadow;
    public Color textColor = Palette.ControlBlack;
    public int fontSize = 0;
    public bool showTicks;

    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    public float Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, _min, _max);
            MarkDirty();
            ValueChanged?.Invoke(_value);
        }
    }

    public float Minimum
    {
        get => _min;
        set { _min = value; _value = Math.Clamp(_value, _min, _max); MarkDirty(); }
    }

    public float Maximum
    {
        get => _max;
        set { _max = value; _value = Math.Clamp(_value, _min, _max); MarkDirty(); }
    }

    public float SmallChange
    {
        get => _smallChange;
        set => _smallChange = Math.Max(0.1f, value);
    }

    public float LargeChange
    {
        get => _largeChange;
        set => _largeChange = Math.Max(1f, value);
    }

    public bool ShowValue
    {
        get => showValue;
        set { showValue = value; MarkDirty(); }
    }

    public event Action<float> ValueChanged;

    public Slider(int x, int y, int width) : base(x, y, width, 25)
    {
    }

    public Slider(int x, int y, int width, int height, Orientation orientation) : base(x, y, width, height)
    {
        Orientation = orientation;
    }

    public override void Update()
    {
        base.Update();

        float target = state == State.Highlighted || isDragging ? 1f : 0f;
        if (Math.Abs(hoverBlend - target) < 0.01f)
        {
            hoverBlend = target;
            return;
        }

        float step = (float)Math.Clamp(Kernel.DeltaTimeMs / 120.0, 0.02, 0.35);
        hoverBlend += target > hoverBlend ? step : -step;
        hoverBlend = Math.Clamp(hoverBlend, 0f, 1f);
        MarkDirty();
    }

    public override void DrawLocal()
    {
        if (Orientation == Orientation.Horizontal)
            DrawHorizontal();
        else
            DrawVertical();
    }

    private void DrawHorizontal()
    {
        int trackY = Height / 2 - 2;
        int trackHeight = 4;
        int thumbSize = Math.Min(14, Height - 4);
        int trackLeft = 4 + thumbSize / 2;
        int trackRight = Width - 4 - thumbSize / 2;
        int trackWidth = trackRight - trackLeft;

        if (trackWidth <= 0) return;

        float range = _max - _min;
        float normalized = range > 0 ? (_value - _min) / range : 0;
        int thumbX = trackLeft + (int)(trackWidth * normalized);

        if (Palette.FlatControls)
        {
            // Track (inactive part)
            DrawFilledRectangle(trackColor, trackLeft, trackY, trackWidth, trackHeight);
            if (useBorders)
                DrawRectangle(Palette.ControlShadow, trackLeft, trackY, trackWidth, trackHeight);

            // Track (active part - from start to thumb)
            if (thumbX > trackLeft)
            {
                DrawFilledRectangle(trackActiveColor, trackLeft, trackY, thumbX - trackLeft, trackHeight);
            }

            // Thumb
            Color thumbFace = GUIFeatures.Blend(thumbColor, Palette.Highlight, hoverBlend * 0.2f);
            if (isDragging)
                thumbFace = GUIFeatures.Blend(thumbFace, Palette.Highlight, 0.3f);

            DrawFilledRectangle(thumbFace, thumbX - thumbSize / 2, (Height - thumbSize) / 2, thumbSize, thumbSize);
            DrawRectangle(thumbBorderColor, thumbX - thumbSize / 2, (Height - thumbSize) / 2, thumbSize, thumbSize);

            // Ticks
            if (showTicks)
            {
                int tickCount = 10;
                for (int i = 0; i <= tickCount; i++)
                {
                    int tickX = trackLeft + (trackWidth * i / tickCount);
                    DrawLine(Palette.ControlShadow, tickX, trackY + trackHeight + 2, 1, 3);
                }
            }
        }
        else
        {
            // Classic: sunken track
            DrawSunkenRectangle(trackLeft, trackY, trackWidth, trackHeight);

            // Thumb with raised border
            Color thumbFace = GUIFeatures.Blend(Palette.ControlFace, Palette.ActiveTitle, hoverBlend * 0.15f);
            DrawRaisedRectangle(thumbX - thumbSize / 2, (Height - thumbSize) / 2, thumbSize, thumbSize);
            DrawFilledRectangle(thumbFace, thumbX - thumbSize / 2 + 1, (Height - thumbSize) / 2 + 1, thumbSize - 2, thumbSize - 2);

            // Ticks
            if (showTicks)
            {
                int tickCount = 10;
                for (int i = 0; i <= tickCount; i++)
                {
                    int tickX = trackLeft + (trackWidth * i / tickCount);
                    DrawLine(Palette.ControlShadow, tickX, trackY + trackHeight + 2, 1, 3);
                }
            }
        }

        // Value text
        if (showValue)
        {
            string valueText = ((int)_value).ToString();
            int effectiveFontSize = fontSize > 0 ? fontSize : 12;
            int textX = Width - MeasureStringWidth(valueText, effectiveFontSize) - 4;
            int textY = Math.Max(0, (Height - MeasureStringHeight(effectiveFontSize)) / 2);
            DrawString(valueText, textColor, textX > trackRight ? textX : 0, textY, effectiveFontSize);
        }
    }

    private void DrawVertical()
    {
        int trackX = Width / 2 - 2;
        int trackWidth = 4;
        int thumbSize = Math.Min(14, Width - 4);
        int trackTop = 4 + thumbSize / 2;
        int trackBottom = Height - 4 - thumbSize / 2;
        int trackHeight = trackBottom - trackTop;

        if (trackHeight <= 0) return;

        float range = _max - _min;
        float normalized = range > 0 ? (_value - _min) / range : 0;
        int thumbY = trackBottom - (int)(trackHeight * normalized);

        if (Palette.FlatControls)
        {
            DrawFilledRectangle(trackColor, trackX, trackTop, trackWidth, trackHeight);
            if (useBorders)
                DrawRectangle(Palette.ControlShadow, trackX, trackTop, trackWidth, trackHeight);

            if (thumbY > trackTop)
            {
                DrawFilledRectangle(trackActiveColor, trackX, trackTop, trackWidth, thumbY - trackTop);
            }

            Color thumbFace = GUIFeatures.Blend(thumbColor, Palette.Highlight, hoverBlend * 0.2f);
            if (isDragging)
                thumbFace = GUIFeatures.Blend(thumbFace, Palette.Highlight, 0.3f);

            DrawFilledRectangle(thumbFace, (Width - thumbSize) / 2, thumbY - thumbSize / 2, thumbSize, thumbSize);
            DrawRectangle(thumbBorderColor, (Width - thumbSize) / 2, thumbY - thumbSize / 2, thumbSize, thumbSize);

            if (showTicks)
            {
                int tickCount = 10;
                for (int i = 0; i <= tickCount; i++)
                {
                    int tickY = trackTop + (trackHeight * i / tickCount);
                    DrawLine(Palette.ControlShadow, trackX + trackWidth + 2, tickY, 3, 1);
                }
            }
        }
        else
        {
            DrawSunkenRectangle(trackX, trackTop, trackWidth, trackHeight);

            Color thumbFace = GUIFeatures.Blend(Palette.ControlFace, Palette.ActiveTitle, hoverBlend * 0.15f);
            DrawRaisedRectangle((Width - thumbSize) / 2, thumbY - thumbSize / 2, thumbSize, thumbSize);
            DrawFilledRectangle(thumbFace, (Width - thumbSize) / 2 + 1, thumbY - thumbSize / 2 + 1, thumbSize - 2, thumbSize - 2);

            if (showTicks)
            {
                int tickCount = 10;
                for (int i = 0; i <= tickCount; i++)
                {
                    int tickY = trackTop + (trackHeight * i / tickCount);
                    DrawLine(Palette.ControlShadow, trackX + trackWidth + 2, tickY, 3, 1);
                }
            }
        }

        if (showValue)
        {
            string valueText = ((int)_value).ToString();
            int effectiveFontSize = fontSize > 0 ? fontSize : 12;
            int textX = Math.Max(0, (Width - MeasureStringWidth(valueText, effectiveFontSize)) / 2);
            DrawString(valueText, textColor, textX, 2, effectiveFontSize);
        }
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (mouse.left == MouseEvents.Press)
        {
            isDragging = true;
            UpdateValueFromMouse(mouseX, mouseY);
            MarkDirty();
            return true;
        }

        if (mouse.left == MouseEvents.Hold && isDragging)
        {
            UpdateValueFromMouse(mouseX, mouseY);
            return true;
        }

        if (mouse.left == MouseEvents.Release && isDragging)
        {
            isDragging = false;
            MarkDirty();
            return true;
        }

        return true;
    }

    private void UpdateValueFromMouse(int mouseX, int mouseY)
    {
        float range = _max - _min;
        if (range <= 0) return;

        if (Orientation == Orientation.Horizontal)
        {
            int thumbSize = Math.Min(14, Height - 4);
            int trackLeft = 4 + thumbSize / 2;
            int trackRight = Width - 4 - thumbSize / 2;
            int trackWidth = trackRight - trackLeft;

            if (trackWidth <= 0) return;

            int localX = mouseX - AbsoluteX;
            float normalized = (float)(localX - trackLeft) / trackWidth;
            normalized = Math.Clamp(normalized, 0f, 1f);
            Value = _min + normalized * range;
        }
        else
        {
            int thumbSize = Math.Min(14, Width - 4);
            int trackTop = 4 + thumbSize / 2;
            int trackBottom = Height - 4 - thumbSize / 2;
            int trackHeight = trackBottom - trackTop;

            if (trackHeight <= 0) return;

            int localY = mouseY - AbsoluteY;
            float normalized = 1f - (float)(localY - trackTop) / trackHeight;
            normalized = Math.Clamp(normalized, 0f, 1f);
            Value = _min + normalized * range;
        }
    }

    public override string GetName() => "Slider";
}

public enum Orientation
{
    Horizontal,
    Vertical
}