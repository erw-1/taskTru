using static taskTru.NativeMethods;

namespace taskTru;

internal static class WindowTheme
{
    private const int ImmersiveDarkMode = 20;
    private const int ImmersiveDarkModeLegacy = 19;
    private const int WindowCornerPreference = 33;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int ColorNone = unchecked((int)0xFFFFFFFE);
    private const int ColorDefault = unchecked((int)0xFFFFFFFF);
    private const int RoundCorners = 2;
    private const int RoundSmallCorners = 3;

    public static void Apply(nint handle)
    {
        int enabled = 1;
        if (!TrySet(handle, ImmersiveDarkMode, ref enabled))
            _ = TrySet(handle, ImmersiveDarkModeLegacy, ref enabled);

        int corners = RoundCorners;
        _ = TrySet(handle, WindowCornerPreference, ref corners);

        int caption = UiTheme.AppBackground.R
            | UiTheme.AppBackground.G << 8
            | UiTheme.AppBackground.B << 16;
        _ = TrySet(handle, CaptionColor, ref caption);
    }

    public static void ApplySmallCorners(nint handle)
    {
        int corners = RoundSmallCorners;
        _ = TrySet(handle, WindowCornerPreference, ref corners);
        HideBorder(handle);
    }

    public static void HideBorder(nint handle)
    {
        int border = ColorNone;
        _ = TrySet(handle, BorderColor, ref border);
    }

    public static void RestoreBorder(nint handle)
    {
        int border = ColorDefault;
        _ = TrySet(handle, BorderColor, ref border);
    }

    private static bool TrySet(nint handle, int attribute, ref int value)
    {
        try
        {
            return DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
