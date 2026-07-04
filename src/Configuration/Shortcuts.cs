using static taskTru.NativeMethods;

namespace taskTru;

internal enum ShortcutAction
{
    ClickThrough = 0x7521,
    TopMost = 0x7522,
    OpacityUp = 0x7523,
    OpacityDown = 0x7524,
    Crop = 0x7525,
    Uncrop = 0x7526,
    RestoreAll = 0x7527,
    ShowTaskTru = 0x7528,
    VideoMode = 0x7529,
    Interact = 0x752A
}

internal sealed record ShortcutDefinition(
    ShortcutAction Action,
    ShortcutGesture DefaultGesture,
    string ActionName);

internal readonly record struct ShortcutGesture(
    HotKeyModifiers Modifiers,
    Keys Key);

internal static class Shortcuts
{
    private const HotKeyModifiers DefaultModifiers =
        HotKeyModifiers.Control
        | HotKeyModifiers.Alt;
    private const HotKeyModifiers AllowedModifiers =
        HotKeyModifiers.Control
        | HotKeyModifiers.Alt
        | HotKeyModifiers.Shift
        | HotKeyModifiers.Windows;

    public static readonly ShortcutDefinition[] All =
    [
        new(ShortcutAction.ClickThrough, new(DefaultModifiers, Keys.X), "Click-through"),
        new(ShortcutAction.TopMost, new(DefaultModifiers, Keys.T), "Lock on top"),
        new(ShortcutAction.OpacityUp, new(DefaultModifiers, Keys.Up), "Opacity +5%"),
        new(ShortcutAction.OpacityDown, new(DefaultModifiers, Keys.Down), "Opacity -5%"),
        new(ShortcutAction.Crop, new(DefaultModifiers, Keys.C), "Crop and recrop"),
        new(ShortcutAction.VideoMode, new(DefaultModifiers, Keys.V), "Attempt auto video crop"),
        new(ShortcutAction.Uncrop, new(DefaultModifiers, Keys.U), "Uncrop"),
        new(ShortcutAction.Interact, new(DefaultModifiers, Keys.I), "Interact with cropped task"),
        new(ShortcutAction.RestoreAll, new(DefaultModifiers, Keys.R), "Restore everything"),
        new(ShortcutAction.ShowTaskTru, new(DefaultModifiers, Keys.S), "Show taskTru")
    ];

    public static Dictionary<string, ShortcutGesture> Normalize(
        IReadOnlyDictionary<string, ShortcutGesture>? shortcuts)
    {
        Dictionary<string, ShortcutGesture> normalized =
            All.ToDictionary(
                shortcut => shortcut.Action.ToString(),
                shortcut => shortcut.DefaultGesture);
        var used = normalized.Values.ToHashSet();
        foreach (ShortcutDefinition shortcut in All)
        {
            string key = shortcut.Action.ToString();
            if (shortcuts?.TryGetValue(key, out ShortcutGesture gesture) != true
                || !IsValid(gesture))
            {
                continue;
            }

            gesture = Clean(gesture);
            ShortcutGesture current = normalized[key];
            used.Remove(current);
            if (used.Add(gesture))
                normalized[key] = gesture;
            else
                used.Add(current);
        }

        return normalized;
    }

    public static ShortcutGesture Get(
        IReadOnlyDictionary<string, ShortcutGesture> shortcuts,
        ShortcutAction action)
    {
        string key = action.ToString();
        return shortcuts.TryGetValue(key, out ShortcutGesture gesture)
            && IsValid(gesture)
                ? Clean(gesture)
                : All.First(shortcut => shortcut.Action == action)
                    .DefaultGesture;
    }

    public static bool IsValid(ShortcutGesture gesture)
    {
        Keys key = gesture.Key & Keys.KeyCode;
        HotKeyModifiers modifiers = gesture.Modifiers & AllowedModifiers;
        return key is not (
                   Keys.None
                   or Keys.ControlKey
                   or Keys.LControlKey
                   or Keys.RControlKey
                   or Keys.ShiftKey
                   or Keys.LShiftKey
                   or Keys.RShiftKey
                   or Keys.Menu
                   or Keys.LMenu
                   or Keys.RMenu
                   or Keys.LWin
                   or Keys.RWin)
               && (modifiers != 0
                   || key is >= Keys.F1 and <= Keys.F24);
    }

    public static string Format(ShortcutGesture gesture)
    {
        gesture = Clean(gesture);
        var parts = new List<string>(5);
        if (gesture.Modifiers.HasFlag(HotKeyModifiers.Control))
            parts.Add("Ctrl");
        if (gesture.Modifiers.HasFlag(HotKeyModifiers.Alt))
            parts.Add("Alt");
        if (gesture.Modifiers.HasFlag(HotKeyModifiers.Shift))
            parts.Add("Shift");
        if (gesture.Modifiers.HasFlag(HotKeyModifiers.Windows))
            parts.Add("Win");

        parts.Add(gesture.Key switch
        {
            Keys.Up => "Up",
            Keys.Down => "Down",
            Keys.Left => "Left",
            Keys.Right => "Right",
            Keys.PageUp => "Page Up",
            Keys.PageDown => "Page Down",
            Keys.OemMinus => "-",
            Keys.Oemplus => "+",
            >= Keys.D0 and <= Keys.D9 => ((int)(gesture.Key - Keys.D0)).ToString(),
            _ => gesture.Key.ToString()
        });
        return string.Join("+", parts);
    }

    public static ShortcutGesture Clean(ShortcutGesture gesture) =>
        new(
            gesture.Modifiers & AllowedModifiers,
            gesture.Key & Keys.KeyCode);
}
