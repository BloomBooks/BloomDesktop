using SIL.Reporting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Bloom.ErrorReporter
{
	/// <summary>
	/// This class wraps LibPalaso's ErrorReport.cs class, particuarly the NotifyUserOfProblem overloads.
	/// The wrapper/adapter is there because HtmlErrorReporter has the concept of a secondary action button, in addition to the Report (Report the problem) button.
	/// So, these wrapper functions expose parameters related to the secondary action button.
	///
	/// There are 5 overloads of NotifyUserOfProblem, which can largely be lumped into two different groups:
	/// 1) Auto-Invoke versions (the button press handler actions are automatically invoked by ErrorReport.cs after the button is clicked), or
	/// 2) Manual Invoke versions (the function returns a return value code which indicates which button was pressed)
	/// 
	/// If you want to know exactly what each overload does, knowing the internals of how ErrorReport works is important.
	/// If so, refer to libpalaso's SIL.Core\Reporting\ErrorReport.cs
	/// </summary>
	public class ErrorReportUtils
	{
		/// <summary>
		/// Wrapper around 2-argument overload of ErrorReport.NotifyUserOfProblem
		/// This wrapper is identical to calling ErrorReport directly.
		/// 
		/// Other than OK, no other buttons will be shown.
		/// </summary>
		public static void NotifyUserOfProblem(string message, params object[] args)
		{
			ErrorReport.NotifyUserOfProblem(message, args);
		}

		/// <summary>
		/// Wrapper around 3-argument overload (with policy) of ErrorReport.NotifyUserOfProblem
		/// This wrapper is identical to calling ErrorReport directly.
		/// 
		/// Other than OK, no other buttons will be shown.
		/// </summary>
		public static ErrorResult NotifyUserOfProblem(IRepeatNoticePolicy policy, string messageFmt, params object[] args)
		{
			return ErrorReport.NotifyUserOfProblem(policy, messageFmt, args);
		}

		/// <summary>
		/// Adapter around 3-argument overload (with input exception) of ErrorReport.NotifyUserOfProblem (Auto-invoke version)
		/// This adapter exposes two additional parameters to control the secondary action button
		/// 
		/// Uses the default Report button (previously known as Details button or alternateButton1)
		/// The corresponding action will be automatically invoked by ErrorReport.cs after each button is clicked.
		/// </summary>
		/// /// <param name="secondaryActionLabel">Optional - The localized text of the secondary action button. Pass null or "" to disable this button.</param>
		/// <param name="onSecondaryPressed">Optional - The action to execute after the secondary action button is pressed. You may pass null if no action is desired.</param>
		/// <param name="error">Optional - The error to report. Or you may rely on {messageFmt}</param>
		/// <param name="messageFmt">Optional - The message to report to the user. May be a format string. Or, you may rely on {error} instead.</param>
		/// <param name="args">The format string arguments</param>
		public static void NotifyUserOfProblem(string secondaryActionLabel, Action<Exception, string> onSecondaryPressed,
			Exception error, string messageFmt, params object[] args)
		{
			HtmlErrorReporter.Instance.CustomNotifyUserAuto(null, secondaryActionLabel, onSecondaryPressed, error, messageFmt, args);
		}

		/// <summary>
		/// Adapter around 4-argument overload of ErrorReport.NotifyUserOfProblem (Auto-invoke version)
		/// This adapter exposes two additional parameters to control the secondary action button
		/// 
		/// Uses the default Report button (aka Details button or alternateButton1)
		/// The corresponding action will be automatically invoked by ErrorReport.cs after each button is clicked.
		/// </summary>
		/// <param name="secondaryActionLabel">Optional - The localized text of the secondary action button. Pass null or "" to disable this button.</param>
		/// <param name="onSecondaryPressed">Optional - The action to execute after the secondary action button is pressed. If the secondary action is disabled, it is safe to pass null here. But if it is enabled and you actually want nothing to happen, you should pass in an Action that does nothing, not pass null</param>
		/// <param name="error">Optional - The error to report. Or you may rely on {messageFmt}</param>
		/// <param name="messageFmt">Optional - The message to report to the user. May be a format string. Or, you may rely on {error} instead.</param>
		/// <param name="args">The format string arguments</param>
		public static void NotifyUserOfProblem(string secondaryActionLabel, Action<Exception, string> onSecondaryPressed,
			IRepeatNoticePolicy policy, Exception error, string messageFmt, params object[] args)
		{
			HtmlErrorReporter.Instance.CustomNotifyUserAuto(null, secondaryActionLabel, onSecondaryPressed, policy, error, messageFmt, args);
		}

		/// <summary>
		/// An adapter around the 5-argument overload of LibPalaso's ErrorReport's NotifyUserOfProblem (Manual Invoke version)
		/// This adapter exposes two additional parameters to control the secondary action button
		///
		/// This version allows you to add a Report and Secondary Action button, if desired
		/// 
		/// This version does not automatically invoke any actions upon button presses,
		/// but relies on returning an ErrorResult to indicate which button was pressed
		/// The caller should check the return value for control flow and take whatever action it desires accordingly.
		/// </summary>
		/// <param name="policy">The policy that indicates how often the message should be shown</param>
		/// <param name="reportButtonLabel">The localized text of the button that will lead to the Problem Report Dialog (report an issue to the issue-tracking system)</param>
		/// <param name="resultIfReportButtonPressed">The return value that this function should return to indicate to the caller that the Report button was pressed</param>
		/// <param name="secondaryActionButtonLabel">The localized text of the button that initiates the secondary action.</param>
		/// <param name="resultIfSecondaryPressed">The return value that this function should return to indicate to the caller that the secondary action button was pressed</param>
		/// <param name="messageFmt">The message to display to the user. May be a format string</param>
		/// <param name="args">Optional. The args to pass to the messageFmt format string.</param>
		/// <returns>ErrorResult.OK if OK button pressed, or {resultIfReportButtonPressed} if the user clicks the Report button, or {resultIfSecondaryActionButtonPressed} if the user clicks the secondary action button. Or ErrorResult.Abort if error.</returns>
		public static ErrorResult NotifyUserOfProblem(
			IRepeatNoticePolicy policy,
			string reportButtonLabel, /* The Report button is equivalent to the Details button in libpalaso's WinFormsErrorReporter of {alternateButton1} in libpalaso's ErrorReport */
			ErrorResult resultIfReportButtonPressed,
			string secondaryActionButtonLabel, /* An additional action such as Retry / etc */
			ErrorResult resultIfSecondaryPressed,
			string messageFmt,
			params object[] args)
		{
			return HtmlErrorReporter.Instance.CustomNotifyUserManual(policy, reportButtonLabel, resultIfReportButtonPressed, secondaryActionButtonLabel, resultIfSecondaryPressed, messageFmt, args);
		}

		#region Premade Alternate Actions
		internal static void TestAction(Exception error, string message)
		{
			MessageBox.Show("Secondary Action button pressed.");
		}
		#endregion
	}
}
