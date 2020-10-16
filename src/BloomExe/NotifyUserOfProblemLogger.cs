using System;
using System.Collections.Generic;
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
		public void ReportFatalException(Exception e)
		{
			SentrySdk.CaptureException(e);
			_reporter.ReportFatalException(e);
		}

		public ErrorResult NotifyUserOfProblem(IRepeatNoticePolicy policy, string alternateButton1Label,
			ErrorResult resultIfAlternateButtonPressed, string message)
		{
			SentrySdk.CaptureMessage(message, SentryLevel.Warning); // level is a guess, but these are usually more serious than 'info'.
			return _reporter.NotifyUserOfProblem(policy, alternateButton1Label, resultIfAlternateButtonPressed, message);
		}

		public void ReportNonFatalException(Exception exception, IRepeatNoticePolicy policy)
		{
			SentrySdk.CaptureException(exception);
			_reporter.ReportNonFatalException(exception, policy);
		}

		public void ReportNonFatalExceptionWithMessage(Exception error, string message, params object[] args)
		{
			// Review: should we capture the message as a separate, additional sentry event,
			// or build a new exception with message and error as inner exception,
			// or just capture the message, or is this enough?
			SentrySdk.CaptureException(error);
			_reporter.ReportNonFatalExceptionWithMessage(error, message, args);
		}

		public void ReportNonFatalMessageWithStackTrace(string message, params object[] args)
		{
			SentrySdk.CaptureMessage(Format(message, args), SentryLevel.Warning);
			throw new NotImplementedException();
		}

		public void ReportFatalMessageWithStackTrace(string message, object[] args)
		{
			SentrySdk.CaptureMessage(Format(message, args), SentryLevel.Fatal);
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
