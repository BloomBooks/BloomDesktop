using System;
using System.Runtime.InteropServices;

namespace Bloom.Utils
{
    internal class DpiUtils
    {
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("Shcore.dll")]
        private static extern int GetScaleFactorForMonitor(IntPtr hMon, out uint pScale);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        /// <summary>
        /// Returns the DPI for a specific window when supported by this Windows version.
        /// </summary>
        public static uint? TryGetWindowDpi(IntPtr handle)
        {
            try
            {
                return GetDpiForWindow(handle);
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the system DPI when supported by this Windows version.
        /// </summary>
        public static uint? TryGetSystemDpi()
        {
            try
            {
                return GetDpiForSystem();
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
        }

        /// <summary>
        /// Returns monitor scale percentage (for example, 100, 125, 150) for the monitor
        /// containing the specified window, when supported by this Windows version.
        /// </summary>
        public static uint? TryGetWindowScalePercent(IntPtr handle)
        {
            try
            {
                var monitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero)
                    return null;

                return GetScaleFactorForMonitor(monitor, out var scaleFactor) == 0
                    ? scaleFactor
                    : null;
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
            catch (DllNotFoundException)
            {
                return null;
            }
        }
    }
}
