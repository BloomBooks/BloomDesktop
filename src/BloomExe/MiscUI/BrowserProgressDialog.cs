using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.web;

namespace Bloom.MiscUI
{
	/// <summary>
	/// This class supports performing a task in the background while a progress dialog
	/// runs in the foreground. (This allows the UI thread to handle updating the dialog.)
	/// The doWhat action performs the task. If it returns true, the progress messages include
	/// something the user should respond to; buttons will be shown and the user must click
	/// one. Otherwise, the progress dialog simply closes when done.
	/// </summary>
	/// <remarks>The action is performed in the background not so that the main program
	/// can keep going...it can't, DoWorkWithProgressDialog does not return until the action
	/// is complete...but so that the UI thread can run the progress dialog,
	/// responding to paint events, a click on the copy button,etc.</remarks>
	public class BrowserProgressDialog
	{
		public static void DoWorkWithProgressDialog(BloomWebSocketServer socketServer, string socketContext, string title,
			Func<IWebSocketProgress, bool> doWhat)
		{
			var progress = new WebSocketProgress(socketServer, socketContext);

			// NOTE: This (specifically ShowDialog) blocks the main thread until the dialog is closed.
			// Be careful to avoid deadlocks.
			using (var dlg = new ReactDialog("teamCollectionSettingsBundle.js",
				"ProgressDialog", $"title={title}"))
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
					var problems = doWhat(progress);
					if (problems)
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
