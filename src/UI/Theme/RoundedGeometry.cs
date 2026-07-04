using System.Drawing.Drawing2D;

namespace taskTru;

internal static class RoundedGeometry
{
    public static GraphicsPath CreatePath(
        Rectangle rectangle,
        int radius)
    {
        var path = new GraphicsPath();
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
            return path;

        radius = Math.Clamp(
            radius,
            1,
            Math.Max(
                1,
                Math.Min(
                    rectangle.Width,
                    rectangle.Height) / 2));
        int diameter = radius * 2;
        path.AddArc(
            rectangle.Left,
            rectangle.Top,
            diameter,
            diameter,
            180,
            90);
        path.AddArc(
            rectangle.Right - diameter,
            rectangle.Top,
            diameter,
            diameter,
            270,
            90);
        path.AddArc(
            rectangle.Right - diameter,
            rectangle.Bottom - diameter,
            diameter,
            diameter,
            0,
            90);
        path.AddArc(
            rectangle.Left,
            rectangle.Bottom - diameter,
            diameter,
            diameter,
            90,
            90);
        path.CloseFigure();
        return path;
    }
}
