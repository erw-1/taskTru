using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace taskTru;

internal class RoundedActionButton : Control, IButtonControl
{
    private Color _borderColor = UiTheme.Border;
    private int _borderWidth = 1;
    private bool _hovered;
    private bool _pressed;
    private bool _subtle;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            _borderColor = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int BorderWidth
    {
        get => _borderWidth;
        set
        {
            _borderWidth = Math.Max(1, value);
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Subtle
    {
        get => _subtle;
        set
        {
            _subtle = value;
            Invalidate();
        }
    }

    public RoundedActionButton()
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
        ForeColor = Color.White;
        Cursor = Cursors.Hand;
        Font = new("Segoe UI", 8.25f);
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
    }

    [DefaultValue(DialogResult.None)]
    public DialogResult DialogResult { get; set; }

    public void NotifyDefault(bool value)
    {
    }

    public void PerformClick()
    {
        if (!Enabled)
            return;

        OnClick(EventArgs.Empty);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (DialogResult != DialogResult.None
            && FindForm() is { } form)
        {
            form.DialogResult = DialogResult;
        }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = ClientRectangle;
        bounds.Width--;
        bounds.Height--;
        using GraphicsPath path = RoundedGeometry.CreatePath(
            bounds,
            UiScale.ToDevice(5, DeviceDpi));
        Color fill = GetFillColor();
        if (fill.A > 0)
        {
            using var brush = new SolidBrush(fill);
            e.Graphics.FillPath(brush, path);
        }

        Color border = GetBorderColor();
        if (border.A > 0)
        {
            using var pen = new Pen(
                border,
                UiScale.ToDevice((float)BorderWidth, DeviceDpi))
            {
                Alignment = PenAlignment.Inset
            };
            e.Graphics.DrawPath(pen, path);
        }

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            ClientRectangle,
            Enabled ? ForeColor : UiTheme.DisabledText,
            TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPadding);

        if (!Focused || !ShowFocusCues)
            return;

        Rectangle focusBounds = ClientRectangle;
        focusBounds.Inflate(
            -UiScale.ToDevice(3, DeviceDpi),
            -UiScale.ToDevice(3, DeviceDpi));
        ControlPaint.DrawFocusRectangle(
            e.Graphics,
            focusBounds,
            Enabled ? ForeColor : UiTheme.DisabledText,
            Color.Transparent);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;

        Focus();
        _pressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = false;
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
        _pressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        _hovered = _pressed = false;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is not (Keys.Space or Keys.Enter))
            return;

        _pressed = true;
        Invalidate();
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode is not (Keys.Space or Keys.Enter))
            return;

        _pressed = false;
        Invalidate();
        PerformClick();
        e.Handled = true;
    }

    private Color GetFillColor()
    {
        if (!Enabled)
        {
            return Subtle
                ? Color.Transparent
                : UiTheme.DisabledBackground;
        }

        if (_pressed)
            return UiTheme.ButtonPressed;

        if (_hovered)
        {
            return Subtle
                ? UiTheme.SubtleHover
                : UiTheme.ButtonHover;
        }

        return Subtle
            ? Color.Transparent
            : UiTheme.ButtonBackground;
    }

    private Color GetBorderColor()
    {
        if (Subtle)
        {
            return Enabled && (_hovered || Focused)
                ? UiTheme.DisabledBorder
                : Color.Transparent;
        }

        return Enabled
            ? BorderColor
            : UiTheme.DisabledBorder;
    }
}
