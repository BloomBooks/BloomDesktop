using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.MiscUI;
using Bloom.ToPalaso;
using Bloom.web.controllers;
using SIL.Reporting;
using SIL.Windows.Forms.Reporting;

namespace Bloom.ErrorReporter
{
	/// <summary>
	/// An Error Reporter designed to be used with libpalaso's ErrorReport.
	/// Unlike WinFormsErrorReporter, which uses WinForms to display the UI, this utilizes a browser to display the UI
	/// </summary>
	public class HtmlErrorReporter: IErrorReporter
	{
		private HtmlErrorReporter()
		{
			ResetToDefaults();
			DefaultReportLabel = L10NSharp.LocalizationManager.GetString("ErrorReportDialog.Report", "Report");
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
			set
			{
				_bloomServer = value;
			}
		}
		#endregion

		#region Additional NotifyUserOfProblem parameters saved as instance vars
		protected string ReportButtonLabel { get; set; }
		protected string SecondaryActionButtonLabel { get; set; }
		protected Action<Exception, string> OnSecondaryActionPressed { get; set; } = null;
		protected ErrorResult? SecondaryActionResult { get; set; }
		#endregion

		private void ResetToDefaults()
		{
			ReportButtonLabel = null;
			SecondaryActionButtonLabel = null;
			OnSecondaryActionPressed = null;
			SecondaryActionResult = null;
			Control = null;
		}

		/// <summary>
		/// Sets all the extra parameters required for the auto-invoke versions of NotifyUserOfProblem to work correctly.
		/// They can't be directly added to NotifyUserOfProblem because it needs to match the IErrorReporter interface.
		/// As a result, we work around that by having class instance variables that you set before invoking NotifyUserOfProblem.
		/// This function exposes only the class instance variables that are necessary for Auto-Invoke versions to work.
		/// Auto-invoke means the overloads that want an Action, which will be automatically invoked by ErrorReport when the corresponding button is pressed.
		///    (as opposed to the overloads that use return codes to accomplish that)
		/// </summary>
		/// <param name="reportButtonLabel">The localized text for the Report button (aka alternateButton1 in libpalaso)</param>
		/// <param name="secondaryActionButtonLabel">The localized text for the Secondary Action button. For example, you might offer a "Retry" button</param>
		/// <param name="onSecondaryActionPressed">The Action that will be invoked after the Secondary Action button is pressed. Note: the action is invoked after the dialog is closed.</param>
		protected void SetExtraParamsForCustomNotifyAuto(string reportButtonLabel, string secondaryActionButtonLabel, Action<Exception, string> onSecondaryActionPressed)
		{
			this.ReportButtonLabel = reportButtonLabel;
			this.SecondaryActionButtonLabel = secondaryActionButtonLabel;
			this.OnSecondaryActionPressed = onSecondaryActionPressed;
			this.SecondaryActionResult = null;
		}

		/// <summary>
		/// Sets all the extra parameters required for the manual-invoke versions of NotifyUserOfProblem to work correctly.
		/// They can't be directly added to NotifyUserOfProblem because it needs to match the IErrorReporter interface.
		/// As a result, we work around that by having class instance variables that you set beofre invoking NotifyUserOfProblem.
		/// This function exposes only the class instance variables that are necessary for Manual-Invoke versions to work.
		/// Manual-invoke means the overloads that utilize return codes to tell the caller which button was pressed.
		/// Then the caller manually invokes the code that should happen for that button.
		///    (as opposed to the overloads that ask for an action to invoke for you)
		/// </summary>
		/// <param name="reportButtonLabel">The localized text for the Report button (aka alternateButton1 in libpalaso)</param>
		/// <param name="secondaryActionButtonLabel">The localized text for the Secondary Action button. For example, you might offer a "Retry" button</param>
		/// <param name="resultIfSecondaryActionPressed">The return code that the caller would like this function to return to indicate that the secondary action button was pressed</param>
		protected void SetExtraParamsForCustomNotifyManual(string reportButtonLabel, string secondaryActionButtonLabel, ErrorResult resultIfSecondaryActionPressed)
		{
			this.ReportButtonLabel = reportButtonLabel;
			this.SecondaryActionButtonLabel = secondaryActionButtonLabel;
			this.SecondaryActionResult = resultIfSecondaryActionPressed;
		}

