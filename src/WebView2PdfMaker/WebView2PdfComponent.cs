using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Threading;
using System.Drawing;

namespace WebView2PdfMaker
{
	public partial class WebView2PdfComponent : Component
	{
		private System.Windows.Forms.Timer _checkForPdfFinishedTimer;
		private System.Windows.Forms.Timer _checkForBrowserNavigatedTimer;
		private WebView2 _webview;
		private bool _readyToNavigate;
		private bool _navigationCompleted;
		private Uri _uriOfDocument;
		private Options _options;
		CoreWebView2PrintSettings _printSettings;
		DateTime _startMakingPdf;
		public event EventHandler Finished;

		public WebView2PdfComponent(IContainer container, Options options)
		{
			if (options.Debug)
				Console.Out.WriteLine("Constructing WebView2PdfComponent");

			if (container != null)
				container.Add(this);
			_options = options;

			InitializeComponent();
			CreateTimers();
			CreateWebView2();
			_webview.CoreWebView2InitializationCompleted += (object sender, CoreWebView2InitializationCompletedEventArgs args) =>
			{
				if (_options.Debug)
					Console.Out.WriteLine("WebView2PdfComponent - CoreWebView2InitializationCompleted");
				_webview.CoreWebView2.NavigationCompleted += (object sender2, CoreWebView2NavigationCompletedEventArgs args2) =>
				{
					if (_options.Debug)
						Console.Out.WriteLine("WebView2PdfComponent - NavigationCompleted");
					_navigationCompleted = true;
				};
				_readyToNavigate = true;
			};
			var initTask = InitWebView();
			if (_options.Debug)
				Console.Out.WriteLine("WebView2PdfComponent.ctor initTask ready to wait");
			while (!initTask.IsCompleted)
			{
				Application.DoEvents();
				Thread.Sleep(100);
				if (_options.Debug)
					Console.Out.WriteLine("WebView2PdfComponent.ctor slept 100ms waiting for initTask.IsCompleted");
			}
			if (_options.Debug)
				Console.Out.WriteLine("WebView2PdfComponent - initTask.IsCompleted!");
			EnsureBrowserReadyToNavigate();
		}

		protected void EnsureBrowserReadyToNavigate()
		{
			if (_options.Debug)
				Console.Out.WriteLine("WebView2PdfComponent.EnsureBrowserReadyToNavigate Start");
			while (!_readyToNavigate)
			{
				Application.DoEvents();
				Thread.Sleep(100);
				if (_options.Debug)
					Console.Out.WriteLine("WebView2PdfComponent.EnsureBrowserReadyToNavigate slept 100ms");
			}
			if (_options.Debug)
				Console.Out.WriteLine("WebView2PdfComponent.EnsureBrowserReadyToNavigate _readyToNavigate finally set!");
		}

		private void CreateTimers()
		{
			_checkForPdfFinishedTimer = new System.Windows.Forms.Timer(this.components);
			_checkForPdfFinishedTimer.Tick += new System.EventHandler(OnCheckForPdfFinishedTimer_Tick);

			_checkForBrowserNavigatedTimer = new System.Windows.Forms.Timer(this.components);
			_checkForBrowserNavigatedTimer.Interval = 50;
			_checkForBrowserNavigatedTimer.Tick += new System.EventHandler(OnCheckForBrowserNavigatedTimerTick);
		}

		private void CreateWebView2()
		{
			_webview = new WebView2();
			((ISupportInitialize)(_webview)).BeginInit();
			_webview.BackColor = System.Drawing.Color.White;
			_webview.CreationProperties = null;
			_webview.DefaultBackgroundColor = System.Drawing.Color.White;
			_webview.Dock = DockStyle.Fill;
			_webview.Location = new System.Drawing.Point(0, 0);
			_webview.Name = "_webview";
			_webview.Size = new System.Drawing.Size(150, 150);
			_webview.TabIndex = 0;
			_webview.ZoomFactor = 1D;
			((ISupportInitialize)(_webview)).EndInit();
			this.components.Add(_webview);
		}

