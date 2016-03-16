using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
#if !__MonoCS__
using System.Windows.Media;
#endif
using Bloom.MiscUI;
using SIL.Reporting;

namespace Bloom
{
	// NB: these must have the exactly the same symbols
	public enum ModalIf { Alpha, Beta, All }
	public enum PassiveIf { Alpha, Beta, All }

	/// <summary>
	/// Provides a way to note a problem in the log and, depending on channel, notify the user.
	/// </summary>
	public class NonFatalProblem
	{
		/// <summary>
		/// Always log, possibly inform the user, possibly throw the exception
		/// </summary>
		/// <param name="modalThreshold">Will show a modal dialog if the channel is this or lower</param>
		/// <param name="passiveThreshold">Ignored for now</param>
		/// <param name="shortUserLevelMessage">Simple message that fits in small toast notification</param>
		/// <param name="moreDetails">Info adds information about the problem, which we get if they report the problem</param>
		/// <param name="exception"></param>
		public static void Report(ModalIf modalThreshold, PassiveIf passiveThreshold, string shortUserLevelMessage = null,
			string moreDetails = null,
			Exception exception = null)
		{
			shortUserLevelMessage = shortUserLevelMessage == null ? "" : shortUserLevelMessage;
			var fullDetailedMessage = shortUserLevelMessage;
			if(!string.IsNullOrEmpty(moreDetails))
				fullDetailedMessage = fullDetailedMessage + System.Environment.NewLine+ moreDetails;

			if (exception == null)
			{
				try
				{
					throw new ApplicationException("Not actually an exception, just a message.");
				}
				catch (Exception errorToGetStackTrace)
				{
					exception = errorToGetStackTrace;
				}
			}

			Logger.WriteError("NonFatalProblem: " + fullDetailedMessage, exception);

			if (modalThreshold == ModalIf.Alpha)
			{
				shortUserLevelMessage = "[Alpha]: " + shortUserLevelMessage;
			}

			var channel = ApplicationUpdateSupport.ChannelName.ToLower();

			if (Matches(modalThreshold).Any(s => channel.Contains(s)))
			{
				try
				{
					SIL.Reporting.ErrorReport.ReportNonFatalExceptionWithMessage(exception, fullDetailedMessage);
				}
				catch (Exception)
				{
					//if we're running when the UI is already shut down, the above is going to throw.
					//At least if we're running in a debugger, we'll stop here:
					throw new ApplicationException(fullDetailedMessage + "Error trying to report normally.");
				}
				return;
			}

			//just convert from InformIf to ThrowIf so that we don't have to duplicate code
			var passive = (ModalIf)ModalIf.Parse(typeof(ModalIf), passiveThreshold.ToString());
			if (!string.IsNullOrEmpty(shortUserLevelMessage) && Matches(passive).Any(s => channel.Contains(s)))
			{
				var toast = new ToastNotifier();
				toast.ToastClicked += (s, e) => { SIL.Reporting.ErrorReport.ReportNonFatalExceptionWithMessage(exception, fullDetailedMessage); };
				toast.Image.Image = ToastNotifier.WarningBitmap;
				toast.Show(shortUserLevelMessage, "Report", 5);
			}
		}

		private static IEnumerable<string> Matches(ModalIf threshold)
		{
			switch (threshold)
			{
				case ModalIf.All:
					return new string[] { "" /*will match anything*/};
				case ModalIf.Beta:
					return new string[] { "developer", "alpha", "beta" };
				case ModalIf.Alpha:
					return new string[] { "developer", "alpha" };
				default:
					return new string[] { };
			}
		}
	}
}
