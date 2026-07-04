namespace taskTru;

internal static class ColorMath
{
    public static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            BlendChannel(from.R, to.R, amount),
            BlendChannel(from.G, to.G, amount),
            BlendChannel(from.B, to.B, amount));
    }

    private static int BlendChannel(int from, int to, float amount) =>
        (int)Math.Round(from + (to - from) * amount);
}
