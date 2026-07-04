using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace taskTru;

internal sealed class RoundedPanel : Panel
{
    private Color _fillColor = UiTheme.RowPrimary;
    private Color _borderColor = UiTheme.Border;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor
    {
        get => _fillColor;
        set
        {
            _fillColor = value;
            Invalidate();
        }
    }

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

    public RoundedPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = ClientRectangle;
        bounds.Width--;
        bounds.Height--;
        using GraphicsPath path = RoundedGeometry.CreatePath(
            bounds,
            UiScale.ToDevice(8, DeviceDpi));
        using var fill = new SolidBrush(FillColor);
        using var border = new Pen(
            BorderColor,
            UiScale.ToDevice(1f, DeviceDpi));
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }
}
