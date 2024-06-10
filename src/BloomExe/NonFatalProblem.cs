using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using Bloom.MiscUI;
using Bloom.web.controllers;
using DesktopAnalytics;
using Sentry;
using SIL.Reporting;
using SIL.Windows.Forms.Progress;

namespace Bloom
{
    // NB: these must have the exactly the same symbols
    public enum ModalIf
    {
        None,
        Alpha,
        Beta,
        All
    }

    public enum PassiveIf
    {
        None,
        Alpha,
        Beta,
        All
    }

    /// <summary>
    /// Provides a way to note a problem in the log and, depending on channel, notify the user.
    /// </summary>
    public class NonFatalProblem
    {
        /// <summary>
        /// Always log, possibly inform the user, possibly throw the exception
        /// </summary>
        /// <param name="modalThreshold">Will show a modal dialog if the channel is this or lower</param>
        /// <param name="passiveThreshold">Will toast if channel is this or lower (and didn't modal) and shortUserLevelMessage is defined.</param>
        /// <param name="shortUserLevelMessage">Simple message that fits in small toast notification</param>
        /// <param name="moreDetails">Info adds information about the problem, which we get if they report the problem</param>
        /// <param name="exception"></param>
        /// <param name="showSendReport">Set to 'false' to eliminate yellow screens and "Report" links on toasts</param>
        public static void Report(
            ModalIf modalThreshold,
            PassiveIf passiveThreshold,
            string shortUserLevelMessage = null,
            string moreDetails = null,
            Exception exception = null,
            bool showSendReport = true,
            bool isShortMessagePreEncoded = false,
            bool skipSentryReport = false,
            bool showRequestDetails = false,
            string[] additionalFilesToInclude = null
        )
        {
            var originalException = exception;
            s_expectedByUnitTest?.ProblemWasReported();

            var channel = ApplicationUpdateSupport.ChannelName.ToLowerInvariant();

            try
            {
                shortUserLevelMessage = shortUserLevelMessage ?? "";
                var fullDetailedMessage = shortUserLevelMessage;
                if (!string.IsNullOrEmpty(moreDetails))
                    fullDetailedMessage =
                        fullDetailedMessage + System.Environment.NewLine + moreDetails;

                if (exception == null)
                {
                    //the code below is simpler if we always have an exception, even this thing that gives
                    //us the stacktrace we would otherwise be missing. Note, you might be tempted to throw
                    //and then catch an exception instead, but for some reason the resulting stack trace
                    //would contain only this method.
                    exception = new ApplicationException(new StackTrace().ToString());
                }

                if (Program.RunningUnitTests)
                {
                    //It's not clear to me what we can do that works for all unit test scenarios...
                    //We can imagine those for which throwing an exception at this point would be helpful,
                    //but there are others in which say, not finding a file is expected. Either way,
                    //the rest of the test should fail if the problem is real, so doing anything here
                    //would just be a help, not really necessary for getting the test to fail.
                    //So, for now I'm going to just go with doing nothing in general.
                    // We'll save a little information so we can write specific "this does not report a problem"
                    // tests.
                    LastNonFatalProblemReported = fullDetailedMessage;
                    return;
                }

                if (!skipSentryReport)
                {
                    if (originalException != null)
                        ReportSentryOnly(originalException, fullDetailedMessage);
                    else
                        ReportSentryOnly(shortUserLevelMessage, fullDetailedMessage);
                }

                //if this isn't going modal even for devs, it's just background noise and we don't want the
                //thousands of exceptions we were getting as with BL-3280
                if (modalThreshold != ModalIf.None)
                {
                    Analytics.ReportException(exception);
                }

                Logger.WriteError("NonFatalProblem: " + fullDetailedMessage, exception);

                if (Program.RunningInConsoleMode)
                {
                    // This is "nonfatal", so report as best we can (standard error) and keep going...
                    Console.Error.WriteLine($"Nonfatal problem: {fullDetailedMessage}");
                    return;
                }

                //just convert from PassiveIf to ModalIf so that we don't have to duplicate code
                var passive = (ModalIf)ModalIf.Parse(typeof(ModalIf), passiveThreshold.ToString());
                var formForSynchronizing = Shell.GetShellOrOtherOpenForm();
                if (formForSynchronizing is ProgressDialog)
                {
                    // Targetting ProgressDialog doesn't work so well for toasts, since the dialog tends
                    // to disappear immediately and the user never sees the toast.
                    modalThreshold = passive;
                }

                if (Matches(modalThreshold).Any(s => channel.Contains(s)))
                {
                    try
                    {
                        if (showSendReport)
                        {
                            // N.B.: We should be more careful than ever about when we want 'showSendReport' to be 'true',
                            // since this new "nonfatal" UI doesn't have a "Cancel" button.
                            ProblemReportApi.ShowProblemDialog(
                                Form.ActiveForm,
                                exception,
                                fullDetailedMessage,
                                "nonfatal",
                                shortUserLevelMessage,
                                isShortMessagePreEncoded,
                                additionalFilesToInclude
                            );
                        }
                        else
                        {
                            ShowProblemInMessageBox(fullDetailedMessage, formForSynchronizing);
                        }
                    }
                    catch (Exception ex)
                    {
                        Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(ex);
                        //if we're running when the UI is already shut down, the above is going to throw.
                        //At least if we're running in a debugger, we'll stop here:
                        throw new ApplicationException(
                            fullDetailedMessage + "Error trying to report normally."
                        );
                    }
                    return;
                }

                if (
                    !string.IsNullOrEmpty(shortUserLevelMessage)
                    && Matches(passive).Any(s => channel.Contains(s))
                )
                {
                    ShowToast(
                        shortUserLevelMessage,
                        exception,
                        fullDetailedMessage,
                        showSendReport,
                        showRequestDetails
                    );
                }
            }
            catch (Exception errorWhileReporting)
            {
                // Don't annoy developers for expected error if the internet is not available.
                if (
                    errorWhileReporting.Message.StartsWith("Bloom could not retrieve the URL")
                    && Bloom.web.UrlLookup.FastInternetAvailable
                )
                {
                    Debug.Fail("error in nonfatalError reporting");
                }
                if (channel.Contains("developer") || channel.Contains("alpha"))
                    ErrorReport.NotifyUserOfProblem(
                        errorWhileReporting,
                        "Error while reporting non fatal error"
                    );
            }
        }

