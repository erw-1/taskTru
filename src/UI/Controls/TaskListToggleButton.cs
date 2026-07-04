using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace taskTru;

internal enum TaskListToggleKind
{
    Favorite,
    Ignore
}

internal sealed class TaskListToggleButton : IconActionButton
{
    private readonly TaskListToggleKind _kind;
    private bool _active;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value)
                return;

            _active = value;
            AccessibleDescription = GetAccessibleDescription();
            if (IsHandleCreated)
                AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            Invalidate();
        }
    }

    public TaskListToggleButton(TaskListToggleKind kind)
    {
        _kind = kind;
        AccessibleRole = AccessibleRole.CheckButton;
        AccessibleName = kind == TaskListToggleKind.Favorite
            ? "Favorite executable"
            : "Ignore executable";
        AccessibleDescription = GetAccessibleDescription();
    }

    protected override Color GetIconColor() =>
        !Enabled
            ? UiTheme.DisabledText
            : Active
                ? _kind == TaskListToggleKind.Favorite
                    ? UiTheme.Favorite
                    : UiTheme.Ignored
                : base.GetIconColor();

    protected override void DrawIcon(Graphics graphics, Color color)
    {
        if (_kind == TaskListToggleKind.Favorite)
            DrawStar(graphics, color);
        else
            DrawCross(graphics, color);
    }

    private void DrawStar(Graphics graphics, Color color)
    {
        float radius = Math.Min(
            UiScale.ToDevice(7f, DeviceDpi),
            Math.Min(ClientSize.Width, ClientSize.Height) / 2f
                - UiScale.ToDevice(3f, DeviceDpi));
        if (radius <= 0)
            return;

        float centerX = ClientSize.Width / 2f;
        float centerY = ClientSize.Height / 2f;
        var points = new PointF[10];
        for (int index = 0; index < points.Length; index++)
        {
            double angle = -Math.PI / 2 + index * Math.PI / 5;
            float pointRadius = index % 2 == 0 ? radius : radius * 0.44f;
            points[index] = new(
                centerX + pointRadius * (float)Math.Cos(angle),
                centerY + pointRadius * (float)Math.Sin(angle));
        }

        if (Active)
        {
            using var brush = new SolidBrush(color);
            graphics.FillPolygon(brush, points);
            return;
        }

        using var pen = new Pen(color, UiScale.ToDevice(1.4f, DeviceDpi))
        {
            LineJoin = LineJoin.Round
        };
        graphics.DrawPolygon(pen, points);
    }

    private void DrawCross(Graphics graphics, Color color)
    {
        float radius = Math.Min(
            UiScale.ToDevice(6f, DeviceDpi),
            Math.Min(ClientSize.Width, ClientSize.Height) / 2f
                - UiScale.ToDevice(4f, DeviceDpi));
        if (radius <= 0)
            return;

        float centerX = ClientSize.Width / 2f;
        float centerY = ClientSize.Height / 2f;
        float thickness = radius * 0.42f;
        PointF[] points =
        [
            new(centerX - radius, centerY - radius + thickness),
            new(centerX - radius + thickness, centerY - radius),
            new(centerX, centerY - thickness),
            new(centerX + radius - thickness, centerY - radius),
            new(centerX + radius, centerY - radius + thickness),
            new(centerX + thickness, centerY),
            new(centerX + radius, centerY + radius - thickness),
            new(centerX + radius - thickness, centerY + radius),
            new(centerX, centerY + thickness),
            new(centerX - radius + thickness, centerY + radius),
            new(centerX - radius, centerY + radius - thickness),
            new(centerX - thickness, centerY)
        ];

        if (Active)
        {
            using var brush = new SolidBrush(color);
            graphics.FillPolygon(brush, points);
            return;
        }

        using var pen = new Pen(color, UiScale.ToDevice(1.25f, DeviceDpi))
        {
            LineJoin = LineJoin.Round
        };
        graphics.DrawPolygon(pen, points);
    }

    private string GetAccessibleDescription() =>
        Active
            ? "Active. Press to remove this executable from the list."
            : "Inactive. Press to add this executable to the list.";

    protected override AccessibleObject CreateAccessibilityInstance() =>
        new ToggleAccessibleObject(this);

    private sealed class ToggleAccessibleObject(TaskListToggleButton owner)
        : ControlAccessibleObject(owner)
    {
        public override AccessibleStates State =>
            base.State
            | (owner.Active
                ? AccessibleStates.Checked
                : AccessibleStates.None);
    }
}
