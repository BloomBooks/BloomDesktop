// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Bloom.Publish.PDF
{
	/// <summary>
	/// Wrapper class that wraps either a Gecko web browser that displays the PDF file through
	/// pdf.js, or the Adobe Reader control. Which one is used depends on the operating system
	/// (Linux always uses pdf.js) and the UseAdobePdfViewer setting in the config file.
	/// </summary>
	public partial class PdfViewer : UserControl
	{
		private Control _pdfViewerControl;
		public event EventHandler<PdfPrintProgressEventArgs> PrintProgress;
		private string _pdfPath;

		public PdfViewer()
		{
			InitializeComponent();

				// Review: do we still want to try Acrobat? We're trying to get rid of it,
				// so I'm inclined not to at least while WV2 is experimental.
				var wv2 = new WebView2Browser();
				_pdfViewerControl = wv2;

			SetupViewerControl();
		}

		private void SetupViewerControl()
		{
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

		private void UpdatePdfViewer(Control viewerControl)
		{
			Controls.Remove(_pdfViewerControl);
			_pdfViewerControl.Dispose();
			_pdfViewerControl = viewerControl;
			SetupViewerControl();
			_pdfViewerControl.Size = this.Size; // not sure why dock doesn't do this
		}

		public bool ShowPdf(string pdfFile)
		{
			_pdfPath = pdfFile;
				var wv2 = _pdfViewerControl as WebView2Browser;
				wv2.Navigate(pdfFile, false);
				return true;
		}

		public async Task PrintAsync()
		{
				var wv2 = _pdfViewerControl as WebView2Browser;
				await wv2.RunJavaScriptAsync("window.print()");
				return;
		}
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