		/// <summary>
		/// Notifies the user of a problem, using a browser-based dialog.
		/// A Report and a secondary action button are potentially available. This method
		/// will automatically invoke the corresponding Action for each button that is clicked.
		/// </summary>
		/// <param name="reportButtonLabel">The localized text that goes on the Report button. null means Use Default ("Report"). Empty string means disable the button</param>
		/// <param name="secondaryActionButtonLabel">The localized text that goes on the Report button. Either null or empty string means disable the button</param>
		/// <param name="onSecondaryActionPressed">Optional - The action which will be invoked if secondary action button is clicked. You may pass null, but that will invoke the ErrorReport default (which is to report a non-fatal exception). ErrorReport.cs will pass the Action an exception ({error}) and a string (the {message} formatted with {args}, which you will probably ignore.</param>
		/// <param name="error">Optional - Any exception that was encountered that should be included in the notification/report. May be null</param>
		/// <param name="message">The message to show to the user. May be a format string.</param>
		/// <param name="args">The args to pass to the {message} format string</param>
		public void CustomNotifyUserAuto(string reportButtonLabel, string secondaryActionButtonLabel, Action<Exception, string> onSecondaryActionPressed,
			Exception error, string message, params object[] args)
		{
			Debug.Assert(!System.Threading.Monitor.IsEntered(_lock), "Expected object not to have been locked yet, but the current thread already aquired it earlier. Bug?");
			// Block until lock acquired
			System.Threading.Monitor.Enter(_lock);

			try
			{
				SetExtraParamsForCustomNotifyAuto(reportButtonLabel, secondaryActionButtonLabel, onSecondaryActionPressed);

				// Note: It's more right to go through ErrorReport than to invoke
				// our NotifyUserOfProblem directly. ErrorReport has some logic,
				// and it's also necessary if we have a CompositeErrorReporter
				ErrorReport.NotifyUserOfProblem(error, message, args);
			}
			finally
			{
				System.Threading.Monitor.Exit(_lock);
			}
		}

		/// <summary>
		/// Notifies the user of a problem, using a browser-based dialog.
		/// A Report and a secondary action button are potentially available. This method
		/// will automatically invoke the corresponding Action for each button that is clicked.
		/// </summary>
		/// <param name="reportButtonLabel">The localized text that goes on the Report button. null means Use Default ("Report"). Empty string means disable the button</param>
		/// <param name="secondaryActionButtonLabel">The localized text that goes on the Report button. Either null or empty string means disable the button</param>
		/// <param name="onSecondaryActionPressed">Optional - The action which will be invoked if secondary action button is clicked. You may pass null, but that will invoke the ErrorReport default (which is to report a non-fatal exception). ErrorReport.cs will pass the Action an exception ({error}) and a string (the {message} formatted with {args}, which you will probably ignore.</param>
		/// <param name="policy">Checks if we should notify the user, based on the contents of {message}</param>
		/// <param name="error">Optional - Any exception that was encountered that should be included in the notification/report. May be null</param>
		/// <param name="message">The message to show to the user. May be a format string.</param>
		/// <param name="args">The args to pass to the {message} format string</param>
		public void CustomNotifyUserAuto(string reportButtonLabel, string secondaryActionButtonLabel, Action<Exception, string> onSecondaryActionPressed ,
			IRepeatNoticePolicy policy, Exception error, string message, params object[] args)
		{
			Debug.Assert(!System.Threading.Monitor.IsEntered(_lock), "Expected object not to have been locked yet, but the current thread already aquired it earlier. Bug?");
			// Block until lock acquired
			System.Threading.Monitor.Enter(_lock);

			try
			{
				SetExtraParamsForCustomNotifyAuto(reportButtonLabel, secondaryActionButtonLabel, onSecondaryActionPressed);

				// Note: It's more right to go through ErrorReport than to invoke
				// our NotifyUserOfProblem directly. ErrorReport has some logic,
				// and it's also necessary if we have a CompositeErrorReporter
				ErrorReport.NotifyUserOfProblem(policy, error, message, args);
			}
			finally
			{
				System.Threading.Monitor.Exit(_lock);
			}
		}

