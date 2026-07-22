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
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("Shcore.dll")]
        private static extern int GetScaleFactorForMonitor(IntPtr hmonitor, out int pScale);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

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

        // Bloom uses this to scale images stored in spreadsheets during spreadsheet export to fit column width.
        // The third-party library that does the spreadsheet export apparently uses the primary monitor's scaling factor.
        // Returns 100 (i.e. no scaling) if the scale factor can't be determined for any reason, so that spreadsheet
        // export still works — falling back to unscaled images — even when this Windows API is unavailable or fails.
        // PublishAudioVideoAPI.TryGetWindowScalePercent guards the same GetScaleFactorForMonitor call the same way.
        internal static int GetScalingFactorForPrimaryMonitor()
        {
            try
            {
                var ptZero = new POINT { X = 0, Y = 0 };
                var hmonitor = MonitorFromPoint(ptZero, MONITOR_DEFAULTTOPRIMARY);
                if (hmonitor == IntPtr.Zero)
                    return 100;
                // GetScaleFactorForMonitor returns S_OK (0) on success; otherwise percentScaleFactor is unreliable.
                if (GetScaleFactorForMonitor(hmonitor, out int percentScaleFactor) != 0 || percentScaleFactor <= 0)
                    return 100;
                return percentScaleFactor;
            }
            catch (EntryPointNotFoundException)
            {
                return 100;
            }
            catch (DllNotFoundException)
            {
                return 100;
            }
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
