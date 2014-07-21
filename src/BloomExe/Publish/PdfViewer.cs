// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Bloom.Properties;
using Gecko;
using Palaso.IO;

namespace Bloom.Publish
{
	/// <summary>
	/// Wrapper class that wraps either a Gecko web browser that displays the PDF file through
	/// pdf.js, or the Adobe Reader control. Which one is used depends on the operating system
	/// (Linux always uses pdf.js) and the UseAdobePdfViewer setting in the config file.
	/// </summary>
	public partial class PdfViewer : UserControl
	{
		private Control _pdfViewerControl;

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
				stylesheet.CssRules.Add("#openFile, #print, #download, #viewBookmark, #pageRotateCw, #pageRotateCcw, #secondaryToolbarButtonContainer div:nth-of-type(2)  {display: none}");
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
#endif
            var browser = ((GeckoWebBrowser)_pdfViewerControl);
            using (AutoJSContext context = new AutoJSContext(browser.Window.JSContext))
            {
                string result;
                context.EvaluateScript(@"window.print()", (nsISupports)browser.Document.DomObject, out result);
            } 	
		}
	}
}
