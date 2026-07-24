using System;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using Bloom.web.controllers;
using SIL.Reporting;
using SIL.Windows.Forms.Reporting;

namespace Bloom
{
    internal class FatalExceptionHandler : ExceptionHandler
    {
        private WinFormsExceptionHandler _fallbackHandler;

        internal static Control ControlOnUIThread { get; private set; }

        internal static bool InvokeRequired
        {
            get { return !ControlOnUIThread.IsDisposed && ControlOnUIThread.InvokeRequired; }
        }

        /// <summary>
        /// Initially true for setup, WorkspaceView sets this to false when BloomServer is up and listening
        /// and the webcontroller api has registered the ProblemReportApi.
        /// </summary>
        internal static bool UseFallback;

        /// ------------------------------------------------------------------------------------
        /// <summary>
        /// Set exception handler. Needs to be done before we create splash screen (don't
        /// understand why, but otherwise some exceptions don't get caught).
        /// </summary>
        /// ------------------------------------------------------------------------------------
        public FatalExceptionHandler()
        {
            // We need to create a WinFormsExceptionHandler so that if we ever need to use the fallback
            // SIL.Reporting.ErrorReport system it has a valid ControlOnUIThread prop.
            // Creating the object is enough to solve this problem. We keep a reference so it gets collected
            // when we go away.
            // Passing false keeps the WinForms handler from responding to exceptions, so we don't get two
            // handlers vying for who gets to report an error.
            _fallbackHandler = new WinFormsExceptionHandler(false);
            UseFallback = true;

            // We need to create a control on the UI thread so that we have a control that we
            // can use to invoke the error reporting dialog on the correct thread.
            ControlOnUIThread = new Control();
            ControlOnUIThread.CreateControl();

            // Using Application.ThreadException rather than
            // AppDomain.CurrentDomain.UnhandledException has the advantage that the
            // program doesn't necessarily end - we can ignore the exception and continue.
            Application.ThreadException += HandleTopLevelError;

            // We also want to catch the UnhandledExceptions for all the cases that
            // ThreadException don't catch, e.g. in the startup.
            AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        }

        /// ------------------------------------------------------------------------------------
        /// <summary>
        /// Catches and displays a otherwise unhandled exception.
        /// </summary>
        /// <param name="sender">sender</param>
        /// <param name="e">Exception</param>
        /// <remarks>previously <c>AfApp::HandleTopLevelError</c></remarks>
        /// ------------------------------------------------------------------------------------
        protected void HandleTopLevelError(object sender, ThreadExceptionEventArgs e)
        {
            if (IsHarmlessInputLanguageCultureException(e.Exception))
                return;

            if (!GetShouldHandleException(sender, e.Exception))
                return;

            if (UseFallback)
            {
                if (Program.RunningHarvesterMode)
                    Console.WriteLine("Uncaught Exception: {0}", e.Exception);
                else
                    _fallbackHandler.HandleTopLevelError(sender, e);
                return;
            }

            if (DisplayError(e.Exception))
            {
                //Are we inside a Application.Run() statement?
                if (Application.MessageLoop)
                    ProgramExit.Exit();
                else
                    Environment.Exit(1); //the 1 here is just non-zero
            }
        }

        /// ------------------------------------------------------------------------------------
        /// <summary>
        /// Catches and displays otherwise unhandled exception, especially those that happen
        /// during startup of the application before we show our main window.
        /// </summary>
        /// ------------------------------------------------------------------------------------
        protected new void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // Note: we do NOT try to suppress the harmless input-language CultureNotFoundException
            // here (see IsHarmlessInputLanguageCultureException / HandleTopLevelError). That crash
            // arrives on the UI thread's WndProc and is delivered to Application.ThreadException,
            // which lets us return and carry on. AppDomain.UnhandledException, by contrast, fires
            // when the CLR is already terminating; returning early cannot prevent the exit, so
            // swallowing it here would only hide the crash report without saving the process.
            if (!GetShouldHandleException(sender, e.ExceptionObject as Exception))
                return;

            if (UseFallback)
            {
                if (Program.RunningHarvesterMode)
                    Console.WriteLine("Uncaught Exception: {0}", e.ExceptionObject);
                else
                    _fallbackHandler.HandleUnhandledException(sender, e);
                return;
            }

            if (e.ExceptionObject is Exception)
                DisplayError(e.ExceptionObject as Exception);
            else
                DisplayError(new ApplicationException("Got unknown exception"));
        }

        /// <summary>
        /// Returns true for the harmless CultureNotFoundException that WinForms throws while
        /// processing WM_INPUTLANGCHANGE when the user switches to a keyboard whose input language
        /// reports a BCP-47 tag that .NET cannot turn into a CultureInfo (e.g. the Keyman IPA
        /// keyboard's "und-Latn"). See BL-16536.
        ///
        /// BloomWebView2 already swallows this when the message reaches the WebView2 control itself,
        /// but Windows delivers WM_INPUTLANGCHANGE to whichever window has focus, so the same
        /// exception can surface from any of our WinForms controls (the stack shows a bare
        /// UserControl.WndProc). Rather than subclass every control, we catch it here, at the
        /// thread-exception level, so it can never take Bloom down. Bloom does all its text editing
        /// inside WebView2, which tracks its own input language independently of WinForms, so there
        /// is nothing to lose by ignoring WinForms' inability to represent the keyboard.
        /// </summary>
        private static bool IsHarmlessInputLanguageCultureException(Exception exception)
        {
            // The distinctive marker is a CultureNotFoundException whose stack runs through the
            // WM_INPUTLANGCHANGE handling (WmInputLangChange / InputLanguageChangedEventArgs).
            if (!(exception is CultureNotFoundException))
                return false;
            var stack = exception.StackTrace;
            if (stack == null || !stack.Contains("InputLang"))
                return false;

            Logger.WriteMinorEvent(
                "Ignoring unsupported input-language culture from keyboard (BL-16536): "
                    + exception.Message
            );
            return true;
        }

        protected override bool ShowUI
        {
            get { return false; }
        }

        protected override bool DisplayError(Exception exception)
        {
            if (Program.RunningE2eTests)
            {
                // No human is present during an e2e/visual-regression run to dismiss a modal, so
                // showing the fatal-error dialog would hang the whole run. Log it instead and return
                // true so the caller exits normally; the dead Bloom will fail the test.
                Logger.WriteError("Fatal error during e2e run (dialog suppressed)", exception);
                return true;
            }
            ProblemReportApi.ShowProblemDialog(Form.ActiveForm, exception, "", "fatal");
            return true;
        }
    }
}
