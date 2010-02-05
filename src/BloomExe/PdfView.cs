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
using Skybound.Gecko;

namespace Bloom
{
	public partial class PdfView : UserControl
	{
	   protected GeckoWebBrowser _browser;
		bool _alreadyLoaded = false;
		protected string _htmlDocPath;
		private string _tempFile;

		public PdfView()
		{
			InitializeComponent();
			_browser = new GeckoWebBrowser();
			_browser.Parent = this;
			_browser.Dock = DockStyle.Fill;
			_tempFile = Path.GetTempFileName()+".pdf";
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
//            info.UseShellExecute = false;
//            info.CreateNoWindow = true;
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
