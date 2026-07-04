using System.Drawing.Drawing2D;

namespace taskTru;

internal sealed class VideoActionButton : IconActionButton
{
    public VideoActionButton() => AccessibleName = "Attempt auto video crop";

    protected override void DrawIcon(Graphics graphics, Color color)
    {
        Rectangle bounds = ClientRectangle;
        float size = Math.Min(bounds.Width, bounds.Height) * 0.38f;
        float centerX = bounds.Left + bounds.Width / 2f;
        float centerY = bounds.Top + bounds.Height / 2f;
        PointF[] triangle =
        [
            new(centerX - size * 0.38f, centerY - size * 0.55f),
            new(centerX - size * 0.38f, centerY + size * 0.55f),
            new(centerX + size * 0.58f, centerY)
        ];
        using var brush = new SolidBrush(color);
        graphics.FillPolygon(brush, triangle, FillMode.Winding);
    }

    protected override Color GetIconColor() =>
        !Enabled
            ? UiTheme.DisabledText
            : IsPressed
                ? Color.White
                : IsHovered
                    ? Color.FromArgb(110, 200, 255)
                    : UiTheme.Accent;
}
