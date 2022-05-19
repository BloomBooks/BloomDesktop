#if !debug
using Bloom.WebLibraryIntegration;
#endif
using L10NSharp;
using SIL.Reporting;
using System;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Bloom.ErrorReporter
{
	internal interface IBloomErrorReporter : IErrorReporter
	{
		/// <summary>
		/// Sets all the extra parameters required to customize the buttons/extra buttons when calling NotifyUserOfProblem
		/// They can't be directly added to NotifyUserOfProblem because it needs to match the IErrorReporter interface.
		/// As a result, we work around that by having class instance variables that you set before invoking NotifyUserOfProblem.
		/// </summary>
		/// <param name="reportButtonLabel">The localized text of the report button. Pass null for default behavior. Pass "" to disable.</param>
		/// <param name="onReportButtonPressed">The action to execute after the report button is pressed. Pass null to use the default behavior.</param>
		/// <param name="extraButtonLabel">The localized text of the extra button. Pass null for default behavior. Pass "" to disable.</param>
		/// <param name="onExtraButtonPressed">The action to execute after the extra button is pressed. Pass null to use the default behavior.</param>
		void SetNotifyUserOfProblemCustomParams(string reportButtonLabel = null, Action<Exception, string> onReportButtonPressed = null,
			string extraButtonLabel = null, Action<Exception, string> onExtraButtonPressed = null);
	}

	/// <summary>
	/// This class is based on LibPalaso's <see cref="ErrorReport"/> class,
	/// but adds methods to call NotifyUserOfProblem with more button customization
	/// </summary>
	public class ErrorReportUtils
	{
		/// <summary>
		/// Customized version of ErrorReport.NotifyUserOfProblem that allows customization of the button labels and what they do.
		/// (This will eventually call <see cref="ErrorReport.NotifyUserOfProblem(IRepeatNoticePolicy, Exception, string, object[])"/>)
		/// </summary>
		/// <param name="message">The message to report to the user.</param>
		/// <param name="exception">Optional - Any exception accompanying the message.</param>
		/// <param name="reportButtonLabel">Optional - The localized text of the report button. Pass null to use the default behavior.</param>
		/// <param name="onReportButtonPressed">Optional - The action to execute after the report button is pressed. Pass null to use the default behavior.</param>
		/// <param name="extraButtonLabel">Optional - The localized text of the secondary action button. Pass null to use the default behavior.</param>
		/// <param name="onExtraButtonPressed">Optional - The action to execute after the secondary action button is pressed. Pass null to use the default behavior.</param>
		/// <param name="policy">Optional - The policy for how often to show the message. If null or not set, then ShowAlwaysPolicy will be used.</param>
		public static void NotifyUserOfProblem(string message, Exception exception = null,
			string reportButtonLabel = null, Action<Exception, string> onReportButtonPressed = null,
			string extraButtonLabel = null, Action<Exception, string> onExtraButtonPressed = null,
			IRepeatNoticePolicy policy = null)
		{
			Program.ErrorReporter.SetNotifyUserOfProblemCustomParams(reportButtonLabel, onReportButtonPressed, extraButtonLabel, onExtraButtonPressed);
			ErrorReport.NotifyUserOfProblem(policy ?? new ShowAlwaysPolicy(), exception, message);
		}

		#region Premade Alternate Actions
		internal static void TestAction(Exception error, string message)
		{
			MessageBox.Show("Secondary Action button pressed.");
		}
		#endregion

		#region Fake Test Errors
		/// <summary>
		/// Generates Error Reports for books with specific titles, but only in Debug or Sandbox mode
		/// Facilitates manual testing of error reporting using specific books.
		/// </summary>
		internal static void CheckForFakeTestErrorsIfNotRealUser(string title)
		{
			// A real user is defined as one using a Release build (i.e. not a Debug build) and not using Sandbox mode.
			// Skip these checks for real users, so there's no possibility of them getting spurious error reports
			// from this code (even if the titles required are unlikely real titles)
			#if DEBUG
				bool checkAllowed = true;
#else
				bool checkAllowed = BookUpload.UseSandbox;
#endif


			if (checkAllowed)
			{
				// Run on the current thread (Should be the main thread)
				CheckForFakeTestErrors(title);

				//// Use this version to test running off the main thread
				//// (This is just a toy example, don't assume that just because this thread works, your code will never deadlock or anything like that
				//// Note: A slightly more realistic example is to generate these errors on a server worker thread. e.g. in Book.cs::GetPreviewHtmlFileForWholeBook() 
				//new Thread(() =>
				//{
				//	CheckForFakeTestErrors(title);
				//}).Start();
			}
		}

		/// <summary>
		/// Generates Error Reports for books with specific titles
		/// Facilitates manual testing of error reporting using specific books.
		/// </summary>
		private static void CheckForFakeTestErrors(string title)
		{
			const string fakeProblemMessage = "Fake problem for development/testing purposes";
			var fakeException = new ApplicationException("Fake exception for development/testing purposes");
			
			if (title == "Error NotifyUser NoReport")
			{
				// Exercises a path through libPalaso directly (goes thru overloads 1, 2, 4)
				ErrorReport.NotifyUserOfProblem(fakeProblemMessage);
			}
			else if (title == "Error NotifyUser NoReport 2")
			{
				// Exercises a path through libPalaso directly (goes thru overloads 3, 4)
				ErrorReport.NotifyUserOfProblem((Exception)null, fakeProblemMessage);
			}
			else if (title == "Error NotifyUser NoReport 3")
			{
				// Exercises a path where you go through the ErrorReportUtils adapters
				ErrorReportUtils.NotifyUserOfProblem(fakeProblemMessage);
			}
			else if (title == "Error NotifyUser LongMessage")
			{
				var longMessageBuilder = new StringBuilder();
				while (longMessageBuilder.Length < 3000)
					longMessageBuilder.Append(fakeProblemMessage + " ");

				ErrorReport.NotifyUserOfProblem(longMessageBuilder.ToString());
			}
			else if (title == "Error NotifyUser Report NoRetry")
			{
				// Exercises another path through libPalaso directly (goes thru overloads 3, 4)
				ErrorReport.NotifyUserOfProblem(fakeException, fakeProblemMessage);
			}
			else if (title == "Error NotifyUser Report NoRetry 2")
			{
				// Exercises a path where you go through the ErrorReportUtils adapters
				ErrorReportUtils.NotifyUserOfProblem(fakeProblemMessage, fakeException);
			}
			else if (title == "Error NotifyUser Report Retry")
			{
				// Exercises a path where you need to go through the ErrorReportUtils adapters
				var secondaryButtonLabel = LocalizationManager.GetString("ErrorReportDialog.Retry", "Retry");
				ErrorReportUtils.NotifyUserOfProblem(fakeProblemMessage, fakeException, null, null, secondaryButtonLabel, ErrorReportUtils.TestAction);
			}
			else if (title == "Error NotifyUser Custom")
			{
				// Exercises a path where you need to go through the ErrorReportUtils adapters
				var secondaryButtonLabel = LocalizationManager.GetString("ErrorReportDialog.Retry", "Retry");
				ErrorReportUtils.NotifyUserOfProblem(fakeProblemMessage, fakeException, "CustomReport", (ex, msg) => { MessageBox.Show("CustomReport button pressed."); }, secondaryButtonLabel, ErrorReportUtils.TestAction);
			}
			else if (title == "Error NotifyUser LegacyInterface")
			{
				// Exercises the legacy 5-argument implementation in libpalaso
				// (follow-up actions are manually invoked by the caller)
				var result = ErrorReport.NotifyUserOfProblem(new ShowAlwaysPolicy(), "CustomReport", ErrorResult.Yes, fakeProblemMessage);

				string message = result == ErrorResult.Yes ? "Report button clicked. [Legacy]" : null;
				if (message != null)
					MessageBox.Show(message);
			}
			else if (title == "Error ReportNonFatalException")
			{
				ErrorReport.ReportNonFatalException(fakeException);
			}
			else if (title == "Error ReportNonFatalExceptionWithMessage")
			{
				ErrorReport.ReportNonFatalExceptionWithMessage(fakeException, fakeProblemMessage);
			}
			else if (title == "Error ReportNonFatalExceptionWithMessage Scrollbar")
			{
				var longMessageBuilder = new StringBuilder();
				while (longMessageBuilder.Length < 500)
					longMessageBuilder.AppendLine(fakeProblemMessage);
				ErrorReport.ReportNonFatalExceptionWithMessage(fakeException, longMessageBuilder.ToString());
			}
			else if (title == "Error ReportNonFatalMessageWithStackTrace")
			{
				ErrorReport.ReportNonFatalMessageWithStackTrace(fakeProblemMessage);
			}
			else if (title == "Error ReportFatalException")
			{
				ErrorReport.ReportFatalException(fakeException);
			}
			else if (title == "Error ReportFatalMessageWithStackTrace")
			{
				ErrorReport.ReportFatalMessageWithStackTrace(fakeProblemMessage);
			}
			else if (title == "Error ReportFatalMessageWithStackTrace Scrollbar")
			{
				var longMessageBuilder = new StringBuilder();
				while (longMessageBuilder.Length < 500)
					longMessageBuilder.AppendLine(fakeProblemMessage);
				ErrorReport.ReportFatalMessageWithStackTrace(longMessageBuilder.ToString());
			}
		}
		#endregion
	}
}
