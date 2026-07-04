using System.Drawing.Drawing2D;

namespace taskTru;

internal abstract class IconActionButton : Control
{
    protected bool IsHovered { get; private set; }
    protected bool IsPressed { get; private set; }

    protected IconActionButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.UserPaint
                | ControlStyles.Selectable,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected sealed override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        Color color = GetIconColor();
        DrawIcon(e.Graphics, color);

        if (Focused && ShowFocusCues)
        {
            Rectangle focus = ClientRectangle;
            focus.Inflate(
                -UiScale.ToDevice(2, DeviceDpi),
                -UiScale.ToDevice(4, DeviceDpi));
            ControlPaint.DrawFocusRectangle(e.Graphics, focus, color, Color.Transparent);
        }
    }

    protected abstract void DrawIcon(Graphics graphics, Color color);

    protected virtual Color GetIconColor() =>
        !Enabled
            ? UiTheme.DisabledText
            : IsPressed
                ? Color.White
                : IsHovered
                    ? Color.Gainsboro
                    : Color.FromArgb(145, 145, 145);

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        IsHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        IsHovered = IsPressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;

        Focus();
        IsPressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        IsPressed = false;
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        IsPressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        IsHovered = IsPressed = false;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is not (Keys.Space or Keys.Enter))
            return;

        IsPressed = true;
        Invalidate();
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode is not (Keys.Space or Keys.Enter))
            return;

        IsPressed = false;
        Invalidate();
        OnClick(EventArgs.Empty);
        e.Handled = true;
    }
}
