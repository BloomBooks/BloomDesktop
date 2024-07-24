using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.MiscUI;
using Bloom.ToPalaso;
using Bloom.Utils;
using Bloom.web.controllers;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.Reporting;

namespace Bloom.ErrorReporter
{
    /// <summary>
    /// An Error Reporter designed to be used with libpalaso's ErrorReport.
    /// Unlike WinFormsErrorReporter, which uses WinForms to display the UI, this utilizes a browser to display the UI
    /// </summary>
    public class HtmlErrorReporter : IErrorReporter, IBloomErrorReporter
    {
        private HtmlErrorReporter()
        {
            ResetToDefaults();
            DefaultReportLabel = "Report";
        }

        private static HtmlErrorReporter _instance;
        public static HtmlErrorReporter Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new HtmlErrorReporter();
                }
                return _instance;
            }
        }

        internal string DefaultReportLabel { get; private set; }

        /// <summary>
        /// Set the label for the "Report" button to the localized value.
        /// </summary>
        /// <remarks>
        /// This call needs to wait until localization has been set up.  See BL-13245.
        /// </remarks>
        internal void LocalizeDefaultReportLabel()
        {
            DefaultReportLabel = L10NSharp.LocalizationManager.GetString(
                "ErrorReportDialog.Report",
                "Report"
            );
        }

        static object _lock = new object();

        #region Dependencies exposed for unit tests to mock
        internal IReactDialogFactory BrowserDialogFactory = new ReactDialogFactory();

        internal Control Control { get; set; }

        private IBloomServer _bloomServer = null;
        internal IBloomServer BloomServer
        {
            // This property allows the unit tests to set the Bloom Server to a mocked value.
            // However, if it hasn't been set at the time the value is read, then it lazily sets it
            // to the default singleton instance.
            // We can't do the simple/eager instantiation at construction time of this object
            // because the Bloom Server is still null when this object is constructed.
            get
            {
                if (_bloomServer == null)
                    _bloomServer = Api.BloomServer._theOneInstance;

                return _bloomServer;
            }
            set { _bloomServer = value; }
        }
        #endregion

        private void ResetToDefaults()
        {
            Control = null;
        }

        #region IBloomErrorReporter interface
        /// <summary>
        /// Notifies the user of a problem, using a browser-based dialog.
        /// Note: This is a legacy method designed to be called by LibPalaso's ErrorReport class.
        /// </summary>
        /// <remarks>Implements legacy IErrorReporter method</remarks>
        /// <param name="policy">Checks if we should notify the user, based on the contents of {message}</param>
        /// <param name="alternateButton1Label">The text that goes on the alternate button (aka "Details" in WinFormsErrorReporter or "Report" in Bloom's HtmlErrorReporter).
        /// If "Details" (the old default) is passed in, it will be converted to "Report" (the new default).
        /// To explicitly specify "Details", please use the new API instead. (See <see cref="NotifyUserOfProblem(IRepeatNoticePolicy, Exception, string)"/>)</param>
        /// <param name="resultIfAlternateButtonClicked">This is the value that this method should return so that the caller (mainly LibPalaso ErrorReport)
        /// can know if the alternate button was clicked, and if so, invoke whatever actions are desired.</param>
        /// <param name="message">The message to show to the user</param>
        /// <returns>If closed normally, returns ErrorResult.OK.
        /// If the report button was clicked, returns the parameter <paramref name="resultIfAlternateButtonClicked"/>.
        /// </returns>
        [Obsolete(
            "Please use the simpler overload NotifyUserOfProblem(policy, exception, message) instead"
        )]
        public ErrorResult NotifyUserOfProblem(
            IRepeatNoticePolicy policy,
            string alternateButton1Label,
            ErrorResult resultIfAlternateButtonClicked,
            string message
        )
        {
            var returnResult = ErrorResult.OK;
            if (alternateButton1Label == "Details")
                alternateButton1Label = DefaultReportLabel;

            Action<string, Exception> onAlternateButtonClicked = (messageParam, exceptionParam) =>
            {
                returnResult = resultIfAlternateButtonClicked;
            };

            var control = GetControlToUse();
            var forceSynchronous = true;
            SafeInvoke.InvokeIfPossible(
                "Show Error Reporter",
                control,
                forceSynchronous,
                () =>
                {
                    NotifyUserOfProblemInternal(
                        message,
                        (Exception)null,
                        policy,
                        alternateButton1Label,
                        onAlternateButtonClicked
                    );
                }
            );
            return returnResult;
        }

        /// <summary>
        /// Notifies the user of a problem, using a browser-based dialog.
        /// </summary>
        /// <remarks>Implements IErrorReporter method</remarks>
        public void NotifyUserOfProblem(
            IRepeatNoticePolicy policy,
            Exception exception,
            string message
        ) =>
            NotifyUserOfProblem(
                message,
                exception,
                new NotifyUserOfProblemSettings(AllowSendReport.AllowIfException),
                policy
            );

        /// <summary>
        /// Notifies the user of a problem, using a browser-based dialog.
        /// Allows customization of whether the "Report" button appears as well as an optional extra button.
        /// </summary>
        /// <remarks>Implements our own IBloomErrorReporter method</remarks>
        /// <param name="message">The message to show the user</param>
        /// <param name="settings">The customization settings controlling which/how extra buttons show up</param>
        /// <param name="policy">The policy to use to decide whether to show the notification.</param>
        public void NotifyUserOfProblem(
            string message,
            NotifyUserOfProblemSettings settings,
            IRepeatNoticePolicy policy
        ) => NotifyUserOfProblem(message, null, settings, policy);

        /// <summary>
        /// Notifies the user of a problem, using a browser-based dialog.
        /// Allows customization of whether the "Report" button appears as well as an optional extra button.
        /// </summary>
        /// <remarks>Implements our own IBloomErrorReporter method</remarks>
        /// <param name="message">The message to show the user</param>
        /// <param name="exception">Any accompanying exception</param>
        /// <param name="settings">The customization settings controlling which/how extra buttons show up</param>
        /// <param name="policy">The policy to use to decide whether to show the notification.</param>
        public void NotifyUserOfProblem(
            string message,
            Exception exception,
            NotifyUserOfProblemSettings settings,
            IRepeatNoticePolicy policy
        )
        {
            bool shouldShowReportButton = settings.AllowSendReport.IsSendReportAllowed(
                new AllowSendReportContext(exception)
            );
            string reportButtonLabel = shouldShowReportButton ? DefaultReportLabel : "";
            NotifyUserOfProblemInternal(
                message,
                exception,
                policy,
                reportButtonLabel,
                DefaultOnReportClicked,
                settings.ExtraButtonLabel,
                settings.OnExtraButtonClicked
            );
        }

        /// <summary>
        /// Internal implementation of NotifyUserOfProblem that is generic enough to handle both the obsoleted legacy {resultIfAlternateButton1Pressed} approach
        /// and the modern approach using IBloomErrorReporter interface
        /// </summary>
        private void NotifyUserOfProblemInternal(
            string message,
            Exception exception,
            IRepeatNoticePolicy policy,
            string reportButtonLabel,
            Action<string, Exception> onReportButtonClicked,
            string extraButtonLabel = null,
            Action<string, Exception> onExtraButtonClicked = null
        )
        {
            // Let this thread try to acquire the lock, if necessary
            // Note: It is expected that sometimes this function will need to acquire the lock for this thread,
            //       and sometimes it'll already be acquired.
            //       The reason is because for legacy code that calls ErrorReport directly, this function is the first entry point into this class.
            //       But for code that needs the new secondaryAction functionality, it needs to enter through CustomNotifyUser*().
            //       That function wants to acquire a lock so that the instance variables it sets aren't modified by any other thread before
            //       entering this NotifyUserOfProblem() function.
            bool wasAlreadyLocked = System.Threading.Monitor.IsEntered(_lock);
            if (!wasAlreadyLocked)
            {
                System.Threading.Monitor.Enter(_lock);
            }

            try
            {
                string filePath = FileException.GetFilePathIfPresent(exception);
                // FileException is a Bloom exception to capture the filepath. We want to report the inner, original exception.
                Exception originalException = FileException.UnwrapIfFileException(exception);
                if (OneDriveUtils.CheckForAndHandleOneDriveExceptions(originalException, filePath))
                {
                    return;
                }

                if (policy == null)
                {
                    policy = new ShowAlwaysPolicy();
                }

                if (policy.ShouldShowMessage(message))
                {
                    ShowNotifyDialog(
                        ProblemLevel.kNotify,
                        message,
                        originalException,
                        reportButtonLabel,
                        onReportButtonClicked,
                        extraButtonLabel,
                        onExtraButtonClicked
                    );
                }

                return;
            }
            catch (Exception e)
            {
                var fallbackReporter = new WinFormsErrorReporter();
                fallbackReporter.ReportNonFatalException(e, new ShowAlwaysPolicy());
            }
            finally
            {
                // NOTE: Each thread needs to make sure it calls Exit() the same number of times as it calls Enter()
                // in order for other threads to be able to acquire the lock later.
                if (!wasAlreadyLocked)
                {
                    System.Threading.Monitor.Exit(_lock);
                }
            }
        }

        // ENHANCE: I think it would be good if ProblemReportApi could be split out.
        // Part of it is related to serving the API requests needed to make the Problem Report Dialog work. That should stay in ProblemReportApi.cs.
        // Another part of it is related to bring up a browser dialog. I think that part should be moved here into this HtmlErrorReporter class.
        // It'll be a big job though.
        //
        // Also, ProblemReportApi and this class share some parallel ideas because this class was derived from ProblemReportApi,
        // but they're not 100% identical because this class revamped some of those ideas.
        // So those will need to be merged.
        public void ReportNonFatalException(Exception exception, IRepeatNoticePolicy policy)
        {
            // Note: I think it's better to call ProblemReportApi directly instead of through NonFatalProblem first.
            // Otherwise you have to deal with NonFatalProblem's ModalIf, PassiveIf parameters.
            // And you also have to worry about whether Sentry will happen twice.
            ProblemReportApi.ShowProblemDialog(
                GetControlToUse(),
                exception,
                null,
                ProblemLevel.kNonFatal
            );
        }

        public void ReportNonFatalExceptionWithMessage(
            Exception error,
            string messageFormat,
            params object[] args
        )
        {
            var message = String.Format(messageFormat, args);
            var shortMsg = error?.Data["ProblemReportShortMessage"] as string;
            var imageFilepath = error?.Data["ProblemImagePath"] as string;
            var extraFilepath = error?.Data["ExtraFilePath"] as string;
            var extraPaths = new List<string>();
            if (!String.IsNullOrEmpty(extraFilepath) && RobustFile.Exists(extraFilepath))
                extraPaths.Add(extraFilepath);
            if (!String.IsNullOrEmpty(imageFilepath) && RobustFile.Exists(imageFilepath))
                extraPaths.Add(imageFilepath);
            string[] extraFilepaths = null;
            if (extraPaths.Count > 0)
                extraFilepaths = extraPaths.ToArray();
            ProblemReportApi.ShowProblemDialog(
                GetControlToUse(),
                error,
                message,
                ProblemLevel.kNonFatal,
                shortMsg,
                additionalPathsToInclude: extraFilepaths
            );
        }

        public void ReportNonFatalMessageWithStackTrace(string messageFormat, params object[] args)
        {
            var stackTrace = new StackTrace(true);
            var userLevelMessage = String.Format(messageFormat, args);
            string detailedMessage = FormatMessageWithStackTrace(userLevelMessage, stackTrace);
            ProblemReportApi.ShowProblemDialog(
                GetControlToUse(),
                null,
                detailedMessage,
                ProblemLevel.kNonFatal,
                userLevelMessage
            );
        }

        public void ReportFatalException(Exception e)
        {
            ProblemReportApi.ShowProblemDialog(GetControlToUse(), e, null, ProblemLevel.kFatal);
            Quit();
        }

        public void ReportFatalMessageWithStackTrace(string messageFormat, object[] args)
        {
            var stackTrace = new StackTrace(true);
            var userLevelMessage = String.Format(messageFormat, args);
            string detailedMessage = FormatMessageWithStackTrace(userLevelMessage, stackTrace);
            ProblemReportApi.ShowProblemDialog(
                GetControlToUse(),
                null,
                detailedMessage,
                ProblemLevel.kFatal,
                userLevelMessage
            );
            Quit();
        }
        #endregion

        protected Control GetControlToUse()
        {
            return this.Control
                ?? Shell.GetShellOrOtherOpenForm()
                ?? FatalExceptionHandler.ControlOnUIThread;
        }

        private string FormatMessageWithStackTrace(string message, StackTrace stackTrace)
        {
            return $"Message (not an exception): {message}"
                + Environment.NewLine
                + Environment.NewLine
                + "--Stack--"
                + Environment.NewLine
                + stackTrace.ToString();
        }

        private static void Quit() => Process.GetCurrentProcess().Kill(); // Same way WinFormsErrorReporter quits

        private void ShowNotifyDialog(
            string severity,
            string messageText,
            Exception exception,
            string reportButtonLabel,
            Action<string, Exception> onReportButtonClicked,
            string extraButtonLabel,
            Action<string, Exception> onExtraButtonClicked
        )
        {
            // Before we do anything that might be "risky", put the problem in the log.
            ProblemReportApi.LogProblem(exception, messageText, severity);

            // ENHANCE: Allow the caller to pass in the control, which would be at the front of this.
            //System.Windows.Forms.Control control = Form.ActiveForm ?? FatalExceptionHandler.ControlOnUIThread;
            var control = GetControlToUse();
            var isSyncRequired = false;
            SafeInvoke.InvokeIfPossible(
                "Show Error Reporter",
                control,
                isSyncRequired,
                () =>
                {
                    // Uses a browser dialog to show the problem report
                    try
                    {
                        StartupScreenManager.CloseSplashScreen(); // if it's still up, it'll be on top of the dialog

                        var message = GetMessage(messageText, exception);

                        if (!Api.BloomServer.ServerIsListening)
                        {
                            // There's no hope of using the HtmlErrorReporter dialog if our server is not yet running.
                            // We'll likely get errors, maybe Javascript alerts, that won't lead to a clean fallback to
                            // the exception handler below. Besides, failure of HtmlErrorReporter in these circumstances
                            // is expected; we just want to cleanly report the original problem, not to report a
                            // failure of error handling.

                            // Note: HtmlErrorReporter supports up to 3 buttons (OK, Report, and [Secondary action]), but the fallback reporter only supports a max of two.
                            // Well, just going to have to drop the secondary action.

                            ShowFallbackProblemDialog(
                                severity,
                                exception,
                                messageText,
                                null,
                                false,
                                reportButtonLabel,
                                onReportButtonClicked
                            );
                            return;
                        }

                        object props = new
                        {
                            level = ProblemLevel.kNotify,
                            reportLabel = reportButtonLabel,
                            secondaryLabel = extraButtonLabel,
                            message = message
                        };

                        // Precondition: we must be on the UI thread for Gecko to work.
                        using (
                            var dlg = BrowserDialogFactory.CreateReactDialog(
                                "problemReportBundle",
                                props
                            )
                        )
                        {
                            dlg.FormBorderStyle = FormBorderStyle.FixedToolWindow; // Allows the window to be dragged around
                            dlg.ControlBox = true; // Add controls like the X button back to the top bar
                            dlg.Text = ""; // Remove the title from the WinForms top bar

                            dlg.Width = 620;

                            // 360px was experimentally determined as what was needed for the longest known text for NotifyUserOfProblem
                            // (which is "Before saving, Bloom did an integrity check of your book [...]" from BookStorage.cs)
                            // You can make this height taller if need be.
                            // A scrollbar will appear if the height is not tall enough for the text
                            dlg.Height = 360;

                            // ShowDialog will cause this thread to be blocked (because it spins up a modal) until the dialog is closed.
                            BloomServer.RegisterThreadBlocking();

                            try
                            {
                                dlg.ShowDialog();

                                // Take action if the user clicked a button other than Close
                                if (
                                    dlg.CloseSource == "closedByAlternateButton"
                                    && onExtraButtonClicked != null
                                )
                                {
                                    onExtraButtonClicked(message, exception);
                                }
                                else if (dlg.CloseSource == "closedByReportButton")
                                {
                                    var onClicked =
                                        onReportButtonClicked != null
                                            ? onReportButtonClicked
                                            : DefaultOnReportClicked;
                                    onClicked(message, exception);
                                }

                                // Note: With the way LibPalaso's ErrorReport is designed,
                                // its intention is that after OnShowDetails is invoked and closed, you will not come back to the Notify Dialog
                                // This code has been implemented to follow that model
                                //
                                // But now that we have more options, it might be nice to come back to this dialog.
                                // If so, you'd need to add/update some code in this section.
                            }
                            finally
                            {
                                ResetToDefaults();
                                BloomServer.RegisterThreadUnblocked();
                            }
                        }
                    }
                    catch (Exception errorReporterException)
                    {
                        Logger.WriteError(
                            "*** HtmlErrorReporter threw an exception trying to display",
                            errorReporterException
                        );
                        // At this point our problem reporter has failed for some reason, so we want the old WinForms handler
                        // to report both the original error for which we tried to open our dialog and this new one where
                        // the dialog itself failed.
                        // In order to do that, we create a new exception with the original exception (if there was one) as the
                        // inner exception. We include the message of the exception we just caught. Then we call the
                        // old WinForms fatal exception report directly.
                        // In any case, both of the errors will be logged by now.
                        var message =
                            "Bloom's error reporting failed: " + errorReporterException.Message;

                        // Fallback to Winforms in case of trouble getting the browser to work
                        var fallbackReporter = new WinFormsErrorReporter();
                        // Food for thought: is it really fatal of the Notify Dialog had an exception? Maybe NonFatal makes more sense
                        fallbackReporter.ReportFatalException(
                            new ApplicationException(message, exception ?? errorReporterException)
                        );
                    }
                }
            );
        }

        internal static string GetMessage(string detailedMessage, Exception exception)
        {
            return !string.IsNullOrEmpty(detailedMessage) ? detailedMessage : exception.Message;
        }

        public static void DefaultOnReportClicked(string message, Exception error)
        {
            ErrorReport.ReportNonFatalExceptionWithMessage(error, message);
        }

        public static void ShowFallbackProblemDialog(
            string levelOfProblem,
            Exception exception,
            string detailedMessage,
            string shortUserLevelMessage,
            bool isShortMessagePreEncoded = false,
            string reportButtonLabel = null,
            Action<string, Exception> onReportButtonClicked = null
        )
        {
            var fallbackReporter = new WinFormsErrorReporter();

            if (shortUserLevelMessage == null)
                shortUserLevelMessage = "";

            string decodedShortUserLevelMessage = isShortMessagePreEncoded
                ? XmlString.FromXml(shortUserLevelMessage).Unencoded
                : shortUserLevelMessage;
            string message = decodedShortUserLevelMessage;
            if (!String.IsNullOrEmpty(detailedMessage))
                message += $"\n{detailedMessage}";

            if (levelOfProblem == ProblemLevel.kFatal)
            {
                if (exception != null)
                    fallbackReporter.ReportFatalException(exception);
                else
                    fallbackReporter.ReportFatalMessageWithStackTrace(message, null);
            }
            else if (
                levelOfProblem == ProblemLevel.kNonFatal || levelOfProblem == ProblemLevel.kUser
            )
            {
                // FYI, if levelOfProblem==kUser, we're unfortunately going to be
                // using the messaging from NonFatal even though we would ideally like to have the customized messaging for levelOfProblem==kUser,
                // but we'll just live with it because there's no easy way to customize it. It's probably an extremely rare situation anyway
                if (String.IsNullOrEmpty(message))
                    fallbackReporter.ReportNonFatalException(exception, new ShowAlwaysPolicy());
                else
                    fallbackReporter.ReportNonFatalExceptionWithMessage(exception, message);
            }
            else // Presumably, levelOfProblem = "notify" now
            {
                if (
                    !String.IsNullOrEmpty(message)
                    && !String.IsNullOrEmpty(reportButtonLabel)
                    && onReportButtonClicked != null
                )
                {
                    var dialogResult = fallbackReporter.NotifyUserOfProblem(
                        new ShowAlwaysPolicy(),
                        reportButtonLabel,
                        ErrorResult.Yes,
                        message
                    );
                    if (dialogResult == ErrorResult.Yes)
                        onReportButtonClicked(message, exception);
                }
                else
                {
                    fallbackReporter.NotifyUserOfProblem(
                        new ShowAlwaysPolicy(),
                        exception,
                        message
                    );
                }
            }
        }
    }
}
