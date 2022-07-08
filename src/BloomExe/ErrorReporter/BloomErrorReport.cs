using SIL.Reporting;
using System;
using System.Diagnostics;

namespace Bloom.ErrorReporter
{
	internal class BloomErrorReport : SIL.Reporting.ErrorReport
	{
		/// <summary>
		/// Notifies the user of problem, with customized button labels and button click handlers
		/// </summary>
		/// <param name="message">The message to show the user</param>
		/// <param name="exception">Any accompanying exception</param>
		/// <param name="policy">Optional: The policy to use to decide whether to show the notification. Defaults to ShowAlwaysPolicy</param>
		/// <param name="allowSendReport">Whether or not to show the Report button.</param>
		/// <param name="extraButtonLabel">Optional: The label of the Report button. Pass "" to disable. Defaults to disabled.</param>
		/// <param name="onExtraButtonPressed">Optional: The action to take when the extra button is pressed.</param>
		public static void NotifyUserOfProblemCustom(string message, Exception exception = null, IRepeatNoticePolicy policy = null,
			AllowSendReport allowSendReport = AllowSendReport.AllowIfException,
			string extraButtonLabel = null, Action<string, Exception> onExtraButtonPressed = null)
		{
			if (policy == null)
				policy = new ShowAlwaysPolicy();

			NotifyUserOfProblemWrapper(message, exception, () =>
			{
				IErrorReporter errorReporter = GetErrorReporter();
				if (errorReporter is IBloomErrorReporter)
				{
					// Normal situation
					((IBloomErrorReporter)errorReporter).NotifyUserOfProblemCustom(message, exception, policy,
						allowSendReport, extraButtonLabel, onExtraButtonPressed);
				}
				else
				{
					// Exceptional situation
					// One case where this can be expected to appear is if you're using Bloom to manually test libpalaso's ErrorReporters,
					// but otherwise, we shouldn't expect to reach here during normal operation!
					Debug.Fail("Warning: Expected SetErrorReporter() to be called with an IBloomErrorReporter, but actual object was not an instance of that type.");

					// Unexpected to not have the derived type, but just do our best using the base type
					string alternateButton1Label = exception != null ? "Details" : "";

					var result = errorReporter.NotifyUserOfProblem(policy, alternateButton1Label, ErrorResult.Yes, message);
					if (result == ErrorResult.Yes)
						HtmlErrorReporter.DefaultOnReportClicked(message, exception);
				}

				return ErrorResult.OK;
			});
		}
	}
}
