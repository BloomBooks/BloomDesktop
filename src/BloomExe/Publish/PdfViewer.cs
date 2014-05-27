// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Drawing;
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

			((GeckoWebBrowser)_pdfViewerControl).Navigate(url);
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
			// TODO
			throw new NotImplementedException("This used to call _adobeReaderControl.Print, but that doesn't exist on GeckoBrowser");
		}
	}
}
