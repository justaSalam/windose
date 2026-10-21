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
        if (Palette.FlatControls)
        {
            int r = 3;
            // Windows 7 Aero styled progress track with rounded edges
            DrawFilledRoundedRectangle(Color.FromArgb(232, 232, 232), 0, 0, Width, Height, r);
            DrawRoundedRectangle(Color.FromArgb(188, 188, 188), 0, 0, Width, Height, r);
            if (Width > 4 && Height > 2)
                DrawHorizontalSpan(Color.FromArgb(215, 215, 215), 2, 1, Width - 4);

            int barHeight = Math.Max(1, Height - 4);
            int halfBarHeight = barHeight / 2;

            if (_indeterminate)
            {
                int barWidth = Math.Max(20, Width / 4);
                int barX = Math.Max(2, (int)_marqueeOffset);
                barWidth = Math.Min(barWidth, Width - 2 - barX);
                if (barWidth > 0)
                {
                    DrawGradientVerticalRounded(Color.FromArgb(210, 245, 180), Color.FromArgb(130, 225, 90), barX, 2, barWidth, halfBarHeight, 2);
                    DrawGradientVerticalRounded(Color.FromArgb(60, 190, 45), Color.FromArgb(90, 220, 75), barX, 2 + halfBarHeight, barWidth, barHeight - halfBarHeight, 2);
                    DrawRoundedRectangle(Color.FromArgb(45, 160, 35), barX, 2, barWidth, barHeight, 2);
                }
            }
            else
            {
                float range = _max - _min;
                if (range > 0)
                {
                    int barWidth = (int)((Width - 4) * ((_value - _min) / range));
                    if (barWidth > 0)
                    {
                        // Signature Windows 7 Aero glossy emerald green progress bar with rounded edges
                        DrawGradientVerticalRounded(Color.FromArgb(210, 245, 180), Color.FromArgb(130, 225, 90), 2, 2, barWidth, halfBarHeight, 2);
                        DrawGradientVerticalRounded(Color.FromArgb(60, 190, 45), Color.FromArgb(90, 220, 75), 2, 2 + halfBarHeight, barWidth, barHeight - halfBarHeight, 2);
                        DrawRoundedRectangle(Color.FromArgb(45, 160, 35), 2, 2, barWidth, barHeight, 2);
                    }
                }
            }
        }
        else
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
                    }
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