        private static void ShowProblemInMessageBox(string fullDetailedMessage, Form formForSynchronizing)
        {
            if (formForSynchronizing == null)
                return; // could be on wrong thread

            if (formForSynchronizing.InvokeRequired)
            {
                formForSynchronizing.BeginInvoke(
                    new Action(() =>
                    {
                        ShowProblemInMessageBox(fullDetailedMessage, formForSynchronizing);
                    })
                );
                return;
            }
            // We don't want any notification (MessageBox or toast) targetting a ProgressDialog,
            // since the dialog seems to disappear quickly and leave us hanging... and not able to show.
            // We'll keep the form if it's not a ProgressDialog in order to center our message properly.
            if (formForSynchronizing is ProgressDialog)
            {
                MessageBox.Show(
                    fullDetailedMessage,
                    string.Empty,
                    MessageBoxButtons.OK
                );
            }
            else
            {
                MessageBox.Show(
                    formForSynchronizing,
                    fullDetailedMessage,
                    string.Empty,
                    MessageBoxButtons.OK
                );
            }
        }

        /// <summary>
        /// Sends a report to Sentry only (no log, no toast, etc.).
        /// Does not send to Sentry if ApplicationUpdateSupport.IsDev is true.
        /// Fails silently (unless throwOnException is true).
        /// </summary>
        /// <param name="exception">The exception to report to Sentry</param>
        /// <param name="message">An optional message to send with the exception to provide more context</param>
        /// <param name="throwOnException">If true, will rethrow any exception which occurs while reporting to Sentry.</param>
        /// <remarks>Note, some previous Sentry reports were adding the message as a fullDetailedMessage tag, but when we refactored
        /// to create this method, we decided to standardize on the more versatile breadcrumbs approach.</remarks>
        public static void ReportSentryOnly(
            Exception exception,
            string message = null,
            bool throwOnException = false
        )
        {
            if (ApplicationUpdateSupport.IsDev)
            {
                Debug.WriteLine(
                    "Developer, we though you might want to know that ReportSentryOnly() was called. Ignore if you like."
                );
                Debug.Indent();
                Debug.WriteLine(exception);
                Debug.Unindent();
                return;
            }
            try
            {
                if (!string.IsNullOrWhiteSpace(message))
                    SentrySdk.AddBreadcrumb(message);
                SentrySdk.CaptureException(exception);
            }
            catch (Exception err)
            {
                // will only "do something" if we're testing reporting and have thus turned off checking for dev
                Debug.Fail(err.Message);

                if (throwOnException)
                    throw;
            }
        }

