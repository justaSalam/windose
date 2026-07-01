using System.Drawing;

namespace Windose;

public static class AnimationManager
{
    private static readonly List<Animation> active = new();
    private static readonly Dictionary<Component, Animation> keyed = new();

    public static bool HasActive => active.Count > 0;

    public static bool IsAnimating(Component target)
    {
        return target != null && keyed.ContainsKey(target);
    }

    public static void Run(Animation animation)
    {
        if (animation == null) return;

        if (animation.Target != null)
        {
            if (keyed.TryGetValue(animation.Target, out Animation existing))
            {
                active.Remove(existing);
                keyed.Remove(animation.Target);
            }

            keyed[animation.Target] = animation;
        }

        animation.ElapsedMs = 0;
        active.Add(animation);
    }

    public static void Run(Component target, double durationMs, EaseType ease, Action<float> onUpdate, Action onComplete = null)
    {
        Run(new Animation
        {
            Target = target,
            DurationMs = durationMs,
            Ease = ease,
            OnUpdate = onUpdate,
            OnComplete = onComplete
        });
    }

    public static void Cancel(Component target)
    {
        if (target == null || !keyed.TryGetValue(target, out Animation animation)) return;

        active.Remove(animation);
        keyed.Remove(target);
    }

    public static void Update(double deltaMs)
    {
        if (active.Count == 0) return;

        for (int i = active.Count - 1; i >= 0; i--)
        {
            Animation animation = active[i];
            animation.ElapsedMs += deltaMs;

            float eased = animation.EasedProgress;
            animation.OnUpdate?.Invoke(eased);

            if (animation.Target != null)
            {
                animation.Target.MarkDirty(false);
                WindowManager.Invalidate(animation.Target);
            }

            if (!animation.IsComplete) continue;

            animation.OnComplete?.Invoke();

            if (animation.Target != null)
                keyed.Remove(animation.Target);

            active.RemoveAt(i);
        }
    }

    public static int Lerp(int from, int to, float t)
    {
        return from + (int)((to - from) * t);
    }

    public static Rectangle Lerp(Rectangle from, Rectangle to, float t)
    {
        return new Rectangle(
            Lerp(from.X, to.X, t),
            Lerp(from.Y, to.Y, t),
            Lerp(from.Width, to.Width, t),
            Lerp(from.Height, to.Height, t));
    }
}
