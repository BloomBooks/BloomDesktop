using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using Bloom.Api;
using Bloom.Book;
using Bloom.Properties;
using DesktopAnalytics;
using L10NSharp;
using SIL.IO;
using SIL.Reporting;
using Application = System.Windows.Forms.Application;

namespace Bloom.Publish.PDF
{
	public class PublishPdfApi
	{
		private PdfMaker _pdfMaker;
		private readonly BloomWebSocketServer _webSocketServer;
		private PublishModel _publishModel;

		private string PdfFilePath => _publishModel.PdfFilePath;

		// This is to support orientation/page size changing during publishing, but we're not doing
		// that yet, so just defer to the current book.
		public Layout PageLayout => CurrentBook?.GetLayout() ?? Layout.A5Portrait;

		public PublishModel.BookletPortions BookletPortion {
			get { return _publishModel.BookletPortion; }
			set
			{
				_publishModel.BookletPortion = value;
			}
		}

		public PublishPdfApi(PdfMaker pdfMaker, BloomWebSocketServer webSocketServer, PublishModel model)
		{
			_pdfMaker = pdfMaker;
			_webSocketServer = webSocketServer;
			_publishModel = model;
		}


		private const string kApiUrlPart = "publish/pdf/";

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "simple", HandleCreateSimplePdf, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "cover", HandleCreateCoverPdf, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "pages", HandleCreatePagesPdf, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "printSettingsPath", HandlePrintSettingsPath, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "save", HandleSavePdf, true);
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "allowBooklet", request => AllowPdfBooklet, null, false);
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "allowFullBleed",
				request => CurrentBook?.FullBleed ?? false, null, false);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "printAnalytics", HandlePrintAnalytics, true);
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "fullBleed",
				request => (CurrentBook?.FullBleed ?? false) && (CurrentBook?.UserPrefs.FullBleed ?? false),
				((writeRequest, value) =>
				{
					if (CurrentBook != null)
					{
						CurrentBook.UserPrefs.FullBleed = value;
					}
				}), false);
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "cmyk",
				request => CurrentBook?.UserPrefs.CmykPdf ?? false,
				((writeRequest, value) =>
				{
					if (CurrentBook != null)
					{
						CurrentBook.UserPrefs.CmykPdf = value;
					}
				}), false);
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "dontShowSamplePrint",
				request => Settings.Default.DontShowPrintNotification,
				((writeRequest, value) =>
				{
					Settings.Default.DontShowPrintNotification = value;
					Settings.Default.Save();
				}), false);
		}

		private void HandleSavePdf(ApiRequest request)
		{
			_publishModel.SavePdf();
			request.PostSucceeded();
		}

		/// <summary>
		/// Gets the url of the image we should show (if any) to help the user configure
		/// the printer to correctly print a booklet.
		/// </summary>
		private void HandlePrintSettingsPath(ApiRequest request)
		{
			var printSettingsPreviewFolder =
				FileLocationUtilities.GetDirectoryDistributedWithApplication("printer settings images");
			var printSettingsSamplePrefix = Path.Combine(printSettingsPreviewFolder,
				PageLayout.SizeAndOrientation + "-" + (isBooklet() ? "Booklet-" : ""));
			string printSettingsSampleName = null;
			if (printSettingsSampleName == null || !RobustFile.Exists(printSettingsSampleName))
				printSettingsSampleName = printSettingsSamplePrefix + LocalizationManager.UILanguageId + ".png";
			if (!RobustFile.Exists(printSettingsSampleName))
				printSettingsSampleName = printSettingsSamplePrefix + "en" + ".png";
			if (Settings.Default.DontShowPrintNotification || !RobustFile.Exists(printSettingsSampleName))
				printSettingsSampleName = "";
			else
				printSettingsSampleName = printSettingsSampleName.ToLocalhost();
			request.ReplyWithText(printSettingsSampleName);
		}

		private void HandlePrintAnalytics(ApiRequest request)
		{
			Analytics.Track("Print PDF", new Dictionary<string, string>()
			{
				{ "BookId", CurrentBook.ID },
				{ "Country", CurrentBook.CollectionSettings.Country }
			});
			CurrentBook.ReportSimplisticFontAnalytics(FontAnalytics.FontEventType.PublishPdf, "Print PDF");

			request.PostSucceeded();
		}

		private bool isBooklet()
		{
			return BookletPortion == PublishModel.BookletPortions.BookletCover
			       || BookletPortion == PublishModel.BookletPortions.BookletPages;
		}

		private void HandleCreatePagesPdf(ApiRequest request)
		{
			BookletPortion = PublishModel.BookletPortions.BookletPages;
			MakePdf();
			request.PostSucceeded();
		}

		private void HandleCreateCoverPdf(ApiRequest request)
		{
			BookletPortion = PublishModel.BookletPortions.BookletCover;
			MakePdf();
			request.PostSucceeded();
		}

		private void HandleCreateSimplePdf(ApiRequest request)
		{
			BookletPortion = PublishModel.BookletPortions.AllPagesNoBooklet;
			MakePdf();
			request.PostSucceeded();
		}

		private Book.Book CurrentBook => _publishModel.CurrentBook;

		/// <summary>
		/// Is it reasonable to make a booklet out of the current book with the current settings?
		/// Roughly duplicates a method of PublishModel, though that includes a concept of
		/// AllowPdf which returns CanPublish which is derived from DeterminePublishability.
		/// (But that has to do with whether the book can be published at all, not whether
		/// it can be published as a booklet.)
		/// </summary>
		public bool AllowPdfBooklet
		{
			get
			{
				// Large page sizes can't make booklets.  See http://issues.bloomlibrary.org/youtrack/issue/BL-4155.
				var size = PageLayout.SizeAndOrientation.PageSizeName;
				return CurrentBook.BookInfo.BookletMakingIsAppropriate &&
				       (size != "A4" && size != "A3" && size != "B5" && size != "Letter" && size != "Device16x9");
			}
		}

		// Make the actual PDF file with current settings. Slightly adapted from the PublishView code
		// that calls PublishModel.LoadBook.
		public void MakePdf()
		{
			var shell = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
			var worker = new BackgroundWorker();
			worker.WorkerReportsProgress = true;
			worker.WorkerSupportsCancellation = true;
			worker.DoWork += ((sender, doWorkEventArgs) =>
			{
				_publishModel.LoadBook(sender as BackgroundWorker, doWorkEventArgs, shell);
				dynamic messageBundle = new DynamicJson();
				messageBundle.path = PdfFilePath.ToLocalhost();
				_webSocketServer.SendBundle("publish", "pdfReady", messageBundle);
			});
			worker.RunWorkerAsync();
		}
	}
}
