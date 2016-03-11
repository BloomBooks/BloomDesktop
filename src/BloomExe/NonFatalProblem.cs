using System;
using System.Collections.Generic;
using System.Linq;
using SIL.Reporting;

namespace Bloom
{
	// NB: these must have the exactly the same symbols
	public enum ModalIf { Alpha, Beta, All }
	public enum PassiveIf { Alpha, Beta, All }

	/// <summary>
	/// Provides a way to note a problem in the log and, depending on channel, notify the user.
	/// Enhance: wire up to a passive notification "toast" in the UI
	/// </summary>
	public class NonFatalProblem
	{
		/// <summary>
		/// Always log, possibly inform the user, possibly throw the exception
		/// </summary>
		/// <param name="modalThreshold">Will show a modal dialog if the channel is this or lower</param>
		/// <param name="passiveThreshold">Ignored for now</param>
		/// <param name="shortUserLevelMessage">Should make sense in a small toast notification</param>
		/// <param name="exception"></param>
		public static void Report(ModalIf modalThreshold, PassiveIf passiveThreshold, string shortUserLevelMessage = null,
			Exception exception = null)
		{
			shortUserLevelMessage = shortUserLevelMessage == null ? "" : shortUserLevelMessage;
			Logger.WriteError("NonFatalProblem: " + shortUserLevelMessage, exception);

			if(modalThreshold == ModalIf.Alpha)
			{
				shortUserLevelMessage = "[Dev/Alpha channel only]: "+shortUserLevelMessage;
			}

			var channel = ApplicationUpdateSupport.ChannelName.ToLower();

			if(exception != null && Matches(modalThreshold).Any(s => channel.Contains(s)))
			{
				SIL.Reporting.ErrorReport.ReportNonFatalExceptionWithMessage(exception, shortUserLevelMessage);
				return;
			}

			//just convert from InformIf to ThrowIf so that we don't have to duplicate code
			var passive = (ModalIf) ModalIf.Parse(typeof(ModalIf), passiveThreshold.ToString());
			if (!string.IsNullOrEmpty(shortUserLevelMessage)  && Matches(passive).Any(s => channel.Contains(s)))
			{
				//Future
			}
		}

		private static IEnumerable<string> Matches(ModalIf threshold)
		{
			switch (threshold)
			{
				case ModalIf.All:
					return new string[] {"" /*will match anything*/};
				case ModalIf.Beta:
					return new string[] { "developer", "alpha", "beta" };
				case ModalIf.Alpha:
					return new string[] { "developer", "alpha" };
				default:
					return new string[] {};
			}
		}
	}
}
