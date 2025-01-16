using System;
using System.Collections.Generic;
using System.Text;

namespace taskTrough
{
    /// <summary>
    /// Provides higher-level operations for enumerating windows and toggling
    /// extended styles (click-through, topmost) or opacity.
    /// </summary>
    internal static class WindowManager
    {
        /// <summary>
        /// Simple data object describing a window's handle and title.
        /// </summary>
        internal sealed class WindowData
        {
            /// <summary>
            /// The window handle.
            /// </summary>
            public IntPtr Handle { get; set; }

            /// <summary>
            /// The window's title. We initialize to empty to avoid null warnings.
            /// </summary>
            public string Title { get; set; } = string.Empty;
        }

        /// <summary>
        /// Enumerates all top-level windows on the system,
        /// returning only visible windows with a non-empty title.
        /// </summary>
        internal static List<WindowData> EnumerateWindows()
        {
            var results = new List<WindowData>();

            NativeMethods.EnumWindows((hWnd, lParam) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd))
                    return true;

                int length = NativeMethods.GetWindowTextLength(hWnd);
                if (length == 0)
                    return true;

                var sb = new StringBuilder(length + 1);
                NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();

                results.Add(new WindowData
                {
                    Handle = hWnd,
                    Title = title
                });

                return true;
            }, IntPtr.Zero);

            return results;
        }

        /// <summary>
        /// Enables or disables click-through mode for the given window.
        /// </summary>
        internal static void ToggleClickThrough(IntPtr hWnd, bool enable)
        {
            int style = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
            if (enable)
            {
                style |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT;
            }
            else
            {
                style &= ~NativeMethods.WS_EX_TRANSPARENT;
            }
            NativeMethods.SetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE, style);
        }

        /// <summary>
        /// Enables or disables topmost status for the given window.
        /// </summary>
        internal static void ToggleTopMost(IntPtr hWnd, bool enable)
        {
            NativeMethods.SetWindowPos(
                hWnd,
                enable ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW
            );
        }

        /// <summary>
        /// Sets the opacity (0..100) of the given window using a layered window attribute.
        /// </summary>
        internal static void SetWindowOpacity(IntPtr hWnd, int opacityValue)
        {
            int style = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
            if ((style & NativeMethods.WS_EX_LAYERED) == 0)
            {
                NativeMethods.SetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_LAYERED);
            }

            byte alpha = (byte)(opacityValue * 255 / 100);

            NativeMethods.SetLayeredWindowAttributes(hWnd, 0, alpha, NativeMethods.LWA_ALPHA);
        }
    }
}
