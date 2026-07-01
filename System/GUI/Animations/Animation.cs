namespace Windose;

public sealed class Animation
{
    public Component Target;
    public double DurationMs = 200;
    public double ElapsedMs;
    public EaseType Ease = EaseType.EaseOut;
    public Action<float> OnUpdate;
    public Action OnComplete;

    public float Progress
    {
        get
        {
            if (DurationMs <= 0) return 1f;
            return (float)Math.Min(1.0, ElapsedMs / DurationMs);
        }
    }

    public float EasedProgress => Easing.Apply(Progress, Ease);

    public bool IsComplete => Progress >= 1f;
}
