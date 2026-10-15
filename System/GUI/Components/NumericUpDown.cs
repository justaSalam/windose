using Cosmos.Kernel.System.Input;

/// <summary>
/// Numeric spinner: editable text field with up/down buttons.
/// </summary>
public class NumericUpDown : Component
{
    private float value;
    private float min;
    private float max = 100f;
    private float increment = 1f;

    private readonly TextField field;
    private readonly Button upButton;
    private readonly Button downButton;
    private bool suppressText;

    public event Action<float> ValueChanged;

    public float Value
    {
        get => value;
        set
        {
            float clamped = Math.Clamp(value, min, max);
            if (Math.Abs(this.value - clamped) < 0.001f) return;
            this.value = clamped;
            SyncField();
            MarkDirty();
            ValueChanged?.Invoke(this.value);
        }
    }

    public float Minimum
    {
        get => min;
        set { min = Math.Min(value, max); Value = Math.Clamp(this.value, min, max); }
    }

    public float Maximum
    {
        get => max;
        set { max = Math.Max(value, min); Value = Math.Clamp(this.value, min, max); }
    }

    public float Increment
    {
        get => increment;
        set => increment = Math.Max(0.001f, value);
    }

    public NumericUpDown(int x, int y, int width, int height = 24) : base(x, y, width, height)
    {
        clampSize = false;
        TabStop = true;

        int btnW = Math.Min(28, Math.Max(16, width / 4));
        field = new TextField(0, 0, width - btnW, height) { clampSize = false, Margin = new Thickness(0) };
        upButton = new Button("+", width - btnW, 0, btnW, height / 2) { clampSize = false, Margin = new Thickness(0) };
        downButton = new Button("-", width - btnW, height / 2, btnW, height - height / 2) { clampSize = false, Margin = new Thickness(0) };

        field.TextChanged += OnFieldChanged;
        upButton.leftClickAction = () => Value += increment;
        downButton.leftClickAction = () => Value -= increment;

        AddChild(field);
        AddChild(upButton);
        AddChild(downButton);
        SyncField();
    }

    private void OnFieldChanged(string text)
    {
        if (suppressText) return;
        if (TryParseFloat(text, out float v))
            Value = v;
    }

    private void SyncField()
    {
        suppressText = true;
        field.SetText(FormatValue(value));
        suppressText = false;
    }

    private static string FormatValue(float v)
    {
        if (v == (int)v) return ((int)v).ToString();
        bool neg = v < 0;
        double a = neg ? -(double)v : v;
        long whole = (long)a;
        int frac = (int)((a - whole) * 100.0 + 0.5);
        if (frac >= 100) { whole++; frac -= 100; }
        string s = whole.ToString();
        if (frac > 0)
        {
            string fs = frac < 10 ? "0" + frac.ToString() : frac.ToString();
            s += "." + fs.TrimEnd('0');
        }
        return neg ? "-" + s : s;
    }

    private static bool TryParseFloat(string s, out float result)
    {
        result = 0;
        if (string.IsNullOrEmpty(s)) return false;
        int i = 0;
        bool neg = false;
        if (s[0] == '+' || s[0] == '-') { neg = s[0] == '-'; i = 1; }
        long whole = 0;
        bool anyDigits = false;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') { whole = whole * 10 + (s[i] - '0'); anyDigits = true; i++; }
        double frac = 0, div = 1;
        if (i < s.Length && (s[i] == '.' || s[i] == ',')) {
            i++;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') { frac = frac * 10 + (s[i] - '0'); div *= 10; anyDigits = true; i++; }
        }
        if (!anyDigits || i != s.Length) return false;
        double v = whole + frac / div;
        result = (float)(neg ? -v : v);
        return true;
    }

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        if (!IsInputEnabled()) return;
        switch (keyEvent.Key)
        {
            case Key.UpArrow: Value += increment; break;
            case Key.DownArrow: Value -= increment; break;
        }
    }

    public override string GetComponentName() => "NumericUpDown";
}
