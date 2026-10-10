using Cosmos.Kernel.System.Input;
using System.Drawing;

public class DesignerSurface : Component
{
    public Component? content;
    public Component? selected;
    public Action<Component?>? selectionChanged;
    public Action<string>? status;
    public Func<bool>? leftButtonProbe;          // optional: true while the left button is physically down
    public int grid = 4;
    public bool debug = true;
    public Color workspaceColor = Color.FromArgb(255, 110, 110, 110);

    private const int MinSize = 8;
    private const int HandleSize = 6;
    private const int HandleHit = 6;

    private enum DragMode { None, Move, Resize }
    private DragMode drag = DragMode.None;
    private int startMX, startMY, startX, startY, startW, startH;
    private int resizeFlags;                      // 1 left, 2 right, 4 top, 8 bottom
    private bool dragChanged;
    private string? dragSnapshot;

    private readonly List<string> undo = new List<string>();
    private readonly List<string> redo = new List<string>();

    private int lastL, lastMX, lastMY;
    private string lastHit = "-";

    public DesignerSurface(int x, int y, int w, int h) : base(x, y, w, h)
    {
        clampSize = false;
    }

    // ============================================================ position helpers
    // Everything is computed from local X/Y up the parent chain. No cached values.

    private int OriginX
    {
        get { int a = 0; for (Component? c = this; c != null; c = c.Parent) { a += c.X; if (c.isRoot) break; } return a; }
    }
    private int OriginY
    {
        get { int a = 0; for (Component? c = this; c != null; c = c.Parent) { a += c.Y; if (c.isRoot) break; } return a; }
    }

    private bool InsideSurface(int mx, int my)
        => mx >= OriginX && my >= OriginY && mx < OriginX + Width && my < OriginY + Height;

    // position of c relative to this surface
    private (int x, int y) LocalPos(Component c)
    {
        int x = 0, y = 0;
        while (c != null && c != this) { x += c.X; y += c.Y; c = c.Parent!; }
        return (x, y);
    }

    private int Snap(int v) => grid <= 1 ? v : (v / grid) * grid;

    private static bool IsContainer(Component c) => c is DockPanel || UiRegistry.IsContainer(c);

    // (lx, ly) is in c's own space; returns the deepest control under the point
    private static Component HitTest(Component c, int lx, int ly)
    {
        if (!IsContainer(c)) return c;

        for (int i = c.children.Count - 1; i >= 0; i--)
        {
            Component ch = c.children[i];
            if (!ch.Visible) continue;
            int cx = lx - ch.X, cy = ly - ch.Y;
            if (cx >= 0 && cy >= 0 && cx < ch.Width && cy < ch.Height)
                return HitTest(ch, cx, cy);
        }
        return c;
    }

    private Component? HitAt(int mx, int my)
    {
        if (content == null) return null;
        int lx = mx - OriginX - content.X, ly = my - OriginY - content.Y;
        if (lx < 0 || ly < 0 || lx >= content.Width || ly >= content.Height) return null;
        return HitTest(content, lx, ly);
    }

    // deepest container under the point, skipping `exclude`
    private static Component DeepestContainer(Component c, int lx, int ly, Component? exclude)
    {
        for (int i = c.children.Count - 1; i >= 0; i--)
        {
            Component ch = c.children[i];
            if (ch == exclude || !ch.Visible) continue;
            int cx = lx - ch.X, cy = ly - ch.Y;
            if (cx < 0 || cy < 0 || cx >= ch.Width || cy >= ch.Height) continue;
            return IsContainer(ch) ? DeepestContainer(ch, cx, cy, exclude) : c;
        }
        return c;
    }

    private Component? ContainerAt(int mx, int my, Component? exclude)
    {
        if (content == null) return null;
        int lx = mx - OriginX - content.X, ly = my - OriginY - content.Y;
        if (lx < 0 || ly < 0 || lx >= content.Width || ly >= content.Height) return null;
        return DeepestContainer(content, lx, ly, exclude);
    }

    // ============================================================ content, selection, undo

    public void SetContent(Component c)
    {
        if (content != null)
        {
            Component old = content;
            RemoveChild(old);
            old.Dispose();
        }
        content = c;
        AddChild(c);
        Select(null);
    }

    public void Select(Component? c)
    {
        selected = c;
        selectionChanged?.Invoke(c);
        MarkDirty();
    }

    public void ClearHistory() { undo.Clear(); redo.Clear(); }

    private string? Snapshot() => content == null ? null : UiFile.Save(content);

    private void PushUndo(string s)
    {
        undo.Add(s);
        if (undo.Count > 50) undo.RemoveAt(0);
        redo.Clear();
    }

    /// <summary>Call before any programmatic edit.</summary>
    public void BeginChange()
    {
        string? s = Snapshot();
        if (s != null) PushUndo(s);
    }

    public void Undo()
    {
        if (undo.Count == 0 || content == null) return;
        redo.Add(UiFile.Save(content));
        string s = undo[undo.Count - 1];
        undo.RemoveAt(undo.Count - 1);
        Restore(s);
    }

