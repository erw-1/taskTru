using System.Runtime.InteropServices;
using static taskTru.NativeMethods;

namespace taskTru;

internal static class WindowGeometry
{
    internal static bool TryGetFrameBounds(nint handle, out Rectangle bounds)
    {
        bool succeeded = DwmGetWindowAttribute(
                handle,
                DwmExtendedFrameBounds,
                out NativeRect rectangle,
                Marshal.SizeOf<NativeRect>()) == 0
            || GetWindowRect(handle, out rectangle);

        bounds = succeeded
            ? Rectangle.FromLTRB(
                rectangle.Left,
                rectangle.Top,
                rectangle.Right,
                rectangle.Bottom)
            : Rectangle.Empty;

        return succeeded && bounds.Width > 0 && bounds.Height > 0;
    }
}
