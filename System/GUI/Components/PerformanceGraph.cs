using System.Drawing;

public class PerformanceGraph : Component
{
    private readonly float[][] samples;
    private readonly Color[] seriesColors;
    private readonly string[] seriesNames;
    private readonly int capacity;
    private int writeIndex;
    private int sampleCount;

    public float maximum = 50;
    public Color backgroundColor = Color.Black;
    public Color gridColor = Color.FromArgb(48, 48, 48);
    public Color textColor = Color.White;
    public int fontSize = 16;

    public PerformanceGraph(int x, int y, int width, int height, int historyLength = 120) : base(x, y, width, height)
    {
        clampSize = false;
        capacity = Math.Max(2, historyLength);
        samples = new float[5][];
        seriesColors = new Color[5];
        seriesNames = new string[5];

        for (int i = 0; i < samples.Length; i++)
            samples[i] = new float[capacity];

        seriesColors[0] = Color.Lime;
        seriesColors[1] = Color.Cyan;
        seriesColors[2] = Color.Yellow;
        seriesColors[3] = Color.Magenta;
        seriesColors[4] = Color.White;
    }

    public void SetSeries(int index, string name, Color color)
    {
        if (index < 0 || index >= samples.Length) return;
        seriesNames[index] = name;
        seriesColors[index] = color;
    }

    public void AddSample(float first, float second = -1, float third = -1, float fourth = -1, float fifth = -1)
    {
        samples[0][writeIndex] = first;
        samples[1][writeIndex] = second;
        samples[2][writeIndex] = third;
        samples[3][writeIndex] = fourth;
        samples[4][writeIndex] = fifth;

        writeIndex = (writeIndex + 1) % capacity;
        if (sampleCount < capacity) sampleCount++;
        MarkDirty();
    }

    public override void DrawLocal()
    {
        DrawFilledRectangle(backgroundColor, 0, 0, Width, Height);
        DrawSunkenRectangle(0, 0, Width, Height);

        int plotTop = 28;
        int plotBottom = Math.Max(plotTop + 1, Height - 4);
        int plotHeight = plotBottom - plotTop;

        for (int i = 0; i <= 4; i++)
        {
            int y = plotTop + i * plotHeight / 4;
            DrawLine(gridColor, 2, y, Width - 3, y);
        }

        DrawLegend();

        for (int series = 0; series < samples.Length; series++)
        {
            if (seriesNames[series] == null || seriesNames[series] == "") continue;
            DrawSeries(series, plotTop, plotBottom);
        }
    }

    private void DrawLegend()
    {
        int x = 6;

        for (int i = 0; i < seriesNames.Length; i++)
        {
            if (seriesNames[i] == null || seriesNames[i] == "") continue;

            DrawFilledRectangle(seriesColors[i], x, 8, 8, 8);
            DrawString(seriesNames[i], textColor, x + 12, 4, fontSize);
            x += 16 + MeasureStringWidth(seriesNames[i], fontSize);
        }
    }

    private void DrawSeries(int series, int plotTop, int plotBottom)
    {
        if (sampleCount < 2) return;

        int previousX = 2;
        int previousY = ValueToY(GetSample(series, 0), plotTop, plotBottom);

        for (int i = 1; i < sampleCount; i++)
        {
            int x = 2 + i * Math.Max(1, Width - 5) / Math.Max(1, capacity - 1);
            int y = ValueToY(GetSample(series, i), plotTop, plotBottom);
            DrawLine(seriesColors[series], previousX, previousY, x, y);
            previousX = x;
            previousY = y;
        }
    }

    private float GetSample(int series, int chronologicalIndex)
    {
        int oldest = (writeIndex - sampleCount + capacity) % capacity;
        return samples[series][(oldest + chronologicalIndex) % capacity];
    }

    private int ValueToY(float value, int plotTop, int plotBottom)
    {
        if (value < 0) value = 0;
        if (value > maximum) value = maximum;

        int plotHeight = plotBottom - plotTop;
        return plotBottom - (int)(value * plotHeight / Math.Max(1, maximum));
    }

    public override bool IsOpaqueForCopy() => true;
    public override string GetName() => "PerformanceGraph";
}