    public void Redo()
    {
        if (redo.Count == 0 || content == null) return;
        undo.Add(UiFile.Save(content));
        string s = redo[redo.Count - 1];
        redo.RemoveAt(redo.Count - 1);
        Restore(s);
    }

    private void Restore(string snap)
    {
        Component? c = UiFile.Load(snap);
        if (c == null) return;
        if (c is DockPanel dp) dp.gridDots = 8;
        SetContent(c);
    }

    // ============================================================ commands

    private string MakeName(Component c)
    {
        string b = (c.designType.Length > 0 ? c.designType : "control").ToLowerInvariant();
        for (int i = 1; ; i++)
        {
            string n = b + i;
            if (content == null || content.Find(n) == null) return n;
        }
    }

    public void AddControl(ComponentDescriptor d)
    {
        if (content == null) return;

        Component target = content;
        if (selected != null && IsContainer(selected)) target = selected;
        else if (selected?.Parent != null && IsContainer(selected.Parent)) target = selected.Parent;

        BeginChange();

        Component c = d.Create();
        c.dock = Dock.None;                       // free-floating; dock it from the property grid if wanted
        c.name = MakeName(c);
        target.AddChild(c);

        int off = 8 + (target.children.Count % 10) * 12;
        c.SetBounds(off, off, c.Width, c.Height);

        Select(c);
        status?.Invoke("Added " + c.name);
    }

    public void DeleteSelected()
    {
        if (selected == null || selected == content || selected.Parent == null) return;

        BeginChange();
        Component victim = selected;
        victim.Parent!.RemoveChild(victim);
        victim.Dispose();
        Select(null);
    }

    public void DuplicateSelected()
    {
        if (selected == null || selected == content || selected.Parent == null) return;
        if (UiRegistry.For(selected) == null) return;

        Component parent = selected.Parent;
        Component? copy = UiFile.Load(UiFile.Save(selected));
        if (copy == null) return;

        BeginChange();
        copy.name = MakeName(copy);
        int nx = selected.X + 8, ny = selected.Y + 8;

        if (parent is DockPanel dp) dp.AddDockChild(copy, copy.dock);
        else parent.AddChild(copy);

        if (copy.dock == Dock.None) copy.SetBounds(nx, ny, copy.Width, copy.Height);
        Select(copy);
    }

    // ============================================================ resize handles
    // 0 TL, 1 T, 2 TR, 3 R, 4 BR, 5 B, 6 BL, 7 L

    private static readonly int[] HX = { 0, 1, 2, 2, 2, 1, 0, 0 };
    private static readonly int[] HY = { 0, 0, 0, 1, 2, 2, 2, 1 };
    private static readonly int[] HFlags = { 1 | 4, 4, 2 | 4, 2, 2 | 8, 8, 1 | 8, 1 };

    private bool HandleActive(int i) => selected != content || i == 3 || i == 4 || i == 5;   // the form only resizes right/bottom

    private (int x, int y) HandleCenter(int i)
    {
        var p = LocalPos(selected!);
        return (p.x + HX[i] * selected!.Width / 2, p.y + HY[i] * selected.Height / 2);
    }

    private int HandleAt(int mx, int my)
    {
        if (selected == null) return -1;
        for (int i = 0; i < 8; i++)
        {
            if (!HandleActive(i)) continue;
            var c = HandleCenter(i);
            if (Math.Abs(mx - (OriginX + c.x)) <= HandleHit && Math.Abs(my - (OriginY + c.y)) <= HandleHit)
                return i;
        }
        return -1;
    }

    // ============================================================ input

    public override bool HandleInput(int mx, int my, MouseState mouse)
    {
        lastL = (int)mouse.left;
        lastMX = mx;
        lastMY = my;

        if (drag != DragMode.None)
        {
            if (mouse.left == MouseEvents.Release) { EndDrag(); return true; }
            if (mouse.left == MouseEvents.Press) { EndDrag(); }          // a stray press: end the old drag, handle as new
            else { ApplyDrag(mx, my); return true; }
        }

        if (content == null || !InsideSurface(mx, my)) return false;
        if (mouse.left != MouseEvents.Press) return true;

        OnPress(mx, my);
        MarkDirty();
        return true;
    }

    private void OnPress(int mx, int my)
    {
        int h = HandleAt(mx, my);
        if (h >= 0)
        {
            resizeFlags = HFlags[h];
            BeginDrag(DragMode.Resize, mx, my);
            lastHit = "handle " + h;
            return;
        }

        Component? hit = HitAt(mx, my);
        Select(hit);
        lastHit = hit == null ? "none" : (hit.name.Length > 0 ? hit.name : hit.designType);

        if (hit != null && hit != content)
            BeginDrag(DragMode.Move, mx, my);
    }

    private void BeginDrag(DragMode mode, int mx, int my)
    {
        drag = mode;
        startMX = mx;
        startMY = my;
        startX = selected!.X;
        startY = selected.Y;
        startW = selected.Width;
        startH = selected.Height;
        dragChanged = false;
        dragSnapshot = Snapshot();
        MouseCapture = this;
    }

