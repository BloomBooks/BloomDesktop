using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Gecko;
using Palaso.IO;

namespace Bloom.Publish
{
	/* Why is this a component? just becuase the geckobrowser that we use, even though it is invisible,
	 * expects to be operating on the UI thread, getting events from the Application.DoEvents() loop, etc.
	 * So we can't be making pdfs on a background thread. Having it as a component with a timer and
	 * an event to signal when it is done seems like a natural way to pull this off.
	*/
	public partial class GeckoPdfComponent : Component
	{
		public event EventHandler PdfReady;
		private GeckoWebBrowser _browser;
		private string _pathToOutputPDF;
		private string _pathToTempPdf;
		private bool _browserHandleCreated;

		public GeckoPdfComponent()
		{
			InitializeComponent();
		}

		public GeckoPdfComponent(IContainer container)
		{
			container.Add(this);

			InitializeComponent();
		}

		public void Start(Browser browser,/*string inputHtmlPath,*/ string outputPdfPath, string paperSizeName, bool landscape)
		{
			_pathToOutputPDF = outputPdfPath;
			_pathToTempPdf = TempFile.WithExtension(".pdf").Path;
			File.Delete(_pathToOutputPDF);
			File.Delete(_pathToTempPdf);
			//Navigate(inputHtmlPath);
			_browser = browser.WebBrowser;
			_checkForBrowserNavigatedTimer.Enabled = true;
		}

//        private void Navigate(string inputHtmlPath)
//        {
//            MakeNewBrowser();
//            _browser.Navigate(inputHtmlPath);
//            _checkForBrowserNavigatedTimer.Enabled = true;
//        }

		/// <summary>
		/// we need to wait for this to happen before we can proceed to navigate to the page
		/// </summary>
		void OnBrowser_HandleCreated(object sender, EventArgs e)
		{
			_browserHandleCreated = true;
		}

//        private void MakeNewBrowser()
//        {
//            _browser = new GeckoWebBrowser();
//            _browser.HandleCreated += new EventHandler(OnBrowser_HandleCreated);
//            _browser.CreateControl();
//            var giveUpTime = DateTime.Now.AddSeconds(2);
//            while (!_browserHandleCreated && DateTime.Now < giveUpTime)
//            {
//                //TODO: could lead to hard to reproduce bugs
//                Application.DoEvents();
//                Thread.Sleep(100);
//            }
//        }
		private void StartMakingPdf()
		{
			nsIWebBrowserPrint print = Xpcom.QueryInterface<nsIWebBrowserPrint>(_browser.Window.DomWindow);

			var service = Xpcom.GetService<nsIPrintSettingsService>("@mozilla.org/gfx/printsettings-service;1");
			var printSettings = service.GetNewPrintSettingsAttribute();

			printSettings.SetToFileNameAttribute(_pathToTempPdf);
			printSettings.SetPrintSilentAttribute(true);
			printSettings.SetPaperNameAttribute("a6");
			printSettings.SetDownloadFontsAttribute(true);
			printSettings.SetOutputFormatAttribute(2); // 2 == kOutputFormatPDF

			//TODO: How do you use the progres parameter here to know when it is done?
			print.Print(printSettings, null);
			_checkForPdfFinishedTimer.Enabled = true;
		}

		private void FinishMakingPdf()
		{
			if (!File.Exists(_pathToTempPdf))
				throw new ApplicationException("Bloom was not able to create the PDF.\r\n\r\nDetails: Gecko did not produce the expected document.");

			try
			{
				File.Move(_pathToTempPdf, _pathToOutputPDF);
			}
			catch (IOException e)
			{
				//TODO: we can get here for a different reason: the source file is still in use
				throw new ApplicationException(
						string.Format("Bloom tried to save the file to {0}, but Windows said that it was locked. Please try again.\r\n\r\nDetails: {1}",
									  _pathToOutputPDF, e.Message));

			}
			RaisePdfReady(null);
		}

		private void OnCheckForBrowserNavigatedTimerTick(object sender, EventArgs e)
		{
			if (_browser.Document != null && _browser.Document.ActiveElement != null)
			{
				_checkForBrowserNavigatedTimer.Enabled = false;
				StartMakingPdf();
			}
		}

		private void RaisePdfReady(EventArgs e)
		{
			EventHandler handler = PdfReady;
			if (handler != null) handler(this, e);
		}

		private void _checkForPdfFinishedTimer_Tick(object sender, EventArgs e)
		{
			//TODO: use progress thingy to know when this is done, rather than just giving it 4 seconds.
			if (File.GetLastWriteTime(_pathToTempPdf).AddSeconds(10) < DateTime.Now)
			{
				_checkForPdfFinishedTimer.Enabled = false;
				FinishMakingPdf();
			}
		}

	}
}
