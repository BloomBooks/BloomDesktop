// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Bloom.Properties;
using Gecko;
using Gecko.Interop;
using Palaso.IO;

namespace Bloom.Publish
{
	/// <summary>
	/// Wrapper class that wraps either a Gecko web browser that displays the PDF file through
	/// pdf.js, or the Adobe Reader control. Which one is used depends on the operating system
	/// (Linux always uses pdf.js) and the UseAdobePdfViewer setting in the config file.
	/// </summary>
	public partial class PdfViewer : UserControl, nsIWebProgressListener, nsISupportsWeakReference
	{
		private Control _pdfViewerControl;
		private Timer _pauseTimer;
		private FormWindowState _savedState;
		private const ulong STATE_STOP = 0x00000010;
		private bool _printing;
		public event EventHandler<PdfPrintProgressEventArgs> PrintProgress;
		//private PdfPrintProgressListener _listener;
		public PdfViewer()
		{
			InitializeComponent();

#if !__MonoCS__
			if (Settings.Default.UseAdobePdfViewer)
			{
				_pdfViewerControl = new AdobeReaderControl();
			}
			else
#endif
			{
				_pdfViewerControl = new GeckoWebBrowser();

				// BL-752: The zoom drop down list does not display on Linux
				((GeckoWebBrowser)_pdfViewerControl).DomClick += 
					(sender, e) => ((GeckoWebBrowser)_pdfViewerControl).WebBrowserFocus.Activate();
			}
			SuspendLayout();

			_pdfViewerControl.BackColor = Color.White;
			_pdfViewerControl.Dock = DockStyle.Fill;
			_pdfViewerControl.Name = "_pdfViewerControl";
			_pdfViewerControl.TabIndex = 0;
			AutoScaleDimensions = new SizeF(6F, 13F);
			AutoScaleMode = AutoScaleMode.Font;
			Controls.Add(_pdfViewerControl);
			Name = "PdfViewer";
			ResumeLayout(false);
		}

		public bool ShowPdf(string pdfFile)
		{
#if !__MonoCS__
			if (Settings.Default.UseAdobePdfViewer)
				return ((AdobeReaderControl)_pdfViewerControl).ShowPdf(pdfFile);
#endif

			var url = string.Format("{0}{1}?file=/bloom/{2}", Bloom.web.ServerBase.PathEndingInSlash,
				FileLocator.GetFileDistributedWithApplication("pdf/web/viewer.html"), pdfFile);

			var browser = ((GeckoWebBrowser)_pdfViewerControl);
			browser.Navigate(url);
			browser.DocumentCompleted += (sender, args) =>
			{
				// We want to suppress several of the buttons that the control normally shows.
				// It's nice if we don't have to modify the html and related files, because they are unzipped from a package we install
				// from a source I'm not sure we control, and installed into a directory we can't modify at runtime.
				// A workaround is to tweak the stylesheet to hide them. The actual buttons (and two menu items) are easily
				// hidden by ID.
				// Unfortunately we're getting rid of a complete group in the pull-down menu, which leaves an ugly pair of
				// adjacent separators. And the separators don't have IDs so we can't easily select just one to hide.
				// Fortunately there are no other divs in the parent (besides the separator) so we just hide the second one.
				// This is unfortunately rather fragile and may not do exactly what we want if the viewer.html file
				// defining the pdfjs viewer changes.
				GeckoStyleSheet stylesheet = browser.Document.StyleSheets.First();
				stylesheet.CssRules.Add("#toolbarViewerRight, #viewOutline, #viewAttachments, #viewThumbnail, #viewFind {display: none}");
				stylesheet.CssRules.Add("#previous, #next, #pageNumberLabel, #pageNumber, #numPages {display: none}");
				stylesheet.CssRules.Add("#toolbarViewerLeft .splitToolbarButtonSeparator {display: none}");
			};
			return true;
		}

