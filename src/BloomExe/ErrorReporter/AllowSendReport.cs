using System;

namespace Bloom.ErrorReporter
{
	public enum AllowSendReport
	{
		AllowIfException = 0,	// Default
		Allow = 1,
		Disallow = 2,
	}

	public static class AllowSendReportExtensions
	{
		/// <summary>
		/// Determines if sending a report should be allowed, given the context of the situation
		/// </summary>
		/// <param name="allowSendReport">The option to check</param>
		/// <param name="context">The context of the situation, such as whether any exceptions will be included with the error report</param>
		/// <returns>True if sending error reports is allowed, given the context. False otherwise.</returns>
		public static bool IsSendReportAllowed(this AllowSendReport allowSendReport, AllowSendReportContext context)
		{
			switch (allowSendReport)
			{
				case AllowSendReport.Disallow:
					return false;
				case AllowSendReport.Allow:
					return true;
				case AllowSendReport.AllowIfException:
					return context?.ExceptionIncluded == true;
				default:
					throw new NotImplementedException($"Unrecognized option {nameof(allowSendReport)} passed to IsSendReportAllowed");
			}
		}
	}

	/// <summary>
	/// The context of the situation in determining whether sending error reports are allowed, such as whether any exceptions will be included with the error report
	/// </summary>
	public class AllowSendReportContext
	{
		public bool ExceptionIncluded { get; set; }

		public AllowSendReportContext(Exception exception)
		{
			this.ExceptionIncluded = exception != null;
		}
	}

}
