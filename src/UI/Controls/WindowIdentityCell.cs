namespace taskTru;

internal sealed class WindowIdentityCell : Control
{
    private const int TextGap = 8;
    private Image? _icon;

    public WindowIdentityCell(string title, Image? icon)
    {
        Text = title;
        _icon = icon;
        ForeColor = Color.White;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.UserPaint,
            true);
    }

    public void SetIcon(Image? icon)
    {
        Image? previous = _icon;
        _icon = icon;
        previous?.Dispose();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _icon?.Dispose();

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_icon is not null)
        {
            int iconSize = UiScale.ToDevice(WindowRow.WindowIconSize, DeviceDpi);
            e.Graphics.DrawImage(
                _icon,
                0,
                Math.Max(0, (ClientSize.Height - iconSize) / 2),
                iconSize,
                iconSize);
        }

        int textLeft = UiScale.ToDevice(WindowRow.WindowIconSize + TextGap, DeviceDpi);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            new Rectangle(
                textLeft,
                0,
                Math.Max(0, ClientSize.Width - textLeft),
                ClientSize.Height),
            ForeColor,
            TextFormatFlags.Left
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPadding);
    }
}
