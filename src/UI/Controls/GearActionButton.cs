using System.Drawing.Drawing2D;

namespace taskTru;

internal sealed class GearActionButton : IconActionButton
{
    public GearActionButton() => AccessibleName = "Settings";

    protected override void DrawIcon(Graphics graphics, Color color)
    {
        float size = Math.Min(
            UiScale.ToDevice(14f, DeviceDpi),
            Math.Min(ClientSize.Width, ClientSize.Height)
                - UiScale.ToDevice(7f, DeviceDpi));
        if (size <= 0)
            return;

        float centerX = ClientSize.Width / 2f;
        float centerY = ClientSize.Height / 2f;
        float outerRadius = size / 2f;
        float innerRadius = outerRadius * 0.73f;
        var points = new PointF[24];
        for (int index = 0; index < points.Length; index++)
        {
            double angle = -Math.PI / 2 + index * Math.PI * 2 / points.Length;
            float radius = index % 3 == 1 ? innerRadius : outerRadius;
            points[index] = new(
                centerX + radius * (float)Math.Cos(angle),
                centerY + radius * (float)Math.Sin(angle));
        }

        using var gear = new GraphicsPath(FillMode.Winding);
        gear.AddPolygon(points);
        using var pen = new Pen(color, UiScale.ToDevice(1.35f, DeviceDpi))
        {
            LineJoin = LineJoin.Round
        };
        float holeRadius = outerRadius * 0.28f;
        graphics.DrawPath(pen, gear);
        graphics.DrawEllipse(
            pen,
            centerX - holeRadius,
            centerY - holeRadius,
            holeRadius * 2,
            holeRadius * 2);
    }
}
