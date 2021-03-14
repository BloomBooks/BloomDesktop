using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Sentry;
using SIL.Reporting;

namespace Bloom
{
	/// <summary>
	/// An ErrorReporter that just forwards the errors to Sentry
	/// Note: This is largely derived from the old NotifyUserOfProblemLogger, but with the forwarding to WinFormsErrorReporter removed.
	/// </summary>
	public class SentryErrorReporter: IErrorReporter
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
		}

		public ErrorResult NotifyUserOfProblem(IRepeatNoticePolicy policy, string alternateButton1Label,
			ErrorResult resultIfAlternateButtonPressed, string message)
		{
			CaptureEvent(message);
			return ErrorResult.OK;
		}

		public void ReportNonFatalException(Exception exception, IRepeatNoticePolicy policy)
		{
			CaptureException(exception);
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
		}

		public void ReportNonFatalMessageWithStackTrace(string message, params object[] args)
		{
			CaptureEvent(Format(message, args));
		}

		public void ReportFatalMessageWithStackTrace(string message, object[] args)
		{
			CaptureEvent(Format(message, args));
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
