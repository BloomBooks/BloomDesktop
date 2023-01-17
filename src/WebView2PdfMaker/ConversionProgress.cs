using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WebView2PdfMaker
{
	public partial class ConversionProgress : Form
	{
		Options _options;
		private WebView2PdfComponent _pdfMaker;

		public ConversionProgress(Options options)
		{
			_options = options;
			InitializeComponent();
			_pdfMaker = new WebView2PdfComponent(this.components);
			_pdfMaker.Finished += new System.EventHandler(OnPdfMaker_Finished);
			//_pdfMaker.StatusChanged += new System.EventHandler<PdfMakingStatus>(this.OnPdfMaker_StatusChanged);
			_progressBar.Maximum = 100;
			if (_options.NoUIMode)
			{
				this.WindowState = FormWindowState.Minimized;
				this.ShowInTaskbar = false;
			}
			this.Load += new System.EventHandler(ConversionProgress_Load);

		}

		private void ConversionProgress_Load(object sender, EventArgs e)
		{
			Text = "Working...";
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
				//on windows 7 (at least) you won't see 100% if you close before the system has had a chance to "animate" the increase. 
				//On very short documents, you won't see it get past around 20%. Now good. So, the
				//trick here is to go *down* to 99, that going downwards makes it skip the animation delay.
				_progressBar.Value = 100;
				_progressBar.Value = 99;

			}
			Close();
		}
	}
}
