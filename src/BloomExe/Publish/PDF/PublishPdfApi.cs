using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.MiscUI;
using Bloom.Properties;
using DesktopAnalytics;
using L10NSharp;
using SIL.Reporting;
using Application = System.Windows.Forms.Application;

namespace Bloom.Publish.PDF
{
	public class PublishPdfApi
	{
		private readonly BloomWebSocketServer _webSocketServer;
		private PublishModel _publishModel;
		private BackgroundWorker _makePdfBackgroundWorker;

		private string PdfFilePath => _publishModel.PdfFilePath;

		// This is to support orientation/page size changing during publishing, but we're not doing
		// that yet, so just defer to the current book.
		public Layout PageLayout => _publishModel.PageLayout;

		public PublishModel.BookletPortions BookletPortion {
			get { return _publishModel.BookletPortion; }
			set
			{
				_publishModel.BookletPortion = value;
			}
		}

		public PublishPdfApi(BloomWebSocketServer webSocketServer, PublishModel model)
		{
			_webSocketServer = webSocketServer;
			_publishModel = model;
		}


		private const string kApiUrlPart = "publish/pdf/";

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "simple", HandleCreateSimplePdf, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "cover", HandleCreateCoverPdf, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "cancel", HandleCancel, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "pages", HandleCreatePagesPdf, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "printSettingsHelp", HandlePrintSettingsHelp, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "save", HandleSavePdf, true);
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "allowBooklet", request => _publishModel.AllowPdfBooklet, null, false);
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

		private void HandleCancel(ApiRequest request)
		{
			_makePdfBackgroundWorker.CancelAsync();
			request.PostSucceeded();
		}

		private void HandleSavePdf(ApiRequest request)
		{
			_publishModel.SavePdf();
			request.PostSucceeded();
		}

		private void HandlePrintSettingsHelp(ApiRequest request)
		{
			var needA4Paper = false;
			var needLandscape = false;
			var needLongEdge = false;
			var needCutAndStack = false;
			var needCutAndFold = false;
			var needHelps = true;
			dynamic result = new ExpandoObject();
			switch (PageLayout.SizeAndOrientation.PageSizeName)
			{
				case "A5":
					needA4Paper = true;
					if (PageLayout.SizeAndOrientation.IsLandScape)  // Portrait, long edge, A4; cut and stack
					{
						needLongEdge = true;
						needCutAndStack = true;
					}
					else                                            // Landscape, short edge, A4
					{
						needLandscape = true;
					}
					break;
				case "A6":
					needA4Paper = true;
					needCutAndFold = true;
					if (PageLayout.SizeAndOrientation.IsLandScape)  // Landscape, short edge, A4; cut and fold
						needLandscape = true;
					else                                            // Portrait, long edge, A4; cut and fold
						needLongEdge = true;
					break;
				case "HalfLetter":
					if (PageLayout.SizeAndOrientation.IsLandScape)	// Portrait, long edge, Letter; cut and stack
					{
						needLongEdge = true;
						needCutAndStack = true;
					}
					else                                            // Landscape, short edge, Letter
					{
						needLandscape = true;
					}
					break;
				case "QuarterLetter":
					if (PageLayout.SizeAndOrientation.IsLandScape)	// Landscape, short edge, Letter; cut and fold
						needLandscape = true;
					else                                            // Portrait, long edge, Letter; cut and fold
						needLongEdge = true;
					needCutAndFold = true;
					break;
				default:
					needHelps = false;
					result.note = "This paper size is not recommended for booklets.";
					break;

			}
			if (needCutAndFold)
				result.note = LocalizationManager.GetString("PublishTab.PDF.Booklet.FourPages.CutAndFold",
						"This will put four pages on each sheet of paper. Cut the stack, then fold to get two booklets.");
			else if (needCutAndStack)
				result.note = LocalizationManager.GetString("PublishTab.PDF.Booklet.TwoPages.CutAndStack",
						"This will put two pages on each sheet of paper. Cut horizontally and stack to make one booklet.");
			if (needHelps)
			{
				result.helps = new string[4];
				if (needLandscape)
					result.helps[0] = LocalizationManager.GetString("PublishTab.PDF.Booklet.Layout.Landscape", "1. Layout: Landscape");
				else
					result.helps[0] = LocalizationManager.GetString("PublishTab.PDF.Booklet.Layout.Portrait", "1. Layout: Portrait");
				if (needLongEdge)
					result.helps[1] = LocalizationManager.GetString("PublishTab.PDF.Booklet.Flip.LongEdge", "2. Print on both sides: \"Flip on long edge\"");
				else
					result.helps[1] = LocalizationManager.GetString("PublishTab.PDF.Booklet.Flip.ShortEdge", "2. Print on both sides: \"Flip on short edge\"");
				if (needA4Paper)
					result.helps[2] = LocalizationManager.GetString("PublishTab.PDF.Booklet.PaperSize.A4", "3. More settings > Paper size: \"A4\"");
				else
					result.helps[2] = LocalizationManager.GetString("PublishTab.PDF.Booklet.PaperSize.Letter", "3. More settings > Paper size: \"Letter\"");
				result.helps[3] = LocalizationManager.GetString("PublishTab.PDF.Booklet.Scale.Actual Size", "4. More settings > Scale: \"Actual size\"");
			}
			request.ReplyWithJson(result);
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

		// Make the actual PDF file with current settings. Slightly adapted from the PublishView code
		// that calls PublishModel.LoadBook.
		public void MakePdf()
		{
			var shell = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
			_makePdfBackgroundWorker = new BackgroundWorker();
			_makePdfBackgroundWorker.WorkerReportsProgress = true;
			_makePdfBackgroundWorker.WorkerSupportsCancellation = true;
			_makePdfBackgroundWorker.RunWorkerCompleted += _makePdfBackgroundWorker_RunWorkerCompleted;
			_makePdfBackgroundWorker.DoWork += ((sender, doWorkEventArgs) =>
			{
				_publishModel.LoadBook(sender as BackgroundWorker, doWorkEventArgs, shell);
			});
			_makePdfBackgroundWorker.RunWorkerAsync();
		}

		void _makePdfBackgroundWorker_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
		{
			_publishModel.PdfGenerationSucceeded = false;
			if (e.Cancelled)
			{
				dynamic messageBundleCancel = new DynamicJson();
				messageBundleCancel.path = "";
				_webSocketServer.SendBundle("publish", "pdfReady", messageBundleCancel);
			} else
			{
				if (e.Result is Exception)
				{
					PublishModel.ReportPdfGenerationError(e.Result as Exception);
					dynamic messageBundleCancel = new DynamicJson();
					messageBundleCancel.path = "";
					_webSocketServer.SendBundle("publish", "pdfReady", messageBundleCancel);
					return;
				}
				_publishModel.PdfGenerationSucceeded = true;
				dynamic messageBundle = new DynamicJson();
				messageBundle.path = PdfFilePath.ToLocalhost();
				_webSocketServer.SendBundle("publish", "pdfReady", messageBundle);
			}
		}
	}
}
