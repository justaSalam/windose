using System.Drawing;

public class PartitionBar : Component
{
    public class Segment
    {
        public string Name = "";
        public ulong Size;
        public object? Tag;
    }

    private readonly List<Segment> segments = new List<Segment>();
    private Segment? selected;

    public Action<Segment?>? segmentClicked;
    public int fontSize = 14;

    public PartitionBar(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
    }

    public void SetSegments(IEnumerable<Segment> items)
    {
        segments.Clear();
        segments.AddRange(items);
        if (selected != null && !segments.Contains(selected)) selected = null;
        MarkDirty();
    }

    public void SelectByTag(object? tag)
    {
        selected = null;
        foreach (var s in segments)
            if (ReferenceEquals(s.Tag, tag)) { selected = s; break; }
        MarkDirty();
    }

    private double Total()
    {
        double t = 0;
        foreach (var s in segments) t += s.Size;
        return t;
    }

    private int[] GetSegmentWidths(int inner)
    {
        int[] widths = new int[segments.Count];
        if (segments.Count == 0 || inner <= 0)
            return widths;

        double total = Total();
        int remaining = inner;
        int lastNonEmpty = segments.Count - 1;
        while (lastNonEmpty > 0 && segments[lastNonEmpty].Size == 0)
            lastNonEmpty--;

        for (int i = 0; i < segments.Count; i++)
        {
            int width;
            if (total == 0)
                width = i == segments.Count - 1 ? remaining : inner / segments.Count;
            else if (segments[i].Size == 0)
                width = 0;
            else if (i == lastNonEmpty)
                width = remaining;
            else
                width = (int)((double)segments[i].Size / total * inner);

            width = Math.Clamp(width, 0, remaining);
            widths[i] = width;
            remaining -= width;
        }

        return widths;
    }

    public override void DrawLocal()
    {
        DrawFilledRectangle(Color.White, 0, 0, Width, Height);

        int inner = Math.Max(0, Width - 8);
        int[] widths = GetSegmentWidths(inner);
        int x = 4;

        for (int i = 0; i < segments.Count; i++)
        {
            Segment s = segments[i];
            int w = widths[i];

            if (w == 0)
                continue;

            DrawFilledRectangle(Color.FromArgb(255, 200, 215, 235), x, 4, w - 2, Height - 8);

            if (s == selected)
            {
                for (int b = 0; b < 3; b++)
                    DrawRectangle(Color.Green, x - b, 4 - b, w - 2 + b * 2, Height - 8 + b * 2);
            }
            else
            {
                DrawRectangle(Color.Gray, x, 4, w - 2, Height - 8);
            }

            string label = s.Name;
            int tw = MeasureStringWidth(label, fontSize);
            if (tw < w - 6)
                DrawString(label, Color.Black, x + (w - 2 - tw) / 2, Height / 2 - fontSize / 2, fontSize);

            x += w;
        }
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInsideAbsolute(mouseX, mouseY)) return false;

        if (mouse.left == MouseEvents.Release)
        {
            int lx = mouseX - AbsoluteX;
            int inner = Math.Max(0, Width - 8);
            int[] widths = GetSegmentWidths(inner);
            int x = 4;

            for (int i = 0; i < segments.Count; i++)
            {
                int w = widths[i];
                if (lx >= x && lx < x + w)
                {
                    selected = segments[i];
                    MarkDirty();
                    segmentClicked?.Invoke(selected);
                    return true;
                }
                x += w;
            }
        }
        return true;
    }

    public override string GetComponentName() => "PartitionBar";
}