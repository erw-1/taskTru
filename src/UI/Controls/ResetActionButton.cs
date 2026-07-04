using System.Drawing.Drawing2D;

namespace taskTru;

internal sealed class ResetActionButton : IconActionButton
{
    public ResetActionButton() => AccessibleName = "Reset window";

    protected override void DrawIcon(Graphics graphics, Color color)
    {
        float availableSize = Math.Min(
            ClientSize.Width,
            ClientSize.Height);
        float iconSize = Math.Min(
            UiScale.ToDevice(17f, DeviceDpi),
            availableSize * 0.68f);
        if (iconSize <= 0)
            return;

        float left = (ClientSize.Width - iconSize) / 2f;
        float top = (ClientSize.Height - iconSize) / 2f;
        float strokeWidth = Math.Max(1f, iconSize * 1.4f / 17f);
        float unit = iconSize / 20f;
        const float center = 10f;
        const float outerRadius = 8.3f;
        const float innerRadius = 5.2f;
        const float headAngle = 225f;
        const float tailAngle = 150f;
        const float ringSweep = 285f;

        RectangleF CircleBounds(float radius) =>
            new(
                left + (center - radius) * unit,
                top + (center - radius) * unit,
                radius * 2 * unit,
                radius * 2 * unit);

        PointF CirclePoint(float radius, float angleDegrees)
        {
            double angle = angleDegrees * Math.PI / 180d;
            return new(
                left + (center + radius * (float)Math.Cos(angle)) * unit,
                top + (center + radius * (float)Math.Sin(angle)) * unit);
        }

        using var iconPen = new Pen(
            color,
            strokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var arrow = new GraphicsPath();
        arrow.StartFigure();
        arrow.AddArc(CircleBounds(outerRadius), headAngle, ringSweep);

        PointF outerTail = CirclePoint(outerRadius, tailAngle);
        PointF innerTail = CirclePoint(innerRadius, tailAngle);
        double tailRadians = tailAngle * Math.PI / 180d;
        float capControl = (outerRadius - innerRadius) * 2f / 3f * unit;
        var tailDirection = new PointF(
            -(float)Math.Sin(tailRadians),
            (float)Math.Cos(tailRadians));
        arrow.AddBezier(
            outerTail,
            new(
                outerTail.X + tailDirection.X * capControl,
                outerTail.Y + tailDirection.Y * capControl),
            new(
                innerTail.X + tailDirection.X * capControl,
                innerTail.Y + tailDirection.Y * capControl),
            innerTail);
        arrow.AddArc(CircleBounds(innerRadius), tailAngle, -ringSweep);

        double headRadians = headAngle * Math.PI / 180d;
        var headNormal = new PointF(
            (float)Math.Cos(headRadians),
            (float)Math.Sin(headRadians));
        var headDirection = new PointF(
            (float)Math.Sin(headRadians),
            -(float)Math.Cos(headRadians));
        PointF headCenter = CirclePoint(
            (outerRadius + innerRadius) / 2f,
            headAngle);
        float headLength = 6f * unit;
        PointF headTip = new(
            headCenter.X + headDirection.X * headLength,
            headCenter.Y + headDirection.Y * headLength);
        PointF innerHead = CirclePoint(innerRadius, headAngle);
        PointF outerHead = CirclePoint(outerRadius, headAngle);
        float shoulderDepth = 2f * unit;
        PointF innerShoulder = new(
            innerHead.X - headNormal.X * shoulderDepth,
            innerHead.Y - headNormal.Y * shoulderDepth);
        PointF outerShoulder = new(
            outerHead.X + headNormal.X * shoulderDepth,
            outerHead.Y + headNormal.Y * shoulderDepth);
        arrow.AddLines(
            [
                innerHead,
                innerShoulder,
                headTip,
                outerShoulder,
                outerHead
            ]);
        arrow.CloseFigure();
        graphics.DrawPath(iconPen, arrow);
    }
}
