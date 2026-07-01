namespace Windose;

public enum EaseType
{
    Linear,
    EaseOut,
    EaseIn,
    EaseInOut
}

public static class Easing
{
    public static float Apply(float t, EaseType ease)
    {
        t = Math.Clamp(t, 0f, 1f);

        switch (ease)
        {
            case EaseType.EaseOut:
                return 1f - (1f - t) * (1f - t);
            case EaseType.EaseIn:
                return t * t;
            case EaseType.EaseInOut:
                return t < 0.5f
                    ? 2f * t * t
                    : 1f - MathF.Pow(-2f * t + 2f, 2f) / 2f;
            default:
                return t;
        }
    }
}
