using System.ComponentModel;
using static taskTru.NativeMethods;

namespace taskTru;

internal sealed class ShortcutCaptureButton : RoundedActionButton
{
    private ShortcutGesture _gesture;
    private bool _capturing;

    public event EventHandler<ShortcutGestureChangingEventArgs>? GestureChanging;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ShortcutGesture Gesture
    {
        get => _gesture;
        set => SetGesture(value);
    }

    public ShortcutCaptureButton(ShortcutGesture gesture)
    {
        _gesture = gesture;
        AccessibleName = "Keyboard shortcut";
        UpdateText();
    }

    public void SetGesture(ShortcutGesture gesture)
    {
        _gesture = gesture;
        StopCapture();
    }

    protected override void OnClick(EventArgs e)
    {
        if (_capturing)
            return;

        base.OnClick(e);
        _capturing = true;
        BorderColor = UiTheme.Accent;
        BorderWidth = 2;
        Text = "Press shortcut...";
        Focus();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        StopCapture();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (!_capturing)
            return base.ProcessCmdKey(ref message, keyData);

        Keys key = keyData & Keys.KeyCode;
        if (key == Keys.Escape)
        {
            StopCapture();
            return true;
        }

        ShortcutGesture gesture = new(GetModifiers(keyData), key);
        if (!Shortcuts.IsValid(gesture))
        {
            if (key is not (
                Keys.ControlKey
                or Keys.LControlKey
                or Keys.RControlKey
                or Keys.ShiftKey
                or Keys.LShiftKey
                or Keys.RShiftKey
                or Keys.Menu
                or Keys.LMenu
                or Keys.RMenu
                or Keys.LWin
                or Keys.RWin))
            {
                Text = "Modifier required";
            }

            return true;
        }

        var args = new ShortcutGestureChangingEventArgs(Shortcuts.Clean(gesture));
        GestureChanging?.Invoke(this, args);
        if (!args.Cancel)
            _gesture = args.Gesture;

        StopCapture();
        return true;
    }

    private static HotKeyModifiers GetModifiers(Keys keyData)
    {
        HotKeyModifiers modifiers = HotKeyModifiers.None;
        if (keyData.HasFlag(Keys.Control))
            modifiers |= HotKeyModifiers.Control;
        if (keyData.HasFlag(Keys.Alt))
            modifiers |= HotKeyModifiers.Alt;
        if (keyData.HasFlag(Keys.Shift))
            modifiers |= HotKeyModifiers.Shift;
        return modifiers;
    }

    private void StopCapture()
    {
        _capturing = false;
        BorderColor = UiTheme.Border;
        BorderWidth = 1;
        UpdateText();
    }

    private void UpdateText()
    {
        Text = Shortcuts.Format(_gesture);
        AccessibleDescription = $"Current shortcut: {Text}";
    }
}

internal sealed class ShortcutGestureChangingEventArgs(ShortcutGesture gesture)
    : CancelEventArgs
{
    public ShortcutGesture Gesture { get; } = gesture;
}
