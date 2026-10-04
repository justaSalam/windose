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

    private int SegmentWidth(Segment s, ulong total, int inner)
        => total == 0 ? inner : Math.Max(4, (int)((double)s.Size / total * inner));

    private ulong Total()
    {
        ulong t = 0;
        foreach (var s in segments) t += s.Size;
        return t;
    }

    public override void DrawLocal()
    {
        DrawFilledRectangle(Color.White, 0, 0, Width, Height);

        int inner = Width - 8;
        ulong total = Total();
        int x = 4;

        for (int i = 0; i < segments.Count; i++)
        {
            Segment s = segments[i];
            int w = SegmentWidth(s, total, inner);
            if (i == segments.Count - 1) w = Math.Max(4, 4 + inner - x);   // absorb rounding

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
            int inner = Width - 8;
            ulong total = Total();
            int x = 4;

            for (int i = 0; i < segments.Count; i++)
            {
                int w = SegmentWidth(segments[i], total, inner);
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