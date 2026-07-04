using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace taskTru;

internal sealed class DarkScrollBar : Control
{
    private const int MinimumThumbHeight = 28;
    private const float TrackWidth = 3;
    private const float ThumbWidth = 5;

    private int _maximum;
    private int _largeChange = 1;
    private int _value;
    private bool _dragging;
    private bool _hoveringThumb;
    private int _dragOffset;

    public event EventHandler? ValueChanged;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => _maximum;
        set
        {
            int maximum = Math.Max(0, value);
            if (_maximum == maximum)
                return;

            _maximum = maximum;
            Value = _value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LargeChange
    {
        get => _largeChange;
        set
        {
            int largeChange = Math.Max(1, value);
            if (_largeChange == largeChange)
                return;

            _largeChange = largeChange;
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
            int newValue = Math.Clamp(value, 0, Maximum);
            if (_value == newValue)
                return;

            _value = newValue;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public DarkScrollBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
            true);
        BackColor = UiTheme.AppBackground;
        Cursor = Cursors.Hand;
        Width = UiTheme.ScrollBarWidth;
        AccessibleRole = AccessibleRole.ScrollBar;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        if (Maximum <= 0)
            return;

        e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        DrawCapsule(
            e.Graphics,
            GetTrackRectangle(),
            UiScale.ToDevice((int)TrackWidth, DeviceDpi),
            UiTheme.ScrollTrack);
        DrawCapsule(
            e.Graphics,
            GetThumbRectangle(),
            UiScale.ToDevice((int)ThumbWidth, DeviceDpi),
            _dragging || _hoveringThumb
                ? UiTheme.ScrollThumbActive
                : UiTheme.ScrollThumb);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || Maximum <= 0)
            return;

        Rectangle thumb = GetThumbRectangle();
        if (thumb.Contains(e.Location))
        {
            _dragging = true;
            _dragOffset = e.Y - thumb.Top;
            Capture = true;
            return;
        }

        Value += e.Y < thumb.Top ? -LargeChange : LargeChange;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            Rectangle track = GetTrackRectangle();
            Rectangle thumb = GetThumbRectangle();
            int travel = track.Height - thumb.Height;
            if (travel > 0)
            {
                int top = Math.Clamp(e.Y - _dragOffset - track.Top, 0, travel);
                Value = (int)Math.Round(top * Maximum / (double)travel);
            }

            return;
        }

        Rectangle hit = GetThumbRectangle();
        hit.Inflate(UiScale.ToDevice(2, DeviceDpi), 0);
        bool hovering = hit.Contains(e.Location);
        if (_hoveringThumb != hovering)
        {
            _hoveringThumb = hovering;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_dragging)
        {
            _hoveringThumb = false;
            Invalidate();
        }
    }

    private Rectangle GetTrackRectangle()
    {
        int inset = UiScale.ToDevice(3, DeviceDpi);
        return new(0, inset, Width, Math.Max(1, Height - inset * 2));
    }

    private Rectangle GetThumbRectangle()
    {
        Rectangle track = GetTrackRectangle();
        if (Maximum <= 0)
            return Rectangle.Empty;

        int contentHeight = Maximum + LargeChange;
        int thumbHeight = Math.Clamp(
            (int)Math.Round(track.Height * LargeChange / (double)contentHeight),
            Math.Min(UiScale.ToDevice(MinimumThumbHeight, DeviceDpi), track.Height),
            track.Height);
        int travel = track.Height - thumbHeight;
        int top = travel == 0
            ? track.Top
            : track.Top + (int)Math.Round(travel * Value / (double)Maximum);
        return new(0, top, Width, thumbHeight);
    }

    private static void DrawCapsule(Graphics graphics, Rectangle bounds, float width, Color color)
    {
        if (bounds.Height <= 0)
            return;

        float centerX = bounds.Left + bounds.Width / 2f;
        float radius = width / 2f;
        using var pen = new Pen(color, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawLine(
            pen,
            centerX,
            bounds.Top + radius,
            centerX,
            Math.Max(bounds.Top + radius, bounds.Bottom - radius));
    }
}
