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

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

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
