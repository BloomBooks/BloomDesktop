using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
#if !__MonoCS__
using System.Windows.Media;
#endif
using Bloom.MiscUI;
using DesktopAnalytics;
using SIL.Reporting;

namespace Bloom
{
	// NB: these must have the exactly the same symbols
	public enum ModalIf { None, Alpha, Beta, All }
	public enum PassiveIf { None, Alpha, Beta, All }

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
			// Simplify some checks below by tweaking the channel name on Linux.
			var channel = ApplicationUpdateSupport.ChannelName.ToLowerInvariant();
			if (channel.EndsWith("-unstable"))
				channel = channel.Replace("unstable", "alpha");
			try
			{
				shortUserLevelMessage = shortUserLevelMessage == null ? "" : shortUserLevelMessage;
				var fullDetailedMessage = shortUserLevelMessage;
				if(!string.IsNullOrEmpty(moreDetails))
					fullDetailedMessage = fullDetailedMessage + System.Environment.NewLine + moreDetails;

				if(exception == null)
				{
					//the code below is simpler if we always have an exception, even this thing that gives
					//us the stacktrace we would otherwise be missing. Note, you might be tempted to throw
					//and then catch an exception instead, but for some reason the resulting stack trace
					//would contain only this method.
					exception = new ApplicationException(new StackTrace().ToString());
				}

				if(Program.RunningUnitTests) 
				{
					//It's not clear to me what we can do that works for all unit test scenarios...
					//We can imagine those for which throwing an exception at this point would be helpful,
					//but there are others in which say, not finding a file is expected. Either way,
					//the rest of the test should fail if the problem is real, so doing anything here
					//would just be a help, not really necessary for getting the test to fail. 
					//So, for now I'm going to just go with doing nothing.
					return;
				}

				//if this isn't going modal even for devs, it's just background noise and we don't want the 
				//thousands of exceptions we were getting as with BL-3280
				if (modalThreshold != ModalIf.None)
				{
					Analytics.ReportException(exception);
				}

				Logger.WriteError("NonFatalProblem: " + fullDetailedMessage, exception);

				if(Matches(modalThreshold).Any(s => channel.Contains(s)))
				{
					try
					{
						SIL.Reporting.ErrorReport.ReportNonFatalExceptionWithMessage(exception, fullDetailedMessage);
					}
					catch(Exception)
					{
						//if we're running when the UI is already shut down, the above is going to throw.
						//At least if we're running in a debugger, we'll stop here:
						throw new ApplicationException(fullDetailedMessage + "Error trying to report normally.");
					}
					return;
				}

				//just convert from PassiveIf to ModalIf so that we don't have to duplicate code
				var passive = (ModalIf) ModalIf.Parse(typeof(ModalIf), passiveThreshold.ToString());
				if(!string.IsNullOrEmpty(shortUserLevelMessage) && Matches(passive).Any(s => channel.Contains(s)))
				{
					ShowToast(shortUserLevelMessage, exception, fullDetailedMessage);
				}
			}
			catch(Exception errorWhileReporting)
			{
				// Don't annoy developers for expected error if the internet is not available.
				if (errorWhileReporting.Message.StartsWith("Bloom could not retrieve the URL") && Bloom.web.UrlLookup.FastInternetAvailable)
				{
					Debug.Fail("error in nonfatalError reporting");
				}
				if (channel.Contains("alpha"))
					ErrorReport.NotifyUserOfProblem(errorWhileReporting,"Error while reporting non fatal error");
			}
		}

		private static void ShowToast(string shortUserLevelMessage, Exception exception, string fullDetailedMessage)
		{
			var formForSynchronizing = Application.OpenForms.Cast<Form>().Last();
			if (formForSynchronizing.InvokeRequired)
			{
				formForSynchronizing.BeginInvoke(new Action(() =>
				{
					ShowToast(shortUserLevelMessage, exception, fullDetailedMessage);
				}));
				return;
			}
			var toast = new ToastNotifier();
			toast.ToastClicked +=
				(s, e) => { SIL.Reporting.ErrorReport.ReportNonFatalExceptionWithMessage(exception, fullDetailedMessage); };
			toast.Image.Image = ToastNotifier.WarningBitmap;
			toast.Show(shortUserLevelMessage, "Report", 5);
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