        /// <summary>
        /// Sends a report to Sentry only (no log, no toast, etc.).
        /// Does not send to Sentry if ApplicationUpdateSupport.IsDev is true.
        /// Fails silently.
        /// </summary>
        /// <param name="message">The message to send with the report</param>
        /// <param name="fullDetailedMessage">An optional message which can be added to the Sentry report</param>
        public static void ReportSentryOnly(string message, string fullDetailedMessage = null)
        {
            if (ApplicationUpdateSupport.IsDev)
            {
                Debug.WriteLine(
                    "Developer, we though you might want to know that ReportSentryOnly() was called. Ignore if you like."
                );
                Debug.Indent();
                Debug.WriteLine(message);
                Debug.WriteLine(fullDetailedMessage);
                Debug.Unindent();
                return;
            }
            try
            {
                var evt = new SentryEvent { Message = message };
                if (!string.IsNullOrWhiteSpace(fullDetailedMessage))
                    evt.SetExtra("fullDetailedMessage", fullDetailedMessage);
                evt.SetExtra("stackTrace", new StackTrace().ToString());
                SentrySdk.CaptureEvent(evt);
            }
            catch (Exception err)
            {
                // will only "do something" if we're testing reporting and have thus turned off checking for dev
                Debug.Fail(err.Message);
            }
        }

        private static void ShowToast(
            string shortUserLevelMessage,
            Exception exception,
            string fullDetailedMessage,
            bool showSendReport = true,
            bool showDetailsOnRequest = false
        )
        {
            // The form is used for the screen shot as well as for synchronizing, so get the shell if possible.
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-8348.
            var formForSynchronizing = Shell.GetShellOrOtherOpenForm();
            if (formForSynchronizing == null)
                return; // can't safely show a toast, may be on wrong thread.

            if (formForSynchronizing.InvokeRequired)
            {
                formForSynchronizing.BeginInvoke(
                    new Action(() =>
                    {
                        ShowToast(
                            shortUserLevelMessage,
                            exception,
                            fullDetailedMessage,
                            showSendReport
                        );
                    })
                );
                return;
            }
            var toast = new ToastNotifier();
            var callToAction = string.Empty;
            if (showSendReport)
            {
                toast.ToastClicked += (s, e) =>
                {
                    ProblemReportApi.ShowProblemDialog(
                        formForSynchronizing,
                        exception,
                        fullDetailedMessage,
                        "nonfatal",
                        shortUserLevelMessage
                    );
                };
                callToAction = "Report";
            }
            else if (showDetailsOnRequest)
            {
                toast.ToastClicked += (s, e) =>
                {
                    ErrorReport.NotifyUserOfProblem(
                        new ShowAlwaysPolicy(),
                        null,
                        default(ErrorResult),
                        "{0}",
                        string.Join(
                            Environment.NewLine, // handle Linux newlines on Windows (and vice-versa)
                            fullDetailedMessage.Split(
                                new[] { '\r', '\n' },
                                StringSplitOptions.RemoveEmptyEntries
                            )
                        )
                    );
                };
                callToAction = "Details";
            }
            toast.Image.Image = ToastNotifier.WarningBitmap;
            toast.Show(shortUserLevelMessage, callToAction, 15);
        }

        private static IEnumerable<string> Matches(ModalIf threshold)
        {
            switch (threshold)
            {
                case ModalIf.All:
                    return new string[]
                    {
                        "" /*will match anything*/
                    };
                case ModalIf.Beta:
                    return new string[] { "developer", "alpha", "beta" };
                case ModalIf.Alpha:
                    return new string[] { "developer", "alpha" };
                default:
                    return new string[] { };
            }
        }

        private static ExpectedByUnitTest s_expectedByUnitTest = null;
        public static string LastNonFatalProblemReported = null; // just for unit tests

        /// <summary>
        /// use this in unit tests to cleanly check that a message would have been shown.
        /// E.g.  using (new NonFatalProblem.ExpectedByUnitTest()) {...}
        /// </summary>
        public class ExpectedByUnitTest : IDisposable
        {
            private bool _reported;

            public ExpectedByUnitTest()
            {
                s_expectedByUnitTest?.Dispose();
                s_expectedByUnitTest = this;
            }

            internal void ProblemWasReported()
            {
                _reported = true;
            }

            public void Dispose()
            {
                s_expectedByUnitTest = null;
                if (!_reported)
                    throw new Exception("NonFatalProblem was expected but wasn't generated.");
            }
        }
    }
}
