using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sentry;
using Sentry.Protocol;
using SIL.Reporting;
using SIL.Windows.Forms.Reporting;

namespace Bloom
{
	/// <summary>
	/// An instance of this class is configured as the error reporter for all of LibPalaso.
	/// As a result, almost all serious errors both in our own code and in libpalaso come to one
	/// of these methods. Currently, they just allow Sentry to capture the problem and then
	/// forward it to the LibPalaso standard error handler. Eventually (BL-9102) we hope to have
	/// our own dialog in HTML.
	/// </summary>
	public class NotifyUserOfProblemLogger: IErrorReporter
	{
		private WinFormsErrorReporter _reporter;
		public NotifyUserOfProblemLogger()
		{
			_reporter = new WinFormsErrorReporter();
		}

		private void CaptureException(Exception e)
		{
			try
			{
				SentrySdk.CaptureException(e);
			}
			catch (Exception err)
			{
				// Will only "do something" if we're testing reporting and have thus turned off checking for dev.
				// Else we're swallowing.
				Debug.Fail(err.Message);
			}
		}

		private void CaptureEvent(string message)
		{
			try
			{
				var evt = new SentryEvent() {Message = message};
				evt.SetExtra("stackTrace", (new StackTrace()).ToString());
				SentrySdk.CaptureEvent(evt);
			}
			catch (Exception err)
			{
				// Will only "do something" if we're testing reporting and have thus turned off checking for dev.
				// Else we're swallowing.
				Debug.Fail(err.Message);
			}
		}

		public void ReportFatalException(Exception e)
		{
			CaptureException(e);
			_reporter.ReportFatalException(e);
		}

		public ErrorResult NotifyUserOfProblem(IRepeatNoticePolicy policy, string alternateButton1Label,
			ErrorResult resultIfAlternateButtonPressed, string message)
		{
			CaptureEvent(message);
			return _reporter.NotifyUserOfProblem(policy, alternateButton1Label, resultIfAlternateButtonPressed, message);
		}

		public void ReportNonFatalException(Exception exception, IRepeatNoticePolicy policy)
		{
			CaptureException(exception);
			_reporter.ReportNonFatalException(exception, policy);
		}

		public void ReportNonFatalExceptionWithMessage(Exception error, string message, params object[] args)
		{
			try
			{
				// similar to Sentry code in NonFatalProblem.Report().
				SentrySdk.WithScope(scope =>
				{
					scope.SetTag("fullDetailedMessage", message);
					SentrySdk.CaptureException(error);
				});
			}
			catch (Exception err)
			{
				// Will only "do something" if we're testing reporting and have thus turned off checking for dev.
				// Else we're swallowing.
				Debug.Fail(err.Message);
			}

			_reporter.ReportNonFatalExceptionWithMessage(error, message, args);
		}

		public void ReportNonFatalMessageWithStackTrace(string message, params object[] args)
		{
			CaptureEvent(Format(message, args));
			_reporter.ReportNonFatalMessageWithStackTrace(message, args);
		}

		public void ReportFatalMessageWithStackTrace(string message, object[] args)
		{
			CaptureEvent(Format(message, args));
			_reporter.ReportFatalMessageWithStackTrace(message, args);
		}

		string Format(string message, object[] args)
		{
			try
			{
				return string.Format(message, args);
			}
			catch (Exception)
			{
				return message;
			}
		}
	}
}
