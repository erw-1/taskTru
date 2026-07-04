namespace taskTru;

internal static class UiScale
{
    internal const int DefaultDpi = 96;

    public static int ToDevice(int logicalPixels, int dpi) =>
        (int)Math.Round(
            logicalPixels * Math.Max(DefaultDpi, dpi) / (double)DefaultDpi);

    public static float ToDevice(float logicalPixels, int dpi) =>
        logicalPixels * Math.Max(DefaultDpi, dpi) / DefaultDpi;

    public static Size ToDevice(Size logicalSize, int dpi) =>
        new(
            ToDevice(logicalSize.Width, dpi),
            ToDevice(logicalSize.Height, dpi));
}
