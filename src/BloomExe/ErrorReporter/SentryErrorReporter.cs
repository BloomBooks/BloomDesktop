using System;
using SIL.Reporting;

namespace Bloom.ErrorReporter
{
	/// <summary>
	/// An ErrorReporter that just forwards the errors to Sentry
	/// Note: This is largely derived from the old NotifyUserOfProblemLogger, but with the forwarding to WinFormsErrorReporter removed.
	/// </summary>
	public class SentryErrorReporter: IBloomErrorReporter
	{
		private SentryErrorReporter()
		{
		}

		private static SentryErrorReporter _instance;
		public static SentryErrorReporter Instance
		{
			get
			{
				if (_instance == null)
				{
					_instance = new SentryErrorReporter();
				}
				return _instance;
			}
		}

		public void ReportFatalException(Exception e)
		{
			NonFatalProblem.ReportSentryOnly(e);
		}

		public void NotifyUserOfProblem(IRepeatNoticePolicy policy, Exception error, string message)
		{
			NonFatalProblem.ReportSentryOnly(error, message);
		}

		public ErrorResult NotifyUserOfProblem(IRepeatNoticePolicy policy, string alternateButton1Label,
			ErrorResult resultIfAlternateButtonPressed, string message)
		{
			NonFatalProblem.ReportSentryOnly(message);
			return ErrorResult.OK;
		}

		public void SetNotifyUserOfProblemCustomParams(string reportButtonLabel, Action<Exception, string> onReportButtonPressed, string extraButtonLabel, Action<Exception, string> onExtraButtonPressed)
		{
			// No need for this class to do anything when this function is called
		}

		public void ReportNonFatalException(Exception exception, IRepeatNoticePolicy policy)
		{
			NonFatalProblem.ReportSentryOnly(exception);
		}

		public void ReportNonFatalExceptionWithMessage(Exception error, string message, params object[] args)
		{
			NonFatalProblem.ReportSentryOnly(error, message);
		}

		public void ReportNonFatalMessageWithStackTrace(string message, params object[] args)
		{
			NonFatalProblem.ReportSentryOnly(Format(message, args));
		}

		public void ReportFatalMessageWithStackTrace(string message, object[] args)
		{
			NonFatalProblem.ReportSentryOnly(Format(message, args));
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
