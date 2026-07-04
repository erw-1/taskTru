namespace taskTru;

internal static class UiToolTips
{
    public static ToolTip Create(int autoPopDelay)
    {
        return new()
        {
            InitialDelay = 800,
            ReshowDelay = 400,
            AutoPopDelay = autoPopDelay,
            ShowAlways = true
        };
    }
}
