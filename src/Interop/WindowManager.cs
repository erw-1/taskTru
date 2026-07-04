using System.Diagnostics;
using System.Text;
using static taskTru.NativeMethods;

namespace taskTru;

internal static class WindowManager
{
    private static readonly Dictionary<nint, OriginalWindowSettings> Originals = [];
    private static readonly string[] ShellProcesses =
    [
        "ctfmon", "dwm", "GameBar", "GameBarFTServer", "LockApp",
        "LogonUI", "MusNotification", "MusNotifyIcon", "RuntimeBroker",
        "SearchApp", "SearchHost", "SecurityHealthSystray",
        "ShellExperienceHost", "ShellHost", "sihost",
        "StartMenuExperienceHost", "TextInputHost", "WidgetService",
        "Widgets"
    ];

    private static readonly string[] ShellClasses =
    [
        "ApplicationManager_ImmersiveShellWindow", "ControlCenterWindow",
        "ForegroundStaging", "MultitaskingViewFrame",
        "NotifyIconOverflowWindow", "Progman", "Shell_SecondaryTrayWnd",
        "Shell_TrayWnd", "TopLevelWindowForOverflowXamlIsland",
        "Windows.Internal.Shell.TabProxyWindow", "WorkerW",
        "XamlExplorerHostIslandWindow"
    ];

    private static readonly string[] ShellTitles =
    [
        "Microsoft Text Input Application", "Notification Center",
        "PopupHost", "Program Manager", "Quick settings", "Search",
        "Start", "System tray overflow window.", "Task Switching",
        "Task View", "Windows Input Experience"
    ];

    public static List<WindowInfo> Enumerate(nint excludedHandle)
    {
        CleanupUnavailableWindows();
        var windows = new List<WindowInfo>();
        var processNames = new Dictionary<uint, string>();

        EnumWindows((handle, _) =>
        {
            if (handle == excludedHandle
                || !TryGetWindowInfo(
                    handle,
                    out WindowInfo? window,
                    processNames))
            {
                return true;
            }

            windows.Add(window);
            return true;
        }, 0);

        return windows;
    }

    public static bool TryGetWindowInfo(
        nint handle,
        out WindowInfo window) =>
        TryGetWindowInfo(
            handle,
            out window,
            processNames: null);

    private static bool TryGetWindowInfo(
        nint handle,
        out WindowInfo window,
        Dictionary<uint, string>? processNames)
    {
        window = null!;
        if (!IsWindow(handle) || !IsWindowVisible(handle))
            return false;

        _ = GetWindowThreadProcessId(
            handle,
            out uint processId);
        if (processId == 0
            || processId == Environment.ProcessId)
        {
            return false;
        }

        int titleLength = GetWindowTextLength(handle);
        if (titleLength == 0)
            return false;

        var titleBuffer = new StringBuilder(titleLength + 1);
        _ = GetWindowText(
            handle,
            titleBuffer,
            titleBuffer.Capacity);
        string title = titleBuffer.ToString();

        var classBuffer = new StringBuilder(64);
        _ = GetClassName(
            handle,
            classBuffer,
            classBuffer.Capacity);
        string className = classBuffer.ToString();
        string processName =
            GetProcessName(processId, processNames);
        if (IsWindowsUiTask(
                title,
                className,
                processName))
        {
            return false;
        }

        window = new(handle, title, processName);
        return true;
    }

    public static WindowState ReadState(nint handle)
    {
        ExtendedWindowStyle style = GetStyle(handle);
        int opacity = 100;

        if (style.HasFlag(ExtendedWindowStyle.Layered)
            && GetLayeredWindowAttributes(handle, out _, out byte alpha, out LayeredWindowAttribute flags)
            && flags.HasFlag(LayeredWindowAttribute.Alpha))
        {
            opacity = (int)Math.Round(alpha * 100d / byte.MaxValue);
        }

        return new(
            style.HasFlag(ExtendedWindowStyle.Transparent),
            style.HasFlag(ExtendedWindowStyle.TopMost),
            opacity);
    }

