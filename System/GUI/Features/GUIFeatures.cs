using System.Drawing;

public static class GUIFeatures
{

    public static Color Darken(Color color, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);

        return Color.FromArgb(
            color.A,
            (int)(color.R * (1f - amount)),
            (int)(color.G * (1f - amount)),
            (int)(color.B * (1f - amount))
        );
    }
}