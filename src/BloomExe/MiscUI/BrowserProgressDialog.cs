using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.web;

namespace Bloom.MiscUI
{
	/// <summary>
	/// This class supports performing a task in the background while a progress dialog
	/// runs in the foreground. (This allows the UI thread to handle updating the dialog.)
	/// The doWhat action does the work and sends progress messages to the dialog.
	/// (A copy button is displayed while doWhat() is in progress, allowing the messages to
	/// be captured.)
	/// DoWhat() returns a boolean:
	/// - false: if doWhenMainActionFalse is provided, it is executed (on the UI thread); normally it
	/// should eventually close the dialog. Otherwise, the dialog closes and the UI thread
	/// resumes as soon as doWhat() returns false.
	/// - true: when doWhat returns this value, the dialog displays a Close button
	/// and a Report button. The Close button allows the user to close the dialog
	/// after studying the the progress messages until satisfied; the UI thread resumes
	/// after this is clicked. The Report button allows the user to send a report
	/// to the Bloom team if the progress messages are unexpected. Again, the code
	/// that initiated the doWhat() task resumes when the report is complete.
	/// </summary>
	/// <remarks>The action is performed in the background not so that the main program
	/// can keep going...it can't, DoWorkWithProgressDialog does not return until doWhat()
	/// is complete...but so that the UI thread can run the progress dialog,
	/// responding to paint events, a click on the buttons, and and so forth.</remarks>
	public class BrowserProgressDialog
	{
		public static void DoWorkWithProgressDialog(BloomWebSocketServer socketServer, string socketContext, string title,
			Func<IWebSocketProgress, bool> doWhat, Action<Form> doWhenMainActionFalse = null)
		{
			var progress = new WebSocketProgress(socketServer, socketContext);

			// NOTE: This (specifically ShowDialog) blocks the main thread until the dialog is closed.
			// Be careful to avoid deadlocks.
			using (var dlg = new ReactDialog("ProgressDialog", $"title={title}"))
			{
				dlg.Width = 500;
				dlg.Height = 300;
				// For now let's not try to handle letting the user abort.
				dlg.ControlBox = false;
				var worker = new BackgroundWorker();
				worker.DoWork += (sender, args) =>
				{
					// A way of waiting until the dialog is ready to receive progress messages
					while (!socketServer.IsSocketOpen(socketContext))
						Thread.Sleep(50);
					var waitForUserToCloseDialogOrReportProblems = doWhat(progress);
					if (waitForUserToCloseDialogOrReportProblems)
					{
						// Now the user is allowed to close the dialog or report problems.
						// (IndependentProgressDialog in JS-land is watching for this message, which causes it to turn
						// on the buttons that allow the dialog to be manually closed (or a problem to be reported).
						socketServer.SendBundle(socketContext, "show-buttons", new DynamicJson());
					}
					else
					{
						// Just close the dialog
						dlg.Invoke((Action)(() =>
						{
							if (doWhenMainActionFalse != null)
								doWhenMainActionFalse(dlg);
							else
								dlg.Close();
						}));
					}
				};

				worker.RunWorkerAsync();
				dlg.ShowDialog(); // returns when dialog closed
			}
		}
	}
}