    public static void ApplyStoredState(nint handle, WindowState state)
    {
        if (!IsWindow(handle))
            return;

        RememberOriginal(handle);
        SetClickThroughRaw(handle, state.ClickThrough);
        SetTopMostRaw(handle, state.TopMost);
        SetOpacityRaw(handle, state.Opacity);
    }

    public static void Reset(nint handle)
    {
        if (!IsWindow(handle))
        {
            Originals.Remove(handle);
            return;
        }

        if (Originals.Remove(
            handle,
            out OriginalWindowSettings original))
        {
            ExtendedWindowStyle currentStyle = GetStyle(handle);
            if (currentStyle.HasFlag(ExtendedWindowStyle.Layered))
            {
                SetLayeredWindowAttributes(
                    handle,
                    original.ColorKey,
                    original.HasLayeredAttributes
                        ? original.Alpha
                        : byte.MaxValue,
                    original.HasLayeredAttributes
                        ? original.LayeredFlags | LayeredWindowAttribute.Alpha
                        : LayeredWindowAttribute.Alpha);
            }

            SetStyle(handle, original.Style);
            SetTopMostRaw(
                handle,
                original.Style.HasFlag(ExtendedWindowStyle.TopMost));

            if (original.HasLayeredAttributes)
            {
                SetLayeredWindowAttributes(
                    handle,
                    original.ColorKey,
                    original.Alpha,
                    original.LayeredFlags);
            }

            return;
        }

        SetClickThroughRaw(handle, false);
        SetTopMostRaw(handle, false);
        SetOpacityRaw(handle, 100);
    }

    public static bool IsTracked(nint handle) =>
        Originals.ContainsKey(handle);

    public static bool HasActiveChanges(nint handle) =>
        Originals.TryGetValue(
            handle,
            out OriginalWindowSettings original)
        && ReadState(handle) != original.State;

    public static nint[] TrackedHandles => [.. Originals.Keys];

    public static void Track(nint handle) => RememberOriginal(handle);

    public static void Forget(nint handle) => Originals.Remove(handle);

    private static void CleanupUnavailableWindows()
    {
        foreach (nint handle in Originals.Keys
                     .Where(handle => !IsWindow(handle))
                     .ToArray())
        {
            Originals.Remove(handle);
        }
    }

    private static void SetClickThroughRaw(nint handle, bool enabled)
    {
        ExtendedWindowStyle style = GetStyle(handle);
        if (enabled)
        {
            style |= ExtendedWindowStyle.Layered
                | ExtendedWindowStyle.Transparent;
        }
        else
        {
            style &= ~ExtendedWindowStyle.Transparent;
            if (!RequiresLayeredStyle(
                    handle,
                    style))
            {
                style &=
                    ~ExtendedWindowStyle.Layered;
            }
        }

        SetStyle(handle, style);
    }

    private static void SetTopMostRaw(nint handle, bool enabled)
    {
        SetWindowPos(
            handle,
            enabled ? TopMost : NotTopMost,
            0,
            0,
            0,
            0,
            WindowPositionFlags.NoMove
                | WindowPositionFlags.NoSize
                | WindowPositionFlags.NoActivate);
    }

    private static void SetOpacityRaw(nint handle, int opacity)
    {
        ExtendedWindowStyle style = GetStyle(handle);
        if (opacity >= 100
            && !style.HasFlag(
                ExtendedWindowStyle.Transparent)
            && Originals.TryGetValue(
                handle,
                out OriginalWindowSettings original)
            && !original.Style.HasFlag(
                ExtendedWindowStyle.Layered))
        {
            SetStyle(
                handle,
                style & ~ExtendedWindowStyle.Layered);
            return;
        }

        if (!style.HasFlag(ExtendedWindowStyle.Layered))
            SetStyle(handle, style | ExtendedWindowStyle.Layered);

        byte alpha = (byte)(Math.Clamp(opacity, 0, 100) * byte.MaxValue / 100);
        SetLayeredWindowAttributes(handle, 0, alpha, LayeredWindowAttribute.Alpha);
    }