		private static bool _clearedCache;
		private async Task InitWebView()
		{
			var op = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
			var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(),"WebView2PdfMaker"), op);
			await _webview.EnsureCoreWebView2Async(env);
			if (!_clearedCache)
			{
				_clearedCache = true;
				// The intent here is that none of Bloom's assets should be cached from one run of the program to another
				// (in case a new version of Bloom has been installed).
				// OTOH, I don't want to clear things so drastically as to preclude using local storage or cookies.
				// The doc is unclear as to the distinction between CacheStorage and DiskCache, but I _think_
				// this should clear what we need and nothing else.
				await _webview.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.CacheStorage | CoreWebView2BrowsingDataKinds.DiskCache);
			}
		}


		private void OnCheckForBrowserNavigatedTimerTick(object sender, EventArgs e)
		{
			if (_uriOfDocument != null && _navigationCompleted)
			{
				_checkForBrowserNavigatedTimer.Enabled = false;
				StartMakingPdf();
			}
		}

		private void OnCheckForPdfFinishedTimer_Tick(object sender, EventArgs e)
		{
			if (_pdfTask.IsCompleted)
			{
				_checkForPdfFinishedTimer.Enabled = false;
				FinishMakingPdf();
			}
		}

		private void RaiseFinished()
		{
			Finished?.Invoke(this, EventArgs.Empty);
		}
		private void FinishMakingPdf()
		{
			if (!File.Exists(_pathToTempPdf))
				throw new ApplicationException(string.Format(
					"WebView2PdfMaker was not able to create the PDF file ({0}).{1}{1}Details: WebView2 did not produce the expected document.",
					_pathToTempPdf, Environment.NewLine));

			try
			{
				File.Move(_pathToTempPdf, _options.OutputPdfPath);
				RaiseFinished();
			}
			catch (IOException e)
			{
				// We can get here for a different reason: the source file is still in use
				throw new ApplicationException(
					string.Format(
						"Tried to move the file {0} to {1}, but the Operating System said that one of these files was locked. Please try again.{2}{2}Details: {3}",
						_pathToTempPdf, _options.OutputPdfPath, Environment.NewLine, e.Message));
			}
		}

		private string _pathToTempPdf;

		/// <summary>
		/// On the application event thread, work on creating the pdf. Will raise the Finished events
		/// </summary>
		public void Start(Options options)
		{
			if (_options.Debug)
				Console.Out.WriteLine("Begin WebView2PdfComponent.Start()");
			_options = options;

			var tempFileName = Path.GetTempFileName();
			_pathToTempPdf = tempFileName + ".pdf";
			File.Delete(tempFileName);
			File.Delete(_pathToTempPdf);
			File.Delete(_options.OutputPdfPath);
			_webview.Size = new Size(1920, 1320);
			_uriOfDocument = new Uri(_options.InputHtmlUri);
			_navigationCompleted = false;
			_webview.CoreWebView2.Navigate(_uriOfDocument.AbsoluteUri);
			_checkForBrowserNavigatedTimer.Enabled = true;
		}

		Task _pdfTask;
		private void StartMakingPdf()
		{
			_printSettings = _webview.CoreWebView2.Environment.CreatePrintSettings();
			// The orientation can be portrait or landscape.  (The default orientation is portrait.)
			_printSettings.Orientation = _options.GetWebView2Orientation();
			// The scale factor is a value between 0.1 and 2.0.  (The default is 1.0.)
			_printSettings.ScaleFactor = 1.0D;	/* implement --zoom? */
			// The page width in inches.  (The default width is 8.5 inches.)
			_printSettings.PageWidth = _options.GetWebView2PageWidth();
			// The page height in inches.  (The default height is 11 inches.)
			_printSettings.PageHeight = _options.GetWebView2PageHeight();
			// The top margin in inches.  (The default is 1 cm, or ~0.4 inches.)
			_printSettings.MarginTop = _options.GetWebView2TopMargin();
			// The bottom margin in inches.  (The default is 1 cm, or ~0.4 inches.)
			_printSettings.MarginBottom = _options.GetWebView2BottomMargin();
			// The left margin in inches.  (The default is 1 cm, or ~0.4 inches.)
			_printSettings.MarginLeft = _options.GetWebView2LeftMargin();
			// The right margin in inches.  (The default is 1 cm, or ~0.4 inches.)
			_printSettings.MarginRight = _options.GetWebView2RightMargin();
			// true if background colors and images should be printed.  (The default value is false.)
			_printSettings.ShouldPrintBackgrounds = false;
			// true if only the current end user's selection of HTML in the document should be printed.  (The default value is false.)
			_printSettings.ShouldPrintSelectionOnly = false;
			// true if header and footer should be printed.  (The default value is false.)
			// The height of the header and footer is 0.5 cm, or ~0.2 inches.
			_printSettings.ShouldPrintHeaderAndFooter = false;
			// The title in the header if Microsoft.Web.WebView2.Core.CoreWebView2PrintSettings.ShouldPrintHeaderAndFooter is true.
			// (The default value is the title of the current document.)
			_printSettings.HeaderTitle = "";
			// The URI in the footer if Microsoft.Web.WebView2.Core.CoreWebView2PrintSettings.ShouldPrintHeaderAndFooter is true.
			// (The default value is the current URI.)
			_printSettings.FooterUri = "";
			if (_options.Debug)
				Console.Out.WriteLine("DEBUG StartMakingPdf(): Width={0}, Height={1}; Margins Top={2}, Bottom={3}, Left={4}, Right={5}; Orientation={6}",
					_printSettings.PageWidth, _printSettings.PageHeight,
					_printSettings.MarginTop, _printSettings.MarginBottom, _printSettings.MarginLeft, _printSettings.MarginRight,
					_printSettings.Orientation);

			_pdfTask = _webview.CoreWebView2.PrintToPdfAsync(_pathToTempPdf, _printSettings);

			_startMakingPdf = DateTime.Now;
			_checkForPdfFinishedTimer.Enabled = true;

		}
	}
}
