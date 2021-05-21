// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Gecko;
using Gecko.Interop;
using L10NSharp;
using Microsoft.Win32;
using SIL.IO;
#if !__MonoCS__
#endif

namespace Bloom.Publish.PDF
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
#if __MonoCS__
		public event EventHandler PrintFinished;
#endif
		//private PdfPrintProgressListener _listener;
		private string _pdfPath;
		private bool _haveShownAdobeReaderRecommendation;

		public PdfViewer()
		{
			InitializeComponent();

#if !__MonoCS__
			// In Windows we would prefer to use Acrobat to display and print PDFs. It avoids various bugs in
			// PDFjs, such as BL-1177 (Andika sometimes lost when printing directly from Bloom),
			// BL-1170 Printing stops after certain point
			// BL-1037 PDFjs sometimes fails to display if use certain jpg images
			// If Acrobat is not installed, it will fall back to PDFjs, and we hope for the best.
			// Todo: we need a better solution in Linux, also. Ghostscript might provide something but
			// has a GPL license.
			_pdfViewerControl = new AdobeReaderControl();
#else
			_pdfViewerControl = new GeckoWebBrowser();
#endif
			SetupViewerControl();
		}

		private void SetupViewerControl()
		{
			if (_pdfViewerControl is GeckoWebBrowser)
			{
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

#if(!__MonoCS__)
		private void UpdatePdfViewer(Control viewerControl)
		{
			Controls.Remove(_pdfViewerControl);
			_pdfViewerControl.Dispose();
			_pdfViewerControl = viewerControl;
			SetupViewerControl();
			_pdfViewerControl.Size = this.Size; // not sure why dock doesn't do this
		}
#endif

		public bool ShowPdf(string pdfFile)
		{
			_pdfPath = pdfFile;
#if !__MonoCS__
			var arc = _pdfViewerControl as AdobeReaderControl;
			if (arc != null) // We haven't yet had a problem displaying with Acrobat...
			{
				if (arc.ShowPdf(pdfFile))
					return true; // success using acrobat
				// Acrobat not working (probably not installed). Switch to using Gecko to display PDF.
				UpdatePdfViewer(new GeckoWebBrowser());
				// and continue to show it using that.
			}
#endif
			// Escaping the filename twice for characters like # is needed in order to get the
			// pdf filename through Geckofx/xulrunner to our local server on Linux.  This is to
			// prevent the filename from being cut short at the # character.  As far as I can
			// tell, Linux xulrunner strips one level of escaping on input, then before passing
			// the query on to the localhost server it truncates the query portion at the first
			// # it sees.  The localhost processor expects one level of encoding, and we deal
			// with having a # in the query (file path) there without any problem.  You may
			// regard this double escaping as a hack to get around the Linux xulrunner which
			// behaves differently than the Windows xulrunner.  It is an exception to the rule
			// of matching EscapeFileNameForHttp() with UnescapeFileNameForHttp().  See a comment in
			// https://jira.sil.org/browse/BL-951 for a description of the buggy program
			// behavior without this hack.
			var file = pdfFile;
			if (SIL.PlatformUtilities.Platform.IsUnix)
				file = file.EscapeFileNameForHttp().EscapeFileNameForHttp();
			var url = string.Format("{0}{1}?file=/bloom/{2}",
				Api.BloomServer.ServerUrlWithBloomPrefixEndingInSlash,
				FileLocationUtilities.GetFileDistributedWithApplication("pdf/web/viewer.html"),
				file);

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

#if !__MonoCS__
				if (!_haveShownAdobeReaderRecommendation)
				{
					_haveShownAdobeReaderRecommendation = true;
					var message = LocalizationManager.GetString("PublishTab.Notifications.AdobeReaderRecommendation",
						"This PDF viewer can be improved by installing the free Adobe Reader on this computer.");
					RunJavaScript("toastr.remove();" +
								  "toastr.options = { 'positionClass': 'toast-bottom-right','timeOut': '15000'};" +
								  "toastr['info']('" + message + "')");
				}
#endif
			};
			return true;
		}

		public string RunJavaScript(string script)
		{
			var browser = ((GeckoWebBrowser)_pdfViewerControl);
			Debug.Assert(!InvokeRequired);
			Debug.Assert(browser.Window != null);
			if (browser.Window != null)
			{
				using (var context = new AutoJSContext(browser.Window))
				{
					string result;
					context.EvaluateScript(script, (nsISupports)browser.Document.DomObject, out result);
					return result;
				}
			}
			return null;
		}

		public void Print()
		{
#if !__MonoCS__
			var arc = _pdfViewerControl as AdobeReaderControl;
			if (arc != null)
			{
				// The print button is only enabled after we have generated a PDF and tried to display it,
				// so if we still have an ARC by this point, it displayed successfully, and presumably can also print.
				arc.Print();
				return;
			}

			// PDFjs printing has proved unreliable, so GhostScript is preferable even on Windows.
			if (TryGhostcriptPrint())
				return;

			var browser = ((GeckoWebBrowser)_pdfViewerControl);
			using (AutoJSContext context = new AutoJSContext(browser.Window))
			{
				string result;
				context.EvaluateScript(@"window.print()", (nsISupports)browser.Document.DomObject, out result);
			}
#else
			// on Linux the isntaller will have a dependency on GhostScript so it should always be available.
			// We've had many problems with PDFJs so hopefully this solves them.
			if (TryGhostcriptPrint())
				return;

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

		private bool TryGhostcriptPrint()
		{
			string systemSpecificArgs = String.Empty;
#if __MonoCS__
			// Ghostscript is built into the CUPS printer service, which is the standard
			// setup in Ubuntu.  It handles PDF automatically.  gtklp is a graphical
			// front end to the printer service that allows the user to specify the
			// printer, paper size, and other parameters that may need to be tweaked.
			var exePath = "/usr/bin/gtklp";
			systemSpecificArgs = "";
#else
			var gsKey = Registry.LocalMachine.OpenSubKey(@"Software\GPL Ghostscript");
			if (gsKey == null)
				gsKey = Registry.LocalMachine.OpenSubKey(@"Software\AGPL Ghostscript");
			// Just possibly the paid version is present?
			if (gsKey == null)
			{
				var hklm64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
				// maybe the 64-bit version is installed?
				gsKey = hklm64.OpenSubKey(@"Software\GPL Ghostscript");
				if (gsKey == null)
					gsKey = hklm64.OpenSubKey(@"Software\AGPL Ghostscript");
			}
			if (gsKey == null)
				return false; // not able to print this way, GhostScript not present
			string exePath = null;
			foreach (var version in gsKey.GetSubKeyNames())
			{
				var gsVersKey = gsKey.OpenSubKey(version);
				var dllPath = gsVersKey.GetValue("GS_DLL") as String;
				if (dllPath == null)
					continue;
				if (!RobustFile.Exists(dllPath))
					continue; // some junk there??
				exePath = Path.Combine(Path.GetDirectoryName(dllPath), "gswin32c.exe");
				if (RobustFile.Exists(exePath))
					break;
				exePath = Path.Combine(Path.GetDirectoryName(dllPath), "gswin64c.exe");
				if (RobustFile.Exists(exePath))
					break;
				// some old install in a bad state? Try another subkey
			}
			// -sDEVICE#mswinpr2 makes it display a print dialog so the user can choose printer.
			// -dBATCH -dNOPAUSE -dQUIET make it go ahead without waiting for user input on each page or after last
			// -dQUIET was an attempt to prevent it display per-page messages. Didn't work. Not sure it does any good.
			// -dNORANGEPAGESIZE makes it automatically select the right page orientation.
			systemSpecificArgs = "-sDEVICE#mswinpr2 -dBATCH -dNOPAUSE -dQUIET -dNORANGEPAGESIZE ";
#endif
			if (exePath == null || !RobustFile.Exists(exePath))
				return false; // Can't use ghostscript approach
			var proc = new Process
			{
				StartInfo =
				{
					FileName = exePath,
					Arguments = systemSpecificArgs + "\"" + _pdfPath + "\"",
					UseShellExecute = false, // enables CreateNoWindow
					CreateNoWindow = true // don't need a DOS box (does not suppress print dialog)
				}
			};
#if __MonoCS__
			proc.EnableRaisingEvents = true;
			proc.Exited += PrintProcessExited;
			// gtklp may use a different GTK version than Bloom: clear the library path, which will cause
			// gtklp to do the right lookup.  See https://issues.bloomlibrary.org/youtrack/issue/BL-9957.
			proc.StartInfo.Environment.Remove("LD_LIBRARY_PATH");
#endif
			proc.Start();
			return true; // we at least think we printed it (unless the user cancels...anyway, don't try again some other way).
		}

#if __MonoCS__
		void PrintProcessExited(object sender, EventArgs e)
		{
			if (PrintFinished != null)
				PrintFinished(sender, e);
		}
#endif

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
			using (AutoJSContext context = new AutoJSContext (browser.Window)) {
				nsISupports domWindow = browser.Window.DomWindow;
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
						throw;
				}
				finally {
					Marshal.ReleaseComObject (print);
				}
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
