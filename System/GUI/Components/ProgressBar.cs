using System.Drawing;
using Windose;

public class ProgressBar : Component
{
    private float _value;
    private float _min;
    private float _max = 100f;
    private bool _indeterminate;
    private float _marqueeOffset;
    private float _targetValue;
    private float _animSpeed;

    public bool useBorders = true;
    public Color borderColor = Palette.ControlShadow;
    public Color barColor = Palette.Highlight;
    public Color trackColor = Palette.ControlWhite;
    public Color textColor = Palette.ControlBlack;
    public int fontSize = 0;
    public bool showText;

    public float Value
    {
        get => _value;
        set
        {
            _targetValue = Math.Clamp(value, _min, _max);
            if (Math.Abs(AnimationSpeed) < 0.001f)
                _value = _targetValue;
            MarkDirty();
        }
    }

    /// <summary>Units per second the bar eases toward Value. 0 snaps instantly.</summary>
    public float AnimationSpeed
    {
        get => _animSpeed;
        set { _animSpeed = Math.Max(0, value); MarkDirty(); }
    }

    public float Minimum
    {
        get => _min;
        set
        {
            _min = Math.Min(value, _max);
            _value = Math.Clamp(_value, _min, _max);
            _targetValue = Math.Clamp(_targetValue, _min, _max);
            MarkDirty();
        }
    }

    public float Maximum
    {
        get => _max;
        set
        {
            _max = Math.Max(value, _min);
            _value = Math.Clamp(_value, _min, _max);
            _targetValue = Math.Clamp(_targetValue, _min, _max);
            MarkDirty();
        }
    }

    public bool Indeterminate
    {
        get => _indeterminate;
        set { _indeterminate = value; MarkDirty(); }
    }

    public ProgressBar(int x, int y, int width, int height) : base(x, y, width, height)
    {
    }

    public override void Update()
    {
        base.Update();

        if (_indeterminate)
        {
            _marqueeOffset += (float)(Kernel.DeltaTimeMs / 8.0);
            if (_marqueeOffset > Width) _marqueeOffset = -Width * 0.3f;
            MarkDirty();
            return;
        }

        if (Math.Abs(_value - _targetValue) < 0.1f)
        {
            _value = _targetValue;
            return;
        }

        float step = _animSpeed > 0 ? _animSpeed * (float)Kernel.DeltaTimeSeconds
                                    : (float)(Kernel.DeltaTimeMs / 50.0);
        float next = _value + (_targetValue > _value ? step : -step);
        if ((_targetValue - _value) * (_targetValue - next) <= 0)
            next = _targetValue;   // reached or overshot
        _value = Math.Clamp(next, _min, _max);
        MarkDirty();
    }

    public override void DrawLocal()
    {

        // Classic: sunken track
        DrawSunkenRectangle(0, 0, Width, Height);

        if (_indeterminate)
        {
            int barWidth = Math.Max(20, Width / 4);
            int barX = (int)_marqueeOffset;
            DrawFilledRectangle(barColor, barX + 2, 2, barWidth, Height - 4);
        }
        else
        {
            float range = _max - _min;
            if (range > 0)
            {
                int barWidth = (int)((Width - 4) * ((_value - _min) / range));
                if (barWidth > 0)
                {
                    DrawFilledRectangle(barColor, 2, 2, barWidth, Height - 4);
                    // Add segments for classic look
                    for (int x = 2; x < barWidth; x += 4)
                        DrawLine(Palette.ControlHighlight, x, 2, 1, Height - 4);
                }
            }

        }

        if (showText && text != "")
        {
            int effectiveFontSize = fontSize > 0 ? fontSize : Math.Max(1, Height - 6);
            int textY = Math.Max(0, (Height - MeasureStringHeight(effectiveFontSize)) / 2);
            DrawString(text, textColor, 4, textY, effectiveFontSize);
        }
    }

    public override string GetComponentName() => "ProgressBar";
}