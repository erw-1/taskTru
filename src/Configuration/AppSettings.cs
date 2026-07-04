using System.IO;

namespace taskTru;

internal sealed record AppSettings
{
    public bool StartWithWindows { get; init; }
    public bool StartMinimizedToTray { get; init; }
    public bool CloseButtonMinimizesToTray { get; init; } = true;
    public bool RestoreTasksOnExit { get; init; } = true;
    public bool ConfirmExitWithActiveTasks { get; init; }
    public bool AutoLockTopMostOnCrop { get; init; } = true;
    public bool ShowOpacityPercentage { get; init; }
    public bool EnableUpdateFlash { get; init; } = true;
    public bool ScanForVideoContent { get; init; } = true;
    public bool ManualTaskRefresh { get; init; }
    public bool CompactTaskRows { get; init; }
    public bool EnableKeybinds { get; init; } = true;
    public Dictionary<string, ShortcutGesture> KeyboardShortcuts { get; init; } = [];
    public bool EnableFavoriteTasks { get; init; }
    public bool EnableIgnoredTasks { get; init; }
    public string[] FavoriteExecutables { get; init; } = [];
    public string[] IgnoredExecutables { get; init; } = [];
    public int RefreshFrequencySeconds { get; init; } = 3;

    public AppSettings Normalize() => this with
    {
        StartMinimizedToTray =
            StartWithWindows && StartMinimizedToTray,
        KeyboardShortcuts =
            Shortcuts.Normalize(KeyboardShortcuts),
        RefreshFrequencySeconds = Math.Clamp(
            RefreshFrequencySeconds,
            1,
            60),
        FavoriteExecutables =
            NormalizeExecutables(FavoriteExecutables),
        IgnoredExecutables =
            NormalizeExecutables(IgnoredExecutables)
    };

    public bool IsFavorite(string processName) =>
        EnableFavoriteTasks
        && FavoriteExecutables.Contains(
            NormalizeExecutable(processName),
            StringComparer.OrdinalIgnoreCase);

    public bool IsIgnored(string processName) =>
        EnableIgnoredTasks
        && IgnoredExecutables.Contains(
            NormalizeExecutable(processName),
            StringComparer.OrdinalIgnoreCase);

    public ShortcutGesture GetShortcut(ShortcutAction action) =>
        Shortcuts.Get(KeyboardShortcuts, action);

    public AppSettings SetFavorite(string processName, bool favorite)
    {
        var values = new HashSet<string>(
            FavoriteExecutables,
            StringComparer.OrdinalIgnoreCase);
        string executable = NormalizeExecutable(processName);
        if (favorite)
            values.Add(executable);
        else
            values.Remove(executable);

        return this with
        {
            FavoriteExecutables = NormalizeExecutables(values)
        };
    }

    public AppSettings SetIgnored(string processName, bool ignored)
    {
        var values = new HashSet<string>(
            IgnoredExecutables,
            StringComparer.OrdinalIgnoreCase);
        string executable = NormalizeExecutable(processName);
        if (ignored)
            values.Add(executable);
        else
            values.Remove(executable);

        return this with
        {
            IgnoredExecutables = NormalizeExecutables(values)
        };
    }

    private static string NormalizeExecutable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string name = Path.GetFileName(value.Trim());
        return name.EndsWith(
                ".exe",
                StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
    }

    private static string[] NormalizeExecutables(
        IEnumerable<string>? values) =>
        values is null
            ? []
            : [.. values
                .Select(NormalizeExecutable)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)];
}
