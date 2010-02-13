using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Bloom.Project;
using Skybound.Gecko;

namespace Bloom.Publish
{
	public partial class PdfView : UserControl
	{
		private readonly PdfModel _model;

		public delegate PdfView Factory();//autofac uses this

		protected GeckoWebBrowser _browser;
		bool _alreadyLoaded = false;
		protected string _htmlDocPath;
		private string _tempFile;

		public PdfView(PdfModel model)
		{
			InitializeComponent();
			if(this.DesignMode)
				return;
			_model = model;
			model.CurrentBookChanged += new EventHandler(OnCurrentBookChanged);
			_browser = new GeckoWebBrowser();
			_browser.Parent = this;
			_browser.Dock = DockStyle.Fill;
			_tempFile = Path.GetTempFileName()+".pdf";
		}

		void OnCurrentBookChanged(object sender, EventArgs e)
		{
			LoadNow();
		}

		public string DocumentPath
		{
			set
			{
				_htmlDocPath = value;

				if (_alreadyLoaded)
				{
					LoadNow();
				}
			}
		}

		private void LoadNow()
		{
			ProcessStartInfo info = new ProcessStartInfo("wkhtmltopdf.exe", string.Format("--print-media-type --page-width 14.5cm --page-height 21cm  --margin-bottom 0mm  --margin-top 0mm  --margin-left 0mm  --margin-right 0mm --disable-smart-shrinking {0} {1}",
																						  Path.GetFileName(_htmlDocPath), _tempFile));
			info.WorkingDirectory = Path.GetDirectoryName(_htmlDocPath) ;
			info.ErrorDialog = false;
			info.WindowStyle = ProcessWindowStyle.Hidden;

			try
			{
				Cursor = Cursors.WaitCursor;
				var proc = System.Diagnostics.Process.Start(info);
				proc.WaitForExit(1000);
			}
			catch (Exception e)
			{
				if(e.Message.Contains("not find"))
				{
					MessageBox.Show("To get pdfs, wkhtmltopdf must be installed and in the PATH environment variable.");
				}
				else
				{
					MessageBox.Show("There was a problem generating the pdf: "+Environment.NewLine+e.Message);
				}
				return;
			}
			finally
			{
				Cursor = Cursors.Default;
			}

			_browser.Navigate(_tempFile);
		}

		private void PdfView_Load(object sender, EventArgs e)
		{
			_alreadyLoaded = true;
			if (!string.IsNullOrEmpty(_htmlDocPath))
				LoadNow();
		}
	}
}