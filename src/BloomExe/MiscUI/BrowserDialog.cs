using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.web;
using SIL.Reporting;

namespace Bloom.MiscUI
{
	public partial class BrowserDialog : Form
	{
		private Browser _browser;
		private Boolean _hidden;

		// This applies only to cases where the dialog is created but not shown (hidden is true)
		// to execute some Javascript.
		// When the javascript sends the close notification, this action gets executed.
		private Action _whenClosed;

		public static BrowserDialog CurrentDialog;

		private static List<BrowserDialog> _activeDialogs = new List<BrowserDialog>();
		public IBloomWebSocketServer WebSocketServer { get; set; }
		private const string kWebsocketContext = "dialog";

		// called by BrowserDialogApi.Close()
		public static void CloseDialog()
		{
			if (CurrentDialog !=null)
			{
				CurrentDialog.CloseMessage = null; // close is coming from JS, don't need to notify it, and must avoid loop
				if (CurrentDialog._hidden)
				{
					CurrentDialog._whenClosed?.Invoke();
					CurrentDialog._browser.Dispose();
					CurrentDialog.Dispose();
				}
				else
				{
					//try
					//{
					//	// This was one of the things I tried to prevent a weird GeckoFx crash while
					//	// closing the FirebaseLoginDialog. By itself it didn't help,
					//	CurrentDialog.Controls.Remove(CurrentDialog._browser);
					//	CurrentDialog._browser.Dispose();
					//}
					//catch (Exception ex)
					//{
					//	Logger.WriteError(ex);
					//}

					try
					{
						CurrentDialog.Invoke((Action) (() => CurrentDialog.Close()));
					}
					catch (Exception ex)
					{
						Logger.WriteError(ex);
					}

					// caller will dispose of the dialog itself.
				}
			}
		}

		// If this has a value, user attempts to close the dialog will send this message
		// to Javascript instead. This is useful when there is essential cleanup to do
		// in the JS world (for example, Bloom will crash completely if we omit some
		// cleanup in the Login dialog).
		public string CloseMessage { get; set; }

		/// <summary>
		/// Create a dialog whose entire content is a GeckoFx control displaying the specified URL.
		/// Once the browser has navigated to that URL, add it to the window's Controls.
		/// Typically the caller will call ShowDialog, and dispose when it gets closed.
		/// If "hidden" is set to true, the dialog is NOT shown. This is useful when we need to
		/// do something in a browser without any UI, like implement the Logout command in the
		/// BloomLibraryUploadControl.
		/// In the normal case where the dialog is shown, it is up to the caller to dispose of it when it is closed.
		/// When hidden, it gets disposed in the CloseDialog code (since the caller would typically have
		/// no way to know when whatever we wanted to happen in the browser is finished).
		/// </summary>
		public BrowserDialog(string url, bool hidden = false, Action whenClosed = null)
		{
			InitializeComponent();
			FormClosing += BrowserDialog_FormClosing;
			_hidden = hidden;
			_whenClosed = whenClosed;
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow; // draggable
			this.Text = ""; // don't show the title, we do that in the html

			// The Size setting is needed on Linux to keep the browser from coming up as a small
			// rectangle in the upper left corner...
			_browser = new Browser { Dock = DockStyle.Fill, Location = new Point(3, 3), Size = new Size(this.Width - 6, this.Height - 6) };
			_browser.BackColor = Color.White;

			var dummy = _browser.Handle; // gets the WebBrowser created
			_browser.WebBrowser.DocumentCompleted += (sender, args) =>
			{
				if (!hidden)
				{
					// If the control gets added to the tab before it has navigated somewhere,
					// it shows as solid black, despite setting the BackColor to white.
					// So just don't show it at all until it contains what we want to see.
					this.Controls.Add(_browser);
				}
			};
			_browser.Navigate(url, false);
			_browser.Focus();
			CurrentDialog = this;
			_activeDialogs.Add(this);
		}

		private void BrowserDialog_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (CloseMessage != null)
			{
				e.Cancel = true;
				WebSocketServer.SendString(kWebsocketContext, "close", CloseMessage);
				return;
			}

			_activeDialogs.Remove(this);
			if (_activeDialogs.Count > 0)
			{
				CurrentDialog = _activeDialogs[_activeDialogs.Count - 1];
			}
			else
			{
				CurrentDialog = null;
			}
		}
	}
}
