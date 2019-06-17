using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Bloom.MiscUI
{
	public partial class BrowserDialog : Form
	{
		private Browser _browser;

		// called by BrowserDialogApi.Close()
		public static void CloseDialog()
		{
			if (CurrentDialog !=null)
			{
				CurrentDialog.Close();
				// caller will dispose
				CurrentDialog = null;
			}
		}

		public static Form CurrentDialog;

		public BrowserDialog(string url)
		{
			InitializeComponent();
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;

			//			if (_browser != null)
			//				return; // Seems to help performance.

			// The Size setting is needed on Linux to keep the browser from coming up as a small
			// rectangle in the upper left corner...
			_browser = new Browser { Dock = DockStyle.Fill, Location = new Point(3, 3), Size = new Size(this.Width - 6, this.Height - 6) };
			_browser.BackColor = Color.White;

			//var rootFile = BloomFileLocator.GetBrowserFile(false, "collection", "enterpriseSettings.html");
			var dummy = _browser.Handle; // gets the WebBrowser created
			_browser.WebBrowser.DocumentCompleted += (sender, args) =>
			{
				// If the control gets added to the tab before it has navigated somewhere,
				// it shows as solid black, despite setting the BackColor to white.
				// So just don't show it at all until it contains what we want to see.
				this.Controls.Add(_browser);
			};
			///			_browser.Navigate(rootFile.ToLocalhost(), false);
			_browser.Navigate(url, false);
			_browser.Focus();
			CurrentDialog = this;
		}
	}
}