		/// <summary>
		/// Notifies the user of a problem, using a browser-based dialog.
		/// A Report and a secondary action button are potentially available. This method
		/// will return a return code to the caller to indicate which button was pressed.
		/// It is the caller's responsibility to perform any appropriate actions based on the button click.
		/// </summary>
		/// <param name="policy">Checks if we should notify the user, based on the contents of {message}</param>
		/// <param name="reportButtonLabel">The localized text that goes on the Report button. null means Use Default ("Report"). Empty string means disable the button</param>
		/// <param name="resultIfReportButtonPressed">This is the value that this method should return so that the caller
		/// can know if the Report button was pressed, and if so, the caller can invoke whatever actions are desired.</param>
		/// <param name="secondaryActionButtonLabel">The localized text that goes on the Report button. Either null or empty string means disable the button</param>
		/// <param name="resultIfSecondaryPressed">This is the value that this method should return so that the caller
		/// can know if the secondary action button was pressed, and if so, the caller can invoke whatever actions are desired.</param>
		/// <param name="messageFmt">The message to show to the user</param>
		/// <param name="args">The args to pass to the {messageFmt} format string</param>
		/// <returns>If closed normally, returns ErrorResult.OK
		/// If the report button was pressed, returns {resultIfAlternateButtonPressed}.
		/// If the secondary action button was pressed, returns {this.SecondaryActionResult} if that is non-null; otherwise falls back to {resultIfAlternateButtonPressed}
		/// If an exception is thrown while executing this function, returns ErrorResult.Abort.
		/// </returns>
		public ErrorResult CustomNotifyUserManual(
			IRepeatNoticePolicy policy,
			string reportButtonLabel, /* The Report button is equivalent to the Details button in libpalaso's WinFormsErrorReporter of {alternateButton1} in libpalaso's ErrorReport */
			ErrorResult resultIfReportButtonPressed,
			string secondaryActionButtonLabel, /* An additional action such as Retry / etc */
			ErrorResult resultIfSecondaryPressed,
			string messageFmt,
			params object[] args)
		{
			Debug.Assert(!System.Threading.Monitor.IsEntered(_lock), "Expected object not to have been locked yet, but the current thread already aquired it earlier. Bug?");
			// Block until lock acquired
			System.Threading.Monitor.Enter(_lock);

			try
			{
				SetExtraParamsForCustomNotifyManual(reportButtonLabel, secondaryActionButtonLabel, resultIfSecondaryPressed);

				// Note: It's more right to go through ErrorReport than to invoke
				// our NotifyUserOfProblem directly. ErrorReport has some logic,
				// and it's also necessary if we have a CompositeErrorReporter
				return ErrorReport.NotifyUserOfProblem(policy, reportButtonLabel, resultIfReportButtonPressed, messageFmt, args);
			}
			finally
			{
				System.Threading.Monitor.Exit(_lock);
			}
		}

		#region IErrorReporter interface
		/// <summary>
		/// Notifies the user of a problem, using a browser-based dialog.
		/// Note: This is designed to be called by LibPalaso's ErrorReport class.
		/// </summary>
		/// <param name="policy">Checks if we should notify the user, based on the contents of {message}</param>
		/// <param name="alternateButton1Label">The text that goes on the Report button. However, if speicfied, this.ReportButtonLabel takes precedence over this parameter</param>
		/// <param name="resultIfAlternateButtonPressed">This is the value that this method should return so that the caller (mainly LibPalaso ErrorReport)
		/// can know if the alternate button was pressed, and if so, invoke whatever actions are desired.</param>
		/// <param name="message">The message to show to the user</param>
		/// <returns>If closed normally, returns ErrorResult.OK
		/// If the report button was pressed, returns {resultIfAlternateButtonPressed}.
		/// If the secondary action button was pressed, returns {this.SecondaryActionResult} if that is non-null; otherwise falls back to {resultIfAlternateButtonPressed}
		/// If an exception is throw while executing this function, returns ErrorResult.Abort.
		/// </returns>
		public ErrorResult NotifyUserOfProblem(IRepeatNoticePolicy policy, string alternateButton1Label, ErrorResult resultIfAlternateButtonPressed, string message)
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
				ErrorResult result = ErrorResult.OK;
				if (policy.ShouldShowMessage(message))
				{
					ErrorReport.OnShowDetails = null;
					ProblemReportApi.NotifyMessage = null;
					var reportButtonLabel = GetReportButtonLabel(alternateButton1Label);
					result = ShowNotifyDialog(ProblemLevel.kNonFatal, message, null, reportButtonLabel, resultIfAlternateButtonPressed, this.SecondaryActionButtonLabel, this.SecondaryActionResult);
				}

