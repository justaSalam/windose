// DragDrop.cs - minimal drag-and-drop: a source starts a drag with text payload,
// the window manager routes the pointer to the topmost AllowDrop target,
// and the target gets Enter/Leave/Drop callbacks.
public sealed class DragData
{
    public Component Source;
    public string Text = "";
    public object Payload;
    public int X;
    public int Y;
}

public static class DragDropManager
{
    public static bool IsDragging => Current != null;
    public static DragData Current { get; private set; }
    public static Component CurrentTarget { get; private set; }

    public static void StartDrag(Component source, string text, object payload = null)
    {
        Cancel();
        Current = new DragData { Source = source, Text = text ?? "", Payload = payload };
    }

    public static void UpdateTarget(Component target, int x, int y)
    {
        if (Current == null) return;
        if (target == CurrentTarget)
        {
            if (target != null)
            {
                Current.X = x;
                Current.Y = y;
                try { target.DragOver?.Invoke(Current); } catch { }
            }
            return;
        }
        if (CurrentTarget != null)
        {
            try { CurrentTarget.DragLeft?.Invoke(Current); } catch { }
        }
        CurrentTarget = target;
        if (target != null)
        {
            Current.X = x;
            Current.Y = y;
            try { target.DragEntered?.Invoke(Current); } catch { }
        }
    }

    public static bool FinishDrop(int x, int y)
    {
        DragData data = Current;
        Component target = CurrentTarget;
        Current = null;
        CurrentTarget = null;
        if (data == null || target == null) return false;
        data.X = x;
        data.Y = y;
        try { target.DragDropped?.Invoke(data); } catch { }
        return true;
    }

    public static void Cancel()
    {
        if (CurrentTarget != null && Current != null)
        {
            try { CurrentTarget.DragLeft?.Invoke(Current); } catch { }
        }
        Current = null;
        CurrentTarget = null;
    }
}
