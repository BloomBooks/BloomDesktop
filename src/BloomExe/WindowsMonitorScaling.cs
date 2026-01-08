using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Bloom
{
    /// <summary>
    /// This class provides access to the Windows API for getting the scaling factor of the monitor that a control is on.
    /// </summary>
    internal static class WindowsMonitorScaling
    {
        [DllImport("Shcore.dll")]
        private static extern int GetScaleFactorForMonitor(IntPtr hmonitor, out int pScale);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        [DllImport("Shcore.dll")]
        private static extern int GetDpiForMonitor(
            IntPtr hmonitor,
            Monitor_DPI_Type dpiType,
            out uint dpiX,
            out uint dpiY
        );

        public enum Monitor_DPI_Type
        {
            MDT_Effective_DPI = 0,
            MDT_Angular_DPI = 1,
            MDT_Raw_DPI = 2,
            MDT_Default = MDT_Effective_DPI,
        }

        internal static int GetMonitorScalingFactorFromControl(Control visibleControl)
        {
            if (visibleControl == null || !visibleControl.Visible)
                return 100;
            IntPtr hwnd = visibleControl.Handle;
            // This MONITOR_DEFAULTTONEAREST flag says to use the monitor that the window is on, not the primary monitor.
            // It actually works quite well, so that it uses the monitor that Bloom is on, even if it's not the primary monitor.
            IntPtr hmonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            GetDpiForMonitor(
                hmonitor,
                Monitor_DPI_Type.MDT_Effective_DPI,
                out uint dpiX,
                out uint dpiY
            );
            GetScaleFactorForMonitor(hmonitor, out int percentScaleFactor);
            return percentScaleFactor;
        }

        // Bloom uses this to scale a Problem Report screenshot if the display resolution is > 100%.
        internal static Rectangle GetRectangleFromControlScaledToMonitorResolution(Control control)
        {
            int scalingFactorPercent = GetMonitorScalingFactorFromControl(control);
            var unscaledBounds = control.Bounds;
            return new Rectangle(
                unscaledBounds.X * scalingFactorPercent / 100,
                unscaledBounds.Y * scalingFactorPercent / 100,
                unscaledBounds.Width * scalingFactorPercent / 100,
                unscaledBounds.Height * scalingFactorPercent / 100
            );
        }
    }
}