				ResetToDefaults();

				return result;
			}
			catch (Exception e)
			{
				var fallbackReporter = new WinFormsErrorReporter();
				fallbackReporter.ReportNonFatalException(e, new ShowAlwaysPolicy());

				return ErrorResult.Abort;
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
			ProblemReportApi.ShowProblemDialog(GetControlToUse(), exception, null, ProblemLevel.kNonFatal);
		}

		public void ReportNonFatalExceptionWithMessage(Exception error, string messageFormat, params object[] args)
		{
			var message = String.Format(messageFormat, args);
			ProblemReportApi.ShowProblemDialog(GetControlToUse(), error, message , ProblemLevel.kNonFatal);
		}

		public void ReportNonFatalMessageWithStackTrace(string messageFormat, params object[] args)
		{
			var stackTrace = new StackTrace(true);
			var userLevelMessage = String.Format(messageFormat, args);
			string detailedMessage = FormatMessageWithStackTrace(userLevelMessage, stackTrace);
			ProblemReportApi.ShowProblemDialog(GetControlToUse(), null, detailedMessage, ProblemLevel.kNonFatal, userLevelMessage);
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
			ProblemReportApi.ShowProblemDialog(GetControlToUse(), null, detailedMessage, ProblemLevel.kFatal, userLevelMessage);
			Quit();
		}
		#endregion

		protected Control GetControlToUse()
		{
			return this.Control ?? Form.ActiveForm ?? FatalExceptionHandler.ControlOnUIThread;
		}

		private string FormatMessageWithStackTrace(string message, StackTrace stackTrace)
        {
			return $"Message (not an exception): {message}" + Environment.NewLine
				+ Environment.NewLine
				+ "--Stack--" + Environment.NewLine
				+ stackTrace.ToString();
		}

		private static void Quit() => Process.GetCurrentProcess().Kill();	// Same way WinFormsErrorReporter quits

		protected string GetReportButtonLabel(string labelFromCaller)
		{
			// Note: We use null to indicate Not Set, so it will fall back to labelFromCaller
			// "" is used to indicate that it was explicitly set and the desire is to disable the Report button.
			if (this.ReportButtonLabel != null)
			{
				return this.ReportButtonLabel;
			}
			else if (labelFromCaller != "Details")
			{
				return labelFromCaller;
			}
			else
			{
				return DefaultReportLabel;
			}
		}

