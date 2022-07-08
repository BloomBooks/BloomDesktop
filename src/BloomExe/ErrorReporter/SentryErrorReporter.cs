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

		public void NotifyUserOfProblem(IRepeatNoticePolicy policy, Exception exception, string message)
			=> NotifyUserOfProblem(message, exception, null, policy);

		public ErrorResult NotifyUserOfProblem(IRepeatNoticePolicy policy, string alternateButton1Label,
			ErrorResult resultIfAlternateButtonPressed, string message)
		{
			NotifyUserOfProblem(message, null, null, policy);
			return ErrorResult.OK;
		}

		public void NotifyUserOfProblem(string message, NotifyUserOfProblemSettings settings, IRepeatNoticePolicy policy)
			=> NotifyUserOfProblem(message, null, settings, policy);

		public void NotifyUserOfProblem(string message, Exception exception, NotifyUserOfProblemSettings settings, IRepeatNoticePolicy policy)
		{
			NonFatalProblem.ReportSentryOnly(exception, message);
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