    private void MarkChange()
    {
        if (dragChanged) return;
        dragChanged = true;
        if (dragSnapshot != null) PushUndo(dragSnapshot);
    }

    private void ApplyDrag(int mx, int my)
    {
        if (selected == null) { EndDrag(); return; }

        int dx = mx - startMX, dy = my - startMY;

        if (drag == DragMode.Move)
        {
            if (selected.dock != Dock.None)
            {
                if (Math.Abs(dx) < 4 && Math.Abs(dy) < 4) return;      // a click must not undock

                MarkChange();
                var dp = selected.Parent as DockPanel;
                selected.dock = Dock.None;
                dp?.ResolveDockLayout();                               // siblings re-flow

                startX = selected.X;
                startY = selected.Y;
                startMX = mx;
                startMY = my;
                dx = 0;
                dy = 0;
            }

            if (dx != 0 || dy != 0) MarkChange();
            selected.SetBounds(Snap(startX + dx), Snap(startY + dy), selected.Width, selected.Height);
        }
        else if (drag == DragMode.Resize)
        {
            int x = startX, y = startY, w = startW, h = startH;

            if ((resizeFlags & 1) != 0) { int nx = Math.Min(Snap(startX + dx), startX + startW - MinSize); w = startX + startW - nx; x = nx; }
            if ((resizeFlags & 2) != 0) w = Math.Max(MinSize, Snap(startW + dx));
            if ((resizeFlags & 4) != 0) { int ny = Math.Min(Snap(startY + dy), startY + startH - MinSize); h = startY + startH - ny; y = ny; }
            if ((resizeFlags & 8) != 0) h = Math.Max(MinSize, Snap(startH + dy));

            if (dx != 0 || dy != 0) MarkChange();
            selected.SetBounds(x, y, w, h);
            (selected.Parent as DockPanel)?.ResolveDockLayout();
        }

        MarkDirty();
    }

    private void EndDrag()
    {
        DragMode m = drag;
        drag = DragMode.None;
        if (MouseCapture == this) MouseCapture = null;

        if (dragChanged)
        {
            if (m == DragMode.Move) TryReparent();
            selectionChanged?.Invoke(selected);         // refresh property values
        }
        dragSnapshot = null;
        MarkDirty();
    }

    // after a move: if the centre sits over a different container, move the control into it
    private void TryReparent()
    {
        if (selected == null || selected == content || selected.Parent == null) return;

        var old = LocalPos(selected);
        int cx = OriginX + old.x + selected.Width / 2;
        int cy = OriginY + old.y + selected.Height / 2;

        Component? target = ContainerAt(cx, cy, selected);
        if (target == null || target == selected.Parent) return;

        for (Component? p = target; p != null; p = p.Parent)
            if (p == selected) return;                  // never into its own descendant

        selected.Parent.RemoveChild(selected);
        selected.dock = Dock.None;
        target.AddChild(selected);

        var tp = LocalPos(target);
        selected.SetBounds(Snap(old.x - tp.x), Snap(old.y - tp.y), selected.Width, selected.Height);
    }

    // runs every frame, so dragging works even if move events never reach HandleInput
    public override void Update()
    {
        base.Update();
        if (drag == DragMode.None) return;

        if (leftButtonProbe != null && !leftButtonProbe()) { EndDrag(); return; }

        ApplyDrag(MouseManager.X, MouseManager.Y);
    }

    // ============================================================ drawing

    public override void DrawLocal()
    {
        DrawFilledRectangle(workspaceColor, 0, 0, Width, Height);

        if (content != null)
        {
            int cx = content.X, cy = content.Y, cw = content.Width, ch = content.Height;
            DrawFilledRectangle(Color.FromArgb(255, 70, 70, 70), cx + 4, cy + 4, cw, ch);   // shadow
            DrawChild(content);
            DrawRectangle(Color.Black, cx - 1, cy - 1, cw + 2, ch + 2);                      // outline
        }

        if (selected != null && selected.Visible)
        {
            var sp = LocalPos(selected);
            DrawRectangle(Color.Yellow, sp.x - 1, sp.y - 1, selected.Width + 2, selected.Height + 2);

            for (int i = 0; i < 8; i++)
            {
                if (!HandleActive(i)) continue;
                var c = HandleCenter(i);
                DrawFilledRectangle(Color.Black, c.x - HandleSize / 2 - 1, c.y - HandleSize / 2 - 1, HandleSize + 2, HandleSize + 2);
                DrawFilledRectangle(Color.Yellow, c.x - HandleSize / 2, c.y - HandleSize / 2, HandleSize, HandleSize);
            }
        }

        if (debug)
            DrawString($"m={lastMX},{lastMY} origin={OriginX},{OriginY} L={lastL} hit={lastHit} drag={drag} sel={selected?.name ?? "-"} undo={undo.Count}",
                       Color.White, 6, Height - 16, 12);
    }

    public override string GetComponentName() => "DesignerSurface";
}