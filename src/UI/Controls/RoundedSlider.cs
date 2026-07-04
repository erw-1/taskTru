using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace taskTru;

internal sealed class RoundedSlider : Control
{
    private const int ThumbDiameter = 12;
    private const int TrackWidth = 4;

    private int _maximum = 100;
    private int _value = 100;
    private bool _dragging;

    public event EventHandler? ValueChanged;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(Minimum + 1, value);
            Value = _value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            int next = Math.Clamp(value, Minimum, Maximum);
            if (_value == next)
                return;

            _value = next;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public RoundedSlider()
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
        AccessibleRole = AccessibleRole.Slider;
        AccessibleName = "Opacity";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        int thumbDiameter = UiScale.ToDevice(ThumbDiameter, DeviceDpi);
        float trackWidth = UiScale.ToDevice((float)TrackWidth, DeviceDpi);
        int left = thumbDiameter / 2;
        int right = Math.Max(left, ClientSize.Width - thumbDiameter / 2);
        int centerY = ClientSize.Height / 2;
        int thumbX = ValueToX(Value, left, right);

        using var track = new Pen(UiTheme.SliderTrack, trackWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        e.Graphics.DrawLine(track, left, centerY, right, centerY);

        using var thumb = new SolidBrush(UiTheme.Accent);
        e.Graphics.FillEllipse(
            thumb,
            thumbX - thumbDiameter / 2,
            centerY - thumbDiameter / 2,
            thumbDiameter,
            thumbDiameter);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;

        Focus();
        _dragging = true;
        Capture = true;
        SetValueFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
            SetValueFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left)
        {
            Value--;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Right)
        {
            Value++;
            e.Handled = true;
        }
    }

    private void SetValueFromX(int x)
    {
        int thumbDiameter = UiScale.ToDevice(ThumbDiameter, DeviceDpi);
        int left = thumbDiameter / 2;
        int right = Math.Max(left + 1, ClientSize.Width - thumbDiameter / 2);
        int clampedX = Math.Clamp(x, left, right);
        Value = Minimum + (int)Math.Round(
            (clampedX - left) * (Maximum - Minimum) / (double)(right - left));
    }

    private int ValueToX(int value, int left, int right) =>
        left + (int)Math.Round(
            (value - Minimum) * (right - left) / (double)(Maximum - Minimum));
}
