using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Bloom.Utils
{
    internal static class LegacyDpiDialogLauncher
    {
        // SYSTEM_AWARE more closely matches Bloom's pre-PerMonitorV2 behavior.
        // Using UNAWARE can cause apparent double-scaling when the primary monitor is scaled.
        private static readonly IntPtr LegacyDialogDpiContext = new IntPtr(-2);

        // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2: what the rest of Bloom uses, because
        // Program.Main calls Application.SetHighDpiMode(HighDpiMode.PerMonitorV2).
        private static readonly IntPtr PerMonitorAwareV2DpiContext = new IntPtr(-4);

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);

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
        /// Enter a temporary legacy thread DPI context. Create and show dialogs inside this scope.
        /// </summary>
        public static IDisposable EnterLegacyDpiScope()
        {
            var previousContext = SetThreadDpiAwarenessContext(LegacyDialogDpiContext);
            return new DpiAwarenessScope(previousContext);
        }

        /// <summary>
        /// True if the given window was created with a DPI awareness other than the PerMonitorV2 the rest
        /// of Bloom uses, i.e. it (or an ancestor) was created inside EnterLegacyDpiScope. A window's DPI
        /// awareness is fixed when it is created, so this stays true for the window's whole life, however
        /// the thread's context may have changed since.
        ///
        /// Callers use this to detect the mixed-DPI-awareness state that some Windows components handle
        /// badly; see WebView2Browser.CorrectTruncatedWebView2HostWindow (BL-16876).
        ///
        /// Returns false for a null handle, and on any platform where these APIs are missing, so callers
        /// treat "we cannot tell" as "nothing unusual".
        /// </summary>
        public static bool IsWindowLegacyDpiAware(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return false;
            try
            {
                var context = GetWindowDpiAwarenessContext(hWnd);
                if (context == IntPtr.Zero)
                    return false;
                return !AreDpiAwarenessContextsEqual(context, PerMonitorAwareV2DpiContext);
            }
            catch (EntryPointNotFoundException)
            {
                return false; // pre-1607 Windows; Bloom is not mixing awareness there either
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        /// <summary>
        /// Show a Form under a temporary legacy DPI context so it behaves like pre-PerMonitorV2 dialogs.
        /// </summary>
        public static DialogResult ShowDialog(Form dialog, IWin32Window owner = null)
        {
            using (EnterLegacyDpiScope())
            {
                return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            }
        }

        /// <summary>
        /// Show a CommonDialog under a temporary legacy DPI context.
        /// </summary>
        public static DialogResult ShowDialog(CommonDialog dialog, IWin32Window owner = null)
        {
            using (EnterLegacyDpiScope())
            {
                return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            }
        }
    }
}