		private ErrorResult ShowNotifyDialog(string severity, string messageText, Exception exception,
			string reportButtonLabel, ErrorResult reportPressedResult,
			string secondaryButtonLabel, ErrorResult? secondaryPressedResult)
        {
			// Before we do anything that might be "risky", put the problem in the log.
			ProblemReportApi.LogProblem(exception, messageText, severity);

			ErrorResult returnResult = ErrorResult.OK;

			// ENHANCE: Allow the caller to pass in the control, which would be at the front of this.
			//System.Windows.Forms.Control control = Form.ActiveForm ?? FatalExceptionHandler.ControlOnUIThread;
			var control = GetControlToUse();
			SafeInvoke.InvokeIfPossible("Show Error Reporter", control, false, () =>
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
						var fallbackReporter = new WinFormsErrorReporter();
						if (exception != null)
							fallbackReporter.ReportNonFatalException(exception, new ShowAlwaysPolicy());
						else
						{
							fallbackReporter.NotifyUserOfProblem(new ShowAlwaysPolicy(), null, ErrorResult.OK,
								message.NotEncoded);
						}
						return;
					}

					string urlQueryString = CreateNotifyUrlQueryString(message, reportButtonLabel, secondaryButtonLabel);

					
					// Precondition: we must be on the UI thread for Gecko to work.
					using (var dlg = BrowserDialogFactory.CreateReactDialog("ProblemDialog", urlQueryString))
					{
						dlg.FormBorderStyle = FormBorderStyle.FixedToolWindow;	// Allows the window to be dragged around
						dlg.ControlBox = true;	// Add controls like the X button back to the top bar
						dlg.Text = "";	// Remove the title from the WinForms top bar

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
							if (dlg.CloseSource == "closedByAlternateButton")
							{
								// OnShowDetails will be invoked if this method returns {resultIfAlternateButtonPressed}
								// FYI, setting to null is OK. It should cause ErrorReport to reset to default handler.
								ErrorReport.OnShowDetails = OnSecondaryActionPressed;

								returnResult = secondaryPressedResult ?? reportPressedResult;
							}
							else if (dlg.CloseSource == "closedByReportButton")
							{
								ErrorReport.OnShowDetails = OnReportPressed;
								returnResult = reportPressedResult;
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
							BloomServer.RegisterThreadUnblocked();
						}
					}
				}
				catch (Exception errorReporterException)
				{
					Logger.WriteError("*** HtmlErrorReporter threw an exception trying to display", errorReporterException);
					// At this point our problem reporter has failed for some reason, so we want the old WinForms handler
					// to report both the original error for which we tried to open our dialog and this new one where
					// the dialog itself failed.
					// In order to do that, we create a new exception with the original exception (if there was one) as the
					// inner exception. We include the message of the exception we just caught. Then we call the
					// old WinForms fatal exception report directly.
					// In any case, both of the errors will be logged by now.
					var message = "Bloom's error reporting failed: " + errorReporterException.Message;

					// Fallback to Winforms in case of trouble getting the browser to work
					var fallbackReporter = new WinFormsErrorReporter();
					fallbackReporter.ReportFatalException(new ApplicationException(message, exception ?? errorReporterException));
				}
			});

			return returnResult;
		}

		internal static UrlPathString GetMessage(string detailedMessage, Exception exception)
		{
			string textToReport = !string.IsNullOrEmpty(detailedMessage) ? detailedMessage : exception.Message;
			return UrlPathString.CreateFromUnencodedString(textToReport, true);
		}
				
		/// <summary>
		/// Generates the query component for the URL to bring up the Notify Dialog
		/// (The query component is the optional part after the "?" in the URL)
		/// </summary>
		/// <param name="message">The final message to show the user, as a UrlPathString</param>
		/// <param name="reportButtonLabel">Optional. The localized text for the Report button. Pass null/"" to disable.</param>
		/// <param name="secondaryActionButtonLabel">Optional. The localized text for the Secondary Action button. Pass null/"" to disable</param>
		/// <returns></returns>
		protected static string CreateNotifyUrlQueryString(UrlPathString message, string reportButtonLabel, string secondaryActionButtonLabel)
		{
			var queryComponents = new List<string>();
			queryComponents.Add($"level={ProblemLevel.kNotify}");

			if (!String.IsNullOrEmpty(reportButtonLabel))
			{
				var encodedReportLabel = UrlPathString.CreateFromUnencodedString(reportButtonLabel).UrlEncoded;
				queryComponents.Add($"reportLabel={encodedReportLabel}");
			}

			if (!String.IsNullOrEmpty(secondaryActionButtonLabel))
			{
				var encodedSecondaryLabel = UrlPathString.CreateFromUnencodedString(secondaryActionButtonLabel).UrlEncoded;
				queryComponents.Add($"secondaryLabel={encodedSecondaryLabel}");
			}

			var query = String.Join("&", queryComponents);
			// Prefer putting the message in the URL parameters, so it can just be a simple one-and-done GET request.
			//   (IMO, this makes debugging easier and simplifies the rendering process).
			// But very long URL's cause our BrowserDialog problems.
			// Although there are suggestions that Firefox based browsers could have URL's about 60k in length,
			// we'll just stick to <2k because that was recommended as a length with basically universal support across browser platforms
			string encodedMessage = message.UrlEncoded;
			if (query.Length + encodedMessage.Length < 2048)
			{
				query += $"&msg={encodedMessage}";
			}
			else
			{
				ProblemReportApi.NotifyMessage = message.NotEncoded;
			}

			return query;
		}

		public static void OnReportPressed(Exception error, string message)
		{
			ErrorReport.ReportNonFatalExceptionWithMessage(error, message);
		}
	}
}