    private static bool RequiresLayeredStyle(
        nint handle,
        ExtendedWindowStyle style)
    {
        if (Originals.TryGetValue(
                handle,
                out OriginalWindowSettings original)
            && original.Style.HasFlag(
                ExtendedWindowStyle.Layered))
        {
            return true;
        }

        return style.HasFlag(
                ExtendedWindowStyle.Layered)
            && GetLayeredWindowAttributes(
                handle,
                out _,
                out byte alpha,
                out LayeredWindowAttribute flags)
            && (flags.HasFlag(
                    LayeredWindowAttribute.ColorKey)
                || (flags.HasFlag(
                        LayeredWindowAttribute.Alpha)
                    && alpha < byte.MaxValue));
    }

    private static void RememberOriginal(nint handle)
    {
        if (!IsWindow(handle) || Originals.ContainsKey(handle))
            return;

        ExtendedWindowStyle style = GetStyle(handle);
        uint colorKey = 0;
        byte alpha = byte.MaxValue;
        LayeredWindowAttribute flags = LayeredWindowAttribute.Alpha;
        bool hasLayeredAttributes = style.HasFlag(
                ExtendedWindowStyle.Layered)
            && GetLayeredWindowAttributes(
                handle,
                out colorKey,
                out alpha,
                out flags);

        Originals.Add(
            handle,
            new(
                style,
                hasLayeredAttributes,
                colorKey,
                alpha,
                flags));
    }

    private static ExtendedWindowStyle GetStyle(nint handle) =>
        (ExtendedWindowStyle)(uint)GetWindowLong(handle, ExtendedStyleIndex);

    private static void SetStyle(nint handle, ExtendedWindowStyle style) =>
        _ = SetWindowLong(
            handle,
            ExtendedStyleIndex,
            unchecked((int)style));

    private static string GetProcessName(
        uint processId,
        Dictionary<uint, string>? processNames)
    {
        if (processId == 0)
            return string.Empty;

        if (processNames?.TryGetValue(
                processId,
                out string? cached) == true)
        {
            return cached;
        }

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            string processName = process.ProcessName;
            if (processNames is not null)
                processNames[processId] = processName;
            return processName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsWindowsUiTask(
        string title,
        string className,
        string processName) =>
        ShellProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase)
        || ShellClasses.Contains(className, StringComparer.OrdinalIgnoreCase)
        || ShellTitles.Contains(title, StringComparer.OrdinalIgnoreCase);

    private readonly record struct OriginalWindowSettings(
        ExtendedWindowStyle Style,
        bool HasLayeredAttributes,
        uint ColorKey,
        byte Alpha,
        LayeredWindowAttribute LayeredFlags)
    {
        public WindowState State
        {
            get
            {
                int opacity =
                    Style.HasFlag(ExtendedWindowStyle.Layered)
                    && HasLayeredAttributes
                    && LayeredFlags.HasFlag(LayeredWindowAttribute.Alpha)
                        ? (int)Math.Round(Alpha * 100d / byte.MaxValue)
                        : 100;

                return new(
                    Style.HasFlag(ExtendedWindowStyle.Transparent),
                    Style.HasFlag(ExtendedWindowStyle.TopMost),
                    opacity);
            }
        }
    }
}

internal sealed record WindowInfo(
    nint Handle,
    string Title,
    string ProcessName)
{
    public string StateKey =>
        $"{ProcessName}\n{Title}";
}

internal sealed record WindowState(
    bool ClickThrough = false,
    bool TopMost = false,
    int Opacity = 100)
{
    public bool IsDefault =>
        !ClickThrough
        && !TopMost
        && Opacity == 100;
}