		public void Print()
		{
#if !__MonoCS__
			if (Settings.Default.UseAdobePdfViewer)
			{
				((AdobeReaderControl)_pdfViewerControl).Print();
				return;
			}

			var browser = ((GeckoWebBrowser)_pdfViewerControl);
			using (AutoJSContext context = new AutoJSContext(browser.Window.JSContext))
			{
				string result;
				context.EvaluateScript(@"window.print()", (nsISupports)browser.Document.DomObject, out result);
			}
#else
			// BL-788 Print dialog appears behind Bloom on Linux
			// Finally went to minimizing Bloom to allow the print window to be
			// displayed and then restore to the original size after the
			// Print or Cancel button is pushed on the print dialog.
			_pauseTimer = new Timer();
			_pauseTimer.Interval = 250;
			_pauseTimer.Tick += PrintAfterPause;

			_savedState = this.ParentForm.WindowState;
			this.ParentForm.WindowState = FormWindowState.Minimized;
			_pauseTimer.Start();

#endif
		}
		private void PrintAfterPause(object sender, EventArgs e)
		{
			_pauseTimer.Stop ();
			BrowserPrint ();
			_pauseTimer = new Timer();
			_pauseTimer.Interval = 250;
			_pauseTimer.Tick += RestoreAfterPrint;
			_pauseTimer.Start();
		}
		private void RestoreAfterPrint(object sender, EventArgs e)
		{
			_pauseTimer.Stop ();
			this.ParentForm.WindowState = _savedState;
		}

		public void BrowserPrint()
		{
			var browser = ((GeckoWebBrowser)_pdfViewerControl);
			using (AutoJSContext context = new AutoJSContext (browser.Window.JSContext)) {
				nsIDOMWindow domWindow = browser.Window.DomWindow;
				nsIWebBrowserPrint print = Xpcom.QueryInterface<nsIWebBrowserPrint> (domWindow);

				try {
					if (PrintProgress != null)
					{
						// Send event to disable print, simple, outside cover and inside buttons
						// while printing
						PrintProgress.Invoke (this, new PdfPrintProgressEventArgs(true));
					}
					_printing = true;
					print.Print (null, this);
				} catch (COMException e) {
					if (PrintProgress != null) {
						PrintProgress.Invoke (this, new PdfPrintProgressEventArgs(false));
					}
					//NS_ERROR_ABORT means user cancelled the printing, not really an error.
					if (e.ErrorCode != GeckoError.NS_ERROR_ABORT)
						Console.WriteLine ("ComError");
				}
				Marshal.ReleaseComObject (print);
			}
		}

		#region nsISupportsWeakReference Members
		public nsIWeakReference GetWeakReference()
		{
			return new nsWeakReference( this );
		}
		#endregion

		#region nsIWebProgressListener Members
		public void OnStateChange(nsIWebProgress aWebProgress, nsIRequest aRequest, uint aStateFlags, int aStatus)
		{
			if (_printing && ((aStateFlags & STATE_STOP) != 0)) {
				_printing = false;
				if (PrintProgress != null) {
					PrintProgress.Invoke (this, new PdfPrintProgressEventArgs(false));
				}
			}
		}

		public void OnProgressChange(nsIWebProgress aWebProgress, nsIRequest aRequest, int aCurSelfProgress, int aMaxSelfProgress, int aCurTotalProgress, int aMaxTotalProgress)
		{
		}

		public void OnLocationChange(nsIWebProgress aWebProgress, nsIRequest aRequest, nsIURI aLocation, uint flags)
		{
		}

		public void OnStatusChange(nsIWebProgress aWebProgress, nsIRequest aRequest, int aStatus, string aMessage)
		{
		}

		public void OnSecurityChange(nsIWebProgress aWebProgress, nsIRequest aRequest, uint aState)
		{
		}
		#endregion
	}

	public class PdfPrintProgressEventArgs
		: EventArgs
	{
		public readonly bool PrintInProgress;
		public PdfPrintProgressEventArgs(bool printInProgress)
		{
			PrintInProgress = printInProgress;
		}
	}
}
