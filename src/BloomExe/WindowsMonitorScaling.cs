using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Bloom
{
    /// <summary>
    /// This class provides access to the Windows API for finding where a control is on screen in physical
    /// (device) pixels, which is what Graphics.CopyFromScreen and PrintWindow work in. Bloom uses it to take
    /// Problem Report screenshots.
    ///
    /// Before Bloom became PerMonitorV2 DPI aware (see Program.cs), Control.Bounds came back in logical
    /// pixels, so we used to multiply them by the monitor's scale factor. Under PerMonitorV2 they are
    /// already physical, and that multiplication inflated the screenshot on high-density monitors
    /// (BL-16495). We can't simply trust Bounds either: while a dialog shown by LegacyDpiDialogLauncher is
    /// open, the UI thread runs SYSTEM_AWARE and coordinates are virtualized again. Asking Windows for the
    /// window rectangle under an explicit PerMonitorV2 thread context gives physical pixels either way.
    /// </summary>
    internal static class WindowsMonitorScaling
    {
        // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        private static readonly IntPtr PerMonitorAwareV2DpiContext = new IntPtr(-4);

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private sealed class DpiAwarenessScope : IDisposable
        {
            private readonly IntPtr _previousContext;

            public DpiAwarenessScope(IntPtr previousContext)
            {
                _previousContext = previousContext;
            }

            public void Dispose()
            {
                SetThreadDpiAwarenessContext(_previousContext);
            }
        }

        /// <summary>
        /// Switch the current thread to PerMonitorV2 DPI awareness until the returned object is disposed,
        /// so that window coordinates and screen copies are all in physical pixels.
        /// </summary>
        internal static IDisposable EnterPerMonitorV2Scope()
        {
            var previousContext = SetThreadDpiAwarenessContext(PerMonitorAwareV2DpiContext);
            return new DpiAwarenessScope(previousContext);
        }

        /// <summary>
        /// The rectangle the control occupies on screen, in screen coordinates. Call this inside
        /// EnterPerMonitorV2Scope so that the result is in physical pixels whatever DPI awareness the
        /// control's window was created with.
        /// </summary>
        internal static Rectangle GetScreenRectangleInPhysicalPixels(Control control)
        {
            if (!GetWindowRect(control.Handle, out RECT rect))
                throw new ApplicationException(
                    "GetWindowRect failed with Win32 error " + Marshal.GetLastWin32Error()
                );
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }
    }
}
