using System;
using System.Diagnostics;
using Bloom.web;
using SIL.Reporting;

namespace Bloom.ErrorReporter
{
    internal class BloomErrorReport : SIL.Reporting.ErrorReport
    {
        /// <summary>
        /// Notifies the user of problem, with customized button labels and button click handlers
        /// </summary>
        /// <param name="message">The message to show the user</param>
        /// <param name="settings">Optional: The customization settings controlling which/how extra buttons show up</param>
        /// <param name="policy">Optional: The policy to use to decide whether to show the notification. Defaults to ShowAlwaysPolicy</param>
        public static void NotifyUserOfProblem(
            string message,
            NotifyUserOfProblemSettings settings,
            IRepeatNoticePolicy policy = null
        ) => NotifyUserOfProblem(message, null, settings, policy);

        /// <summary>
        /// Notifies the user of problem, with customized button labels and button click handlers
        /// </summary>
        /// <param name="message">The message to show the user</param>
        /// <param name="exception">Any accompanying exception</param>
        /// <param name="settings">Optional: The customization settings controlling which/how extra buttons show up</param>
        /// <param name="policy">Optional: The policy to use to decide whether to show the notification. Defaults to ShowAlwaysPolicy</param>
        public static void NotifyUserOfProblem(
            string message,
            Exception exception,
            NotifyUserOfProblemSettings settings,
            IRepeatNoticePolicy policy = null
        )
        {
            if (policy == null)
                policy = new ShowAlwaysPolicy();

            NotifyUserOfProblemWrapper(
                message,
                exception,
                () =>
                {
                    IErrorReporter errorReporter = GetErrorReporter();
                    if (errorReporter is IBloomErrorReporter)
                    {
                        // Normal situation
                        ((IBloomErrorReporter)errorReporter).NotifyUserOfProblem(
                            message,
                            exception,
                            settings,
                            policy
                        );
                    }
                    else
                    {
                        // Exceptional situation
                        // One case where this can be expected to appear is if you're using Bloom to manually test libpalaso's ErrorReporters,
                        // but otherwise, we shouldn't expect to reach here during normal operation!
                        Debug.Fail(
                            "Warning: Expected SetErrorReporter() to be called with an IBloomErrorReporter, but actual object was not an instance of that type."
                        );

                        // Unexpected to not have the derived type, but just do our best using the base type
                        // The base type is a little clunkier to use, and also doesn't support customization of the extra button at all,
                        // but we'll do our best using it.
                        string alternateButton1Label = exception != null ? "Details" : "";

                        var result = errorReporter.NotifyUserOfProblem(
                            policy,
                            alternateButton1Label,
                            ErrorResult.Yes,
                            message
                        );
                        if (result == ErrorResult.Yes)
                            HtmlErrorReporter.DefaultOnReportClicked(message, exception);
                    }

                    return ErrorResult.OK;
                }
            );
        }

        /// <summary>
        /// Notify the user by first showing a toast containing shortMsg. If the user clicks that,
        /// show longerMsg. (This is meant to be something relatively unimportant, so we don't
        /// allow the user to report it, and don't do any logging here except what the regular
        /// NotifyUserOfProblem may do if the toast is clicked.)
        /// </summary>
        public static void NotifyUserUnobtrusively(
            string shortMsg,
            string longerMsg,
            Exception ex = null
        )
        {
            ToastService.ShowToast(
                ToastSeverity.Warning,
                text: shortMsg,
                durationSeconds: 10,
                dedupeKey: shortMsg,
                action: new ToastAction
                {
                    Callback = () =>
                    {
                        BloomErrorReport.NotifyUserOfProblem(
                            longerMsg,
                            ex,
                            new NotifyUserOfProblemSettings(AllowSendReport.Disallow)
                        );
                    },
                }
            );
        }
    }
}
