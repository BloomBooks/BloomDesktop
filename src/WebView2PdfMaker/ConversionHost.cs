using System;
using System.Windows.Forms;

namespace WebView2PdfMaker
{
	public partial class ConversionHost : Form
	{
		Options _options;
		private WebView2PdfComponent _pdfMaker;

		public ConversionHost(Options options)
		{
			_options = options;
			InitializeComponent();
			_pdfMaker = new WebView2PdfComponent(this.components, options);
			_pdfMaker.Finished += new System.EventHandler(OnPdfMaker_Finished);
			if (_options.NoUIMode)
			{
				this.WindowState = FormWindowState.Minimized;
				this.ShowInTaskbar = false;
			}
			this.Load += new System.EventHandler(ConversionHost_Load);
		}

		private void ConversionHost_Load(object sender, EventArgs e)
		{
			Text = "Working...";
			if (_options.NoUIMode)
			{
				Console.WriteLine("Status: Started|Percent: 0");
			}
			else
			{
				_statusLabel.Text = "Started";
			}
			_pdfMaker.Start(_options);
		}

		private void OnPdfMaker_Finished(object sender, EventArgs e)
		{
			if (_options.NoUIMode)
			{
				Console.WriteLine("Status: Finished|Percent: 100");
			}
			else
			{
				_statusLabel.Text = "Finished";
			}
			Close();
		}
	}
}